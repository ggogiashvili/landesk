using System.Net.Sockets;
using LanDesk.Core.Protocol;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for receiving and processing input commands
/// </summary>
public class InputReceiverService : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isReceiving;
    private Task? _receiveTask;
    private readonly InputInjectionService _inputInjection;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<InputCommand>? CommandReceived;

    public InputReceiverService()
    {
        _inputInjection = new InputInjectionService();
    }

    /// <summary>
    /// Starts receiving input commands from a TCP client
    /// </summary>
    public void StartReceiving(TcpClient client)
    {
        if (_isReceiving)
            StopReceiving();

        // Create a new CancellationTokenSource for this connection
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        _client = client;
        _stream = client.GetStream();
        _isReceiving = true;

        _receiveTask = Task.Run(ReceiveCommands, _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Stops receiving input commands
    /// </summary>
    public void StopReceiving()
    {
        _isReceiving = false;
        
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch { }

        try
        {
            _stream?.Close();
        }
        catch { }

        try
        {
            _client?.Close();
        }
        catch { }

        _stream = null;
        _client = null;

        try
        {
            _receiveTask?.Wait(1000);
        }
        catch { }
        
        _receiveTask = null;
    }

    /// <summary>
    /// Checks if currently receiving
    /// </summary>
    public bool IsReceiving => _isReceiving;

    private async Task ReceiveCommands()
    {
        var sizeBuffer = new byte[4];

        while (_isReceiving && _stream != null && _client?.Connected == true)
        {
            try
            {
                // Read command size
                var bytesRead = await _stream.ReadAsync(sizeBuffer, 0, 4, _cancellationTokenSource.Token);
                
                if (bytesRead != 4)
                {
                    // Connection closed
                    break;
                }

                var commandSize = BitConverter.ToInt32(sizeBuffer, 0);

                if (commandSize <= 0 || commandSize > 1024) // Max 1KB command
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid command size: {commandSize}");
                    break;
                }

                // Read command data
                var commandData = new byte[commandSize];
                var totalRead = 0;

                while (totalRead < commandSize)
                {
                    var read = await _stream.ReadAsync(commandData, totalRead, commandSize - totalRead, _cancellationTokenSource.Token);
                    if (read == 0)
                    {
                        // Connection closed
                        break;
                    }
                    totalRead += read;
                }

                if (totalRead == commandSize)
                {
                    // Parse and inject command
                    var command = InputProtocol.ParseCommand(commandData);
                    if (command != null)
                    {
                        CommandReceived?.Invoke(this, command);
                        _inputInjection.InjectInput(command);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                Utilities.Logger.Debug("InputReceiverService: Receiving cancelled (stopping)");
                break;
            }
            catch (System.IO.IOException ex)
            {
                // Connection closed by remote - this is normal
                Utilities.Logger.Debug($"InputReceiverService: Connection closed by remote: {ex.Message}");
                break;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // Socket error - connection lost
                Utilities.Logger.Debug($"InputReceiverService: Socket error (connection lost): {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                // Other unexpected errors
                Utilities.Logger.Warning($"InputReceiverService: Error receiving command: {ex.Message}");
                break;
            }
        }

        _isReceiving = false;
    }

    public void Dispose()
    {
        StopReceiving();
        _cancellationTokenSource?.Dispose();
    }
}
