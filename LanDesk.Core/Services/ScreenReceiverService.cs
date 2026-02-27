using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for receiving and displaying screen frames
/// </summary>
public class ScreenReceiverService : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isReceiving;
    private Task? _receiveTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private int _framesReceivedCount = 0;

    public event EventHandler<Bitmap>? FrameReceived;

    /// <summary>Raised when the receive loop exits (connection closed by remote or error).</summary>
    public event EventHandler? ReceivingStopped;

    public ScreenReceiverService()
    {
    }

    /// <summary>
    /// Starts receiving frames from a TCP client
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
        _framesReceivedCount = 0;

        Utilities.Logger.Info($"ScreenReceiverService: Starting to receive frames from {client.Client.RemoteEndPoint}");
        _receiveTask = Task.Run(ReceiveFrames, _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Stops receiving frames
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

    private async Task ReceiveFrames()
    {
        var buffer = new byte[4]; // For frame size
        Utilities.Logger.Info($"ScreenReceiverService: ReceiveFrames loop started - waiting for frames from {_client?.Client.RemoteEndPoint}");

        while (_isReceiving && _stream != null && _client?.Connected == true)
        {
            try
            {
                Utilities.Logger.Debug($"ScreenReceiverService: Waiting to read frame size (4 bytes) from stream...");
                // Read frame size
                var stream = _stream;
                var bytesRead = stream != null ? await stream.ReadAsync(buffer, 0, 4, _cancellationTokenSource.Token) : 0;
                Utilities.Logger.Debug($"ScreenReceiverService: Read {bytesRead} bytes for frame size");
                
                if (bytesRead != 4)
                {
                    // Connection closed
                    break;
                }

                var frameSize = BitConverter.ToInt32(buffer, 0);
                Utilities.Logger.Debug($"ScreenReceiverService: Reading frame size: {frameSize} bytes");

                if (frameSize <= 0 || frameSize > 10 * 1024 * 1024) // Max 10MB frame
                {
                    Utilities.Logger.Warning($"ScreenReceiverService: Invalid frame size: {frameSize} bytes - stopping receive");
                    break;
                }

                // Read frame data from pool
                var bufferPool = System.Buffers.ArrayPool<byte>.Shared;
                var frameData = bufferPool.Rent(frameSize);
                try
                {
                    var totalRead = 0;
                    while (totalRead < frameSize)
                    {
                        var read = await _stream.ReadAsync(frameData, totalRead, frameSize - totalRead, _cancellationTokenSource.Token);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (totalRead == frameSize)
                    {
                        // Decode frame
                        try
                        {
                            using var ms = new MemoryStream(frameData, 0, frameSize);
                            using var bitmap = new Bitmap(ms);
                            
                            // Create a copy since the original depends on the MemoryStream/buffer
                            var frameCopy = new Bitmap(bitmap);
                            var n = Interlocked.Increment(ref _framesReceivedCount);
                            if (n <= 5)
                                Utilities.Logger.Info($"ScreenReceiverService: Received frame #{n} ({frameSize} bytes -> {bitmap.Width}x{bitmap.Height})");
                            else
                                Utilities.Logger.Debug($"ScreenReceiverService: Successfully decoded frame {frameSize} bytes -> {bitmap.Width}x{bitmap.Height}");
                            FrameReceived?.Invoke(this, frameCopy);
                        }
                        catch (Exception decodeEx)
                        {
                            Utilities.Logger.Warning($"ScreenReceiverService: Failed to decode frame: {decodeEx.Message}");
                            // Continue receiving - might be a bad frame
                        }
                    }
                    else
                    {
                        Utilities.Logger.Warning($"ScreenReceiverService: Incomplete frame read: {totalRead}/{frameSize} bytes");
                    }
                }
                finally
                {
                    bufferPool.Return(frameData);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                Utilities.Logger.Debug("ScreenReceiverService: Receiving cancelled (stopping)");
                break;
            }
            catch (System.IO.IOException ex)
            {
                // Connection closed by remote - this is normal
                Utilities.Logger.Debug($"ScreenReceiverService: Connection closed by remote: {ex.Message}");
                break;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // Socket error - connection lost
                Utilities.Logger.Debug($"ScreenReceiverService: Socket error (connection lost): {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                // Other unexpected errors
                Utilities.Logger.Warning($"ScreenReceiverService: Error receiving frame: {ex.Message}");
                Utilities.Logger.Error($"ScreenReceiverService: Exception details: {ex.GetType().Name} - {ex.Message}", ex);
                break;
            }
        }

        Utilities.Logger.Info($"ScreenReceiverService: ReceiveFrames loop exited - _isReceiving={_isReceiving}, stream={_stream != null}, client connected={_client?.Connected}, framesReceived={_framesReceivedCount}");
        _isReceiving = false;
        try { ReceivingStopped?.Invoke(this, EventArgs.Empty); } catch { }
    }

    public void Dispose()
    {
        StopReceiving();
        _cancellationTokenSource?.Dispose();
    }
}
