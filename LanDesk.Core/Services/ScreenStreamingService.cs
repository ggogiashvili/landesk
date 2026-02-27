using System.Net.Sockets;
using System.Threading;
using LanDesk.Core.Protocol;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for streaming screen frames over TCP.
/// Can use either direct capture (ScreenCaptureService) or frames from helper in user session (ScreenCaptureHelperService).
/// </summary>
public class ScreenStreamingService : IDisposable
{
    private readonly ScreenCaptureService? _screenCapture;
    private readonly ScreenCaptureHelperService? _helperService;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isStreaming;

    /// <summary>Fired when streaming stops (client disconnected or error). Allows service to clear approved-IP.</summary>
    public event EventHandler<string?>? StreamingStopped;

    /// <summary>Use when running as GUI or when capture runs in same process (direct capture).</summary>
    public ScreenStreamingService(ScreenCaptureService screenCapture)
    {
        _screenCapture = screenCapture;
        _helperService = null;
        _screenCapture.FrameCaptured += OnFrameCaptured;
    }

    /// <summary>Use when running as Windows service: frames come from GUI helper client via named pipe.</summary>
    public ScreenStreamingService(ScreenCaptureHelperService helperService)
    {
        _screenCapture = null;
        _helperService = helperService;
        _helperService.FrameReceived += OnFrameCaptured;
    }

    /// <summary>
    /// Starts streaming to a TCP client
    /// </summary>
    public void StartStreaming(TcpClient client, int frameRate = 10, int quality = 70)
    {
        if (_isStreaming)
        {
            Utilities.Logger.Warning("ScreenStreamingService: Already streaming, stopping previous stream first");
            StopStreaming();
        }

        _client = client;
        _stream = client.GetStream();
        _isStreaming = true;
        _framesSentCount = 0;

        Utilities.Logger.Info($"ScreenStreamingService: Starting to stream to {client.Client.RemoteEndPoint} at {frameRate} FPS, quality {quality}%");
        Utilities.Logger.Info($"ScreenStreamingService: Client connected: {client.Connected}, Stream can write: {_stream?.CanWrite}");
        
        if (_screenCapture != null)
        {
            try
            {
                _screenCapture.StartCapture(frameRate, quality);
                Utilities.Logger.Info($"ScreenStreamingService: Screen capture started successfully - waiting for frames...");
            }
            catch (Exception ex)
            {
                Utilities.Logger.Error($"ScreenStreamingService: Failed to start screen capture: {ex.Message}", ex);
                _isStreaming = false;
                throw;
            }
        }
        else
        {
            Utilities.Logger.Info("ScreenStreamingService: Using helper (pipe) for frames - ensure LanDesk app is open on this PC");
        }
    }

    /// <summary>
    /// Stops streaming
    /// </summary>
    public void StopStreaming()
    {
        if (!_isStreaming)
            return;

        string? remoteEndPoint = null;
        if (_client?.Client?.RemoteEndPoint != null)
            remoteEndPoint = _client.Client.RemoteEndPoint.ToString();
            
        _isStreaming = false;
        _screenCapture?.StopCapture();

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
        
        Utilities.Logger.Debug("ScreenStreamingService: Streaming stopped");
        try { StreamingStopped?.Invoke(this, remoteEndPoint); } catch { }
    }

    /// <summary>
    /// Checks if currently streaming
    /// </summary>
    public bool IsStreaming => _isStreaming;

    private volatile bool _isSendingFrame = false;
    private int _framesSentCount = 0;
    
    private void OnFrameCaptured(object? sender, byte[] frameData)
    {
        if (!_isStreaming)
        {
            Utilities.Logger.Debug("ScreenStreamingService: OnFrameCaptured called but not streaming");
            return;
        }
        
        if (_stream == null || !_stream.CanWrite)
        {
            Utilities.Logger.Warning("ScreenStreamingService: Stream is null or cannot write");
            return;
        }

        // Check if client is still connected
        if (_client == null || !_client.Connected)
        {
            Utilities.Logger.Debug("ScreenStreamingService: Client disconnected, stopping streaming");
            StopStreaming();
            return;
        }

        // Skip frame if still sending previous frame (frame dropping for network efficiency)
        if (_isSendingFrame)
        {
            Utilities.Logger.Debug("ScreenStreamingService: Skipping frame - still sending previous frame");
            return;
        }

        try
        {
            _isSendingFrame = true;
            
            // Double-check connection before sending
            if (_client == null || !_client.Connected || _stream == null || !_stream.CanWrite)
            {
                Utilities.Logger.Debug("ScreenStreamingService: Connection lost during frame send");
                StopStreaming();
                return;
            }
            
            // Send frame size and data
            var sizeBytes = BitConverter.GetBytes(frameData.Length);
            _stream.Write(sizeBytes, 0, sizeBytes.Length);
            _stream.Write(frameData, 0, frameData.Length);
            _stream.Flush();
            var n = Interlocked.Increment(ref _framesSentCount);
            if (n <= 5)
                Utilities.Logger.Info($"ScreenStreamingService: Sent frame #{n} ({frameData.Length} bytes) to {_client.Client.RemoteEndPoint}");
            else
                Utilities.Logger.Debug($"ScreenStreamingService: Sent frame #{n} ({frameData.Length} bytes) to {_client.Client.RemoteEndPoint}");
        }
        catch (System.IO.IOException ex)
        {
            // Connection closed by client - this is normal, just log as debug
            Utilities.Logger.Debug($"ScreenStreamingService: Connection closed by client: {ex.Message}");
            StopStreaming();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Socket error - connection lost
            Utilities.Logger.Debug($"ScreenStreamingService: Socket error (connection lost): {ex.Message}");
            StopStreaming();
        }
        catch (Exception ex)
        {
            // Other errors - log as warning
            Utilities.Logger.Warning($"ScreenStreamingService: Error sending frame: {ex.Message}");
            StopStreaming();
        }
        finally
        {
            _isSendingFrame = false;
        }
    }

    public void Dispose()
    {
        StopStreaming();
        if (_screenCapture != null)
            _screenCapture.FrameCaptured -= OnFrameCaptured;
        if (_helperService != null)
            _helperService.FrameReceived -= OnFrameCaptured;
    }
}
