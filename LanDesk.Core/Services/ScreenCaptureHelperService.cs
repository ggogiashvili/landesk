using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for communicating with a helper process that runs in the user session
/// The helper process performs screen capture and sends frames via named pipe
/// </summary>
public class ScreenCaptureHelperService : IDisposable
{
    private NamedPipeServerStream? _pipeServer;
    private bool _isRunning = false;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _receiveTask;
    private int _framesReceivedFromHelper = 0;

    public event EventHandler<byte[]>? FrameReceived;

    public ScreenCaptureHelperService(string pipeName = "LanDeskScreenCapture")
    {
        _pipeName = pipeName;
    }

    public void Start()
    {
        if (_isRunning)
        {
            Logger.Warning("ScreenCaptureHelperService: Already running");
            return;
        }

        _isRunning = true;
        // Pipe must allow the logged-in user (GUI) to connect; service runs as SYSTEM.
        var pipeSecurity = new PipeSecurity();
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(authenticatedUsers,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        _pipeServer = NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);

        Logger.Info($"ScreenCaptureHelperService: Starting named pipe server '{_pipeName}' (ACL allows user session to connect)");
        _receiveTask = Task.Run(ReceiveFrames);
    }

    /// <summary>
    /// Disconnects the current helper client so it stops capturing (e.g. when viewer stops sharing).
    /// The helper will detect the broken pipe and stop; it can reconnect when a new viewer connects.
    /// </summary>
    public void DisconnectCurrentClient()
    {
        try
        {
            if (_pipeServer != null && _pipeServer.IsConnected)
            {
                _pipeServer.Disconnect();
                Logger.Info("ScreenCaptureHelperService: Disconnected helper client (stop sharing) - capture will stop on host");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"ScreenCaptureHelperService: DisconnectCurrentClient: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cancellationTokenSource.Cancel();

        try
        {
            _pipeServer?.Disconnect();
        }
        catch { }

        _receiveTask?.Wait(1000);
        _pipeServer?.Dispose();
        _pipeServer = null;

        Logger.Info("ScreenCaptureHelperService: Stopped");
    }

    private async Task ReceiveFrames()
    {
        var buffer = new byte[4]; // For frame size
        try
        {
            while (_isRunning && _pipeServer != null)
            {
                Logger.Info("ScreenCaptureHelperService: Waiting for helper process to connect...");
                await _pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);
                Logger.Info("ScreenCaptureHelperService: Helper process connected");

                while (_isRunning && _pipeServer.IsConnected)
            {
                try
                {
                    // Read frame size (4 bytes)
                    int bytesRead = await _pipeServer.ReadAsync(buffer, 0, 4, _cancellationTokenSource.Token);
                    if (bytesRead != 4)
                    {
                        Logger.Debug("ScreenCaptureHelperService: Connection closed by helper");
                        break;
                    }

                    int frameSize = BitConverter.ToInt32(buffer, 0);
                    if (frameSize <= 0 || frameSize > 10 * 1024 * 1024) // Max 10MB
                    {
                        Logger.Warning($"ScreenCaptureHelperService: Invalid frame size: {frameSize}");
                        break;
                    }

                    // Read frame data
                    byte[] frameData = new byte[frameSize];
                    int totalRead = 0;
                    while (totalRead < frameSize)
                    {
                        int read = await _pipeServer.ReadAsync(frameData, totalRead, frameSize - totalRead, _cancellationTokenSource.Token);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (totalRead == frameSize)
                    {
                        var n = Interlocked.Increment(ref _framesReceivedFromHelper);
                        if (n <= 3)
                            Logger.Info($"ScreenCaptureHelperService: Received frame #{n} ({frameSize} bytes) from helper - pipe OK");
                        else
                            Logger.Debug($"ScreenCaptureHelperService: Received frame {frameSize} bytes from helper");
                        // Invoke on thread pool so pipe read loop isn't blocked by TCP write (avoids "Pipe is broken")
                        byte[] copy = new byte[frameSize];
                        Buffer.BlockCopy(frameData, 0, copy, 0, frameSize);
                        _ = Task.Run(() => { try { FrameReceived?.Invoke(this, copy); } catch { } });
                    }
                    else
                    {
                        Logger.Warning($"ScreenCaptureHelperService: Incomplete frame read: {totalRead}/{frameSize}");
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug("ScreenCaptureHelperService: Receiving cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"ScreenCaptureHelperService: Error receiving frame: {ex.Message}", ex);
                    break;
                }
                }

                try { _pipeServer?.Disconnect(); } catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"ScreenCaptureHelperService: Error in receive loop: {ex.Message}", ex);
        }
        finally
        {
            Logger.Info("ScreenCaptureHelperService: Receive loop exited");
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource.Dispose();
    }
}
