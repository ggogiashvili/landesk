using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanDesk.Core.Configuration;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Client used by the GUI to connect to the service's approval channel.
/// When the service receives an incoming connection, it sends "PENDING|remoteEndPoint";
/// this client shows the user a prompt and sends "APPROVE" or "REJECT".
/// </summary>
public class ApprovalClientService : IDisposable
{
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    /// <summary>Called when the service requests approval. Return true to approve, false to reject.</summary>
    public Func<string, Task<bool>>? OnApprovalRequested { get; set; }

    public ApprovalClientService(int port = NetworkConfiguration.ApprovalPort)
    {
        _port = port;
    }

    /// <summary>
    /// Starts connecting to the approval channel and processing requests (retries on disconnect).
    /// </summary>
    public void Start()
    {
        if (_runTask != null) return;
        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token));
        Logger.Info($"ApprovalClientService: Started (connecting to 127.0.0.1:{_port})");
    }

    /// <summary>
    /// Stops the client.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _client?.Close(); } catch { }
        _client = null;
        _reader = null;
        _writer = null;
        try { _runTask?.Wait(3000); } catch { }
        _runTask = null;
        Logger.Info("ApprovalClientService: Stopped");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(System.Net.IPAddress.Loopback, _port, cancellationToken);
                client.ReceiveTimeout = 0;
                client.SendTimeout = 5000;
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                _client = client;
                _reader = reader;
                _writer = writer;
                Logger.Info($"ApprovalClientService: Connected to approval channel on 127.0.0.1:{_port}");

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("PENDING|", StringComparison.OrdinalIgnoreCase)) continue;
                    var remoteEndPoint = trimmed.Length > 8 ? trimmed.Substring(8) : "unknown";
                    bool approve = false;
                    if (OnApprovalRequested != null)
                    {
                        try
                        {
                            approve = await OnApprovalRequested(remoteEndPoint);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"ApprovalClientService: OnApprovalRequested error: {ex.Message}");
                        }
                    }
                    var response = approve ? "APPROVE" : "REJECT";
                    await writer.WriteLineAsync(response);
                    await writer.FlushAsync();
                    Logger.Info($"ApprovalClientService: Sent {response} for {remoteEndPoint}");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.Debug($"ApprovalClientService: {ex.Message}");
            }
            finally
            {
                _client = null;
                _reader = null;
                _writer = null;
            }

            if (!cancellationToken.IsCancellationRequested)
                await Task.Delay(2000, cancellationToken); // Reconnect after delay
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
