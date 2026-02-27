using System.Net;
using System.Net.Sockets;
using System.Text;
using LanDesk.Core.Configuration;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Listens for the GUI approval client on localhost. When an incoming screen/input connection
/// arrives at the service, the service asks for approval via this channel; the GUI shows
/// Approve/Reject and sends the response.
/// </summary>
public class ConnectionApprovalService : IDisposable
{
    private TcpListener? _listener;
    private TcpClient? _approvalClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly object _clientLock = new object();
    private readonly int _port;
    private bool _isListening;
    private Task? _acceptTask;
    private volatile bool _waitingForApprovalResponse;
    private const int DefaultApprovalTimeoutMs = 60000; // 60 seconds

    public ConnectionApprovalService(int port = NetworkConfiguration.ApprovalPort)
    {
        _port = port;
    }

    /// <summary>
    /// Starts listening for the approval client (GUI) on 127.0.0.1
    /// </summary>
    public void Start()
    {
        if (_isListening) return;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _isListening = true;
            _acceptTask = Task.Run(AcceptLoop);
            Logger.Info($"ConnectionApprovalService: Listening on 127.0.0.1:{_port} for approval client (GUI)");
        }
        catch (Exception ex)
        {
            Logger.Error($"ConnectionApprovalService: Failed to start: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Stops the listener and disconnects any approval client
    /// </summary>
    public void Stop()
    {
        _isListening = false;
        lock (_clientLock)
        {
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _approvalClient?.Close(); } catch { }
            _writer = null;
            _reader = null;
            _approvalClient = null;
        }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _acceptTask?.Wait(2000);
        Logger.Info("ConnectionApprovalService: Stopped");
    }

    /// <summary>
    /// Returns true if the approval client (GUI) is connected
    /// </summary>
    public bool IsApprovalClientConnected
    {
        get
        {
            lock (_clientLock)
            {
                return _approvalClient != null && _approvalClient.Connected;
            }
        }
    }

    /// <summary>
    /// Request approval for an incoming connection. Sends "PENDING|remoteEndPoint" to the GUI
    /// and waits for "APPROVE" or "REJECT" (or timeout). Returns true if approved, false if rejected or timeout.
    /// </summary>
    public async Task<bool> RequestApprovalAsync(string remoteEndPoint, int timeoutMs = DefaultApprovalTimeoutMs, CancellationToken cancellationToken = default)
    {
        StreamWriter? w;
        StreamReader? r;
        lock (_clientLock)
        {
            w = _writer;
            r = _reader;
        }

        if (w == null || r == null)
        {
            Logger.Info($"ConnectionApprovalService: No approval client connected - REJECTING {remoteEndPoint} (open LanDesk app on this PC to approve)");
            return false;
        }

        try
        {
            await w.WriteLineAsync($"PENDING|{remoteEndPoint}");
            await w.FlushAsync();
            _waitingForApprovalResponse = true;
            Logger.Info($"ConnectionApprovalService: Sent PENDING|{remoteEndPoint}, waiting for APPROVE/REJECT (timeout {timeoutMs}ms)");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            var line = await r.ReadLineAsync(cts.Token);
            var response = line?.Trim().ToUpperInvariant();
            if (response == "APPROVE")
            {
                Logger.Info($"ConnectionApprovalService: Connection from {remoteEndPoint} APPROVED by user");
                return true;
            }
            if (string.IsNullOrEmpty(response))
                Logger.Info($"ConnectionApprovalService: Connection from {remoteEndPoint} REJECTED (no response / connection lost / timeout)");
            else
                Logger.Info($"ConnectionApprovalService: Connection from {remoteEndPoint} REJECTED (response was: '{response}')");
            return false;
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"ConnectionApprovalService: Approval TIMEOUT for {remoteEndPoint} ({timeoutMs}ms) - rejecting");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning($"ConnectionApprovalService: Error during approval for {remoteEndPoint}: {ex.Message} - rejecting");
            return false;
        }
        finally
        {
            _waitingForApprovalResponse = false;
        }
    }

    private async Task AcceptLoop()
    {
        while (_isListening && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                lock (_clientLock)
                {
                    // Do not replace the approval client while waiting for user's Approve/Reject - otherwise
                    // the in-flight response is lost and the viewer gets rejected (black screen).
                    if (_waitingForApprovalResponse)
                    {
                        Logger.Info("ConnectionApprovalService: New approval client connected while waiting for response - keeping existing connection until response received");
                        try { client.Close(); } catch { }
                        try { client.Dispose(); } catch { }
                        continue;
                    }
                    try { _writer?.Dispose(); } catch { }
                    try { _reader?.Dispose(); } catch { }
                    try { _approvalClient?.Close(); } catch { }
                    _approvalClient = client;
                    _approvalClient.ReceiveTimeout = 0;
                    _approvalClient.SendTimeout = 5000;
                    var stream = _approvalClient.GetStream();
                    _reader = new StreamReader(stream, Encoding.UTF8);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
                    Logger.Info($"ConnectionApprovalService: Approval client connected from {client.Client.RemoteEndPoint}");
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (!_isListening) { break; }
            catch (Exception ex)
            {
                if (_isListening)
                    Logger.Debug($"ConnectionApprovalService: Accept error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
