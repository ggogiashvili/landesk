using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Client side of screen capture helper - runs in user session and sends frames to service.
/// Reconnects automatically if the pipe is not ready or connection is lost.
/// </summary>
public class ScreenCaptureHelperClient : IDisposable
{
    private NamedPipeClientStream? _pipeClient;
    private readonly string _pipeName;
    private readonly ScreenCaptureService _screenCapture;
    private volatile bool _isRunning;
    private Task? _connectTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _pipeLock = new object();
    private int _framesSentCount;

    public ScreenCaptureHelperClient(string pipeName, ScreenCaptureService screenCapture)
    {
        _pipeName = pipeName;
        _screenCapture = screenCapture;
    }

    public void Start()
    {
        if (_isRunning)
        {
            Logger.Warning("ScreenCaptureHelperClient: Already running");
            return;
        }

        _isRunning = true;
        _framesSentCount = 0;
        Logger.Info($"ScreenCaptureHelperClient: Starting - will connect to pipe '{_pipeName}' (retries until service is ready)");
        _connectTask = Task.Run(ConnectionLoop);
    }

    private async Task ConnectionLoop()
    {
        while (_isRunning && !_cts.Token.IsCancellationRequested)
        {
            NamedPipeClientStream? client = null;
            try
            {
                client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                Logger.Info("ScreenCaptureHelperClient: Connecting to service pipe...");
                using (var timeoutCts = new CancellationTokenSource(5000))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token))
                {
                    await client.ConnectAsync(linked.Token).ConfigureAwait(false);
                }
                Logger.Info("ScreenCaptureHelperClient: Connected to service - starting screen capture and stream");

                lock (_pipeLock)
                {
                    _pipeClient = client;
                    client = null;
                }

                _screenCapture.FrameCaptured += OnFrameCaptured;
                _screenCapture.StartCapture(frameRate: 30, quality: 75);

                // Stay connected until pipe breaks or we're stopped (short interval so we stop capture quickly when viewer disconnects)
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                    lock (_pipeLock)
                    {
                        if (_pipeClient == null || !_pipeClient.IsConnected)
                            break;
                    }
                }

                _screenCapture.FrameCaptured -= OnFrameCaptured;
                _screenCapture.StopCapture();
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException)
            {
                Logger.Warning("ScreenCaptureHelperClient: Connection timeout - will retry in 5s (is the LanDesk service running?)");
            }
            catch (Exception ex)
            {
                Logger.Warning($"ScreenCaptureHelperClient: Connect failed: {ex.Message} - will retry in 5s");
            }
            finally
            {
                client?.Dispose();
                lock (_pipeLock)
                {
                    try { _pipeClient?.Dispose(); } catch { }
                    _pipeClient = null;
                }
            }

            if (!_isRunning) break;
            try { await Task.Delay(5000, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }

        Logger.Info("ScreenCaptureHelperClient: Connection loop ended");
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts.Cancel();
        _screenCapture.FrameCaptured -= OnFrameCaptured;
        _screenCapture.StopCapture();

        lock (_pipeLock)
        {
            try { _pipeClient?.Close(); } catch { }
            try { _pipeClient?.Dispose(); } catch { }
            _pipeClient = null;
        }

        try { _connectTask?.Wait(3000); } catch { }
        Logger.Info("ScreenCaptureHelperClient: Stopped");
    }

    private async void OnFrameCaptured(object? sender, byte[] frameData)
    {
        NamedPipeClientStream? pipe;
        lock (_pipeLock)
        {
            pipe = _pipeClient;
            if (pipe == null || !pipe.IsConnected) return;
        }

        try
        {
            byte[] sizeBytes = BitConverter.GetBytes(frameData.Length);
            await pipe.WriteAsync(sizeBytes, 0, 4).ConfigureAwait(false);
            await pipe.WriteAsync(frameData, 0, frameData.Length).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);

            var n = Interlocked.Increment(ref _framesSentCount);
            if (n <= 3)
                Logger.Info($"ScreenCaptureHelperClient: Sent frame #{n} ({frameData.Length} bytes) to service");
            else
                Logger.Debug($"ScreenCaptureHelperClient: Sent frame {frameData.Length} bytes to service");
        }
        catch (Exception ex)
        {
            Logger.Warning($"ScreenCaptureHelperClient: Pipe send failed: {ex.Message} - stopping capture and will reconnect when a viewer connects");
            lock (_pipeLock)
            {
                try { _pipeClient?.Dispose(); } catch { }
                _pipeClient = null;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
