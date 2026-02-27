using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for capturing screen frames
/// </summary>
public class ScreenCaptureService : IDisposable
{
    private bool _isCapturing;
    private readonly Timer? _captureTimer;
    private int _normalFrameRate = 30; // Store normal frame rate
    private int _quality = 70; // JPEG quality (0-100)
    
    private Bitmap? _cachedBitmap;
    private Graphics? _cachedGraphics;
    private int _cachedWidth;
    private int _cachedHeight;
    private readonly object _captureLock = new object();
    private bool _isUacActive = false;
    private int _consecutiveFailures = 0;
    private const int MAX_CONSECUTIVE_FAILURES = 10;
    private bool _capturePermanentlyFailed = false;
    /// <summary>Last successful frame - sent when capture fails during UAC so viewer doesn't freeze</summary>
    private byte[]? _lastSuccessfulFrame;
    private readonly object _lastFrameLock = new object();

    public event EventHandler<byte[]>? FrameCaptured;

    public ScreenCaptureService()
    {
        _captureTimer = new Timer(CaptureFrame, null, Timeout.Infinite, Timeout.Infinite);
        DesktopSecurity.UacStateChanged += OnUacStateChanged;
    }

    private void OnUacStateChanged(object? sender, bool isUacActive)
    {
        _isUacActive = isUacActive;
        
        if (_isCapturing)
        {
            // Update interval based on UAC state
            int effectiveFps = _isUacActive ? 2 : _normalFrameRate;
            int interval = Math.Max(16, 1000 / effectiveFps);
            
            Logger.Info($"ScreenCaptureService: UAC State changed to {isUacActive}. Adjusting FPS to {effectiveFps}");
            _captureTimer?.Change(0, interval);
        }
    }

    public void StartCapture(int frameRate = 15, int quality = 75)
    {
        if (_isCapturing)
        {
            Logger.Warning("ScreenCaptureService: StartCapture called but already capturing - ignoring");
            return;
        }

        // Reset failure tracking when starting capture
        _consecutiveFailures = 0;
        _capturePermanentlyFailed = false;

        _normalFrameRate = Math.Max(1, Math.Min(60, frameRate));
        _quality = Math.Max(10, Math.Min(100, quality));
        _isCapturing = true;
        
        int effectiveFps = _isUacActive ? 2 : _normalFrameRate;
        int interval = Math.Max(16, 1000 / effectiveFps);
        
        Logger.Info($"ScreenCaptureService: StartCapture at {effectiveFps} FPS (UAC active: {_isUacActive}), interval: {interval}ms");
        Logger.Info($"ScreenCaptureService: FrameCaptured event has {(FrameCaptured != null ? FrameCaptured.GetInvocationList().Length : 0)} subscriber(s)");
        
        _captureTimer?.Change(0, interval);
        Logger.Info($"ScreenCaptureService: Capture timer started - first frame should arrive in ~{interval}ms");
    }

    public void StopCapture()
    {
        _isCapturing = false;
        _captureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void CaptureFrame(object? state)
    {
        if (!_isCapturing)
        {
            Logger.Debug("ScreenCaptureService: CaptureFrame called but not capturing");
            return;
        }

        // Stop trying if capture has permanently failed
        if (_capturePermanentlyFailed)
        {
            return;
        }

        try
        {
            var frame = CaptureScreen();
                if (frame != null && frame.Length > 0)
            {
                // Success - reset failure counter
                if (_consecutiveFailures > 0)
                {
                    Logger.Info($"ScreenCaptureService: Capture succeeded after {_consecutiveFailures} failures - resetting counter");
                    _consecutiveFailures = 0;
                }
                UpdateLastFrame(frame);
                Logger.Debug($"ScreenCaptureService: Captured frame {frame.Length} bytes");
                if (FrameCaptured != null)
                {
                    Logger.Debug($"ScreenCaptureService: Invoking FrameCaptured event with {FrameCaptured.GetInvocationList().Length} subscriber(s)");
                    FrameCaptured.Invoke(this, frame);
                }
                else
                {
                    Logger.Warning("ScreenCaptureService: FrameCaptured event has no subscribers - frame will be lost!");
                }
            }
            else
            {
                // When UAC is active, send last good frame so remote viewer doesn't freeze; don't count toward permanent failure
                if (_isUacActive)
                {
                    byte[]? lastFrame = GetLastFrame();
                    if (lastFrame != null && lastFrame.Length > 0 && FrameCaptured != null)
                    {
                        FrameCaptured.Invoke(this, lastFrame);
                    }
                    return; // Continue next tick without marking permanent failure
                }
                _consecutiveFailures++;
                // Only log warnings occasionally to avoid log spam
                if (_consecutiveFailures == 1 || _consecutiveFailures % 5 == 0)
                {
                    Logger.Warning($"ScreenCaptureService: CaptureScreen returned null or empty frame (failure #{_consecutiveFailures})");
                }
                // After too many failures, stop trying and log clear error
                if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    _capturePermanentlyFailed = true;
                    bool isService = DesktopSecurity.IsRunningAsService();
                    
                    if (isService)
                    {
                        Logger.Error("=== ScreenCaptureService: PERMANENT CAPTURE FAILURE ===");
                        Logger.Error("Service running in Session 0 cannot capture user desktop using GDI methods.");
                        Logger.Error("Windows services in Session 0 cannot access the user's desktop screen.");
                        Logger.Error("SOLUTION: Implement Windows Desktop Duplication API (DXGI) for service-based screen capture.");
                        Logger.Error("For now, screen sharing will not work when running as a service.");
                        Logger.Error("Consider running the GUI app instead, or implement Desktop Duplication API support.");
                    }
                    else
                    {
                        Logger.Error($"ScreenCaptureService: Capture failed {_consecutiveFailures} times consecutively - stopping capture attempts");
                    }
                    
                    StopCapture();
                }
            }
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            Logger.Error($"ScreenCaptureService: Error in CaptureFrame loop: {ex.Message}", ex);
            
            if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
            {
                _capturePermanentlyFailed = true;
                StopCapture();
            }
        }
    }

    private void UpdateLastFrame(byte[] frame)
    {
        if (frame == null || frame.Length == 0) return;
        lock (_lastFrameLock)
        {
            _lastSuccessfulFrame = frame;
        }
    }

    private byte[]? GetLastFrame()
    {
        lock (_lastFrameLock)
        {
            return _lastSuccessfulFrame;
        }
    }

    private byte[]? CaptureScreen()
    {
        // Shorter timeout when UAC active so we don't block and can send last frame
        int lockTimeoutMs = _isUacActive ? 100 : 500;
        if (!Monitor.TryEnter(_captureLock, lockTimeoutMs)) return null;

        try
        {
            // Ensure we are on the correct desktop (UAC support)
            DesktopSecurity.EnsureCorrectDesktop(out bool desktopSwitched);
            
            int screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
            int screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

            if (screenWidth <= 0 || screenHeight <= 0) return null;

            // Recreate buffer if needed
            if (_cachedBitmap == null || _cachedWidth != screenWidth || _cachedHeight != screenHeight || desktopSwitched)
            {
                _cachedGraphics?.Dispose();
                _cachedBitmap?.Dispose();
                
                _cachedBitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
                _cachedGraphics = Graphics.FromImage(_cachedBitmap);
                _cachedWidth = screenWidth;
                _cachedHeight = screenHeight;
                Logger.Info($"ScreenCaptureService: Reinitialized buffer {screenWidth}x{screenHeight}");
            }

            bool captured = false;

            // Method 1: Graphics.CopyFromScreen (Optimal for GUI apps)
            try
            {
                _cachedGraphics!.CopyFromScreen(0, 0, 0, 0, _cachedBitmap.Size, CopyPixelOperation.SourceCopy);
                captured = true;
            }
            catch (Exception ex)
            {
                // Method 2: GDI fallback (Required for some Session 0 scenarios or Secure Desktop)
                Logger.Debug($"ScreenCaptureService: CopyFromScreen failed ({ex.Message}), trying GDI BitBlt fallback...");
                captured = CaptureViaGdi(screenWidth, screenHeight);
            }

            if (!captured) return null;

            // Compress to JPEG
            using (var ms = new MemoryStream())
            {
                var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (encoder != null)
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
                    _cachedBitmap.Save(ms, encoder, encoderParams);
                    encoderParams.Dispose();
                }
                else
                {
                    _cachedBitmap.Save(ms, ImageFormat.Jpeg);
                }
                return ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ScreenCaptureService: CaptureScreen exception", ex);
            return null;
        }
        finally
        {
            Monitor.Exit(_captureLock);
        }
    }

    private bool CaptureViaGdi(int width, int height)
    {
        IntPtr screenDC = IntPtr.Zero;
        IntPtr memoryDC = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        bool usingCreateDC = false;
        
        try
        {
            // Check if we're running as a service (Session 0)
            bool isService = DesktopSecurity.IsRunningAsService();
            uint activeSession = DesktopSecurity.GetActiveConsoleSessionId();
            
            if (isService)
            {
                Logger.Debug($"ScreenCaptureService: Running as service (Session 0), active console session: {activeSession}");
            }
            
            // Try CreateDC first (works from service/Session 0), fallback to GetDC
            screenDC = CreateDC("DISPLAY", null, null, IntPtr.Zero);
            if (screenDC != IntPtr.Zero)
            {
                usingCreateDC = true;
                Logger.Debug("ScreenCaptureService: Using CreateDC for screen capture (service mode)");
            }
            else
            {
                int createDCError = Marshal.GetLastWin32Error();
                Logger.Debug($"ScreenCaptureService: CreateDC failed with error {createDCError}, trying GetDC");
                // Fallback to GetDC (works for GUI apps)
                screenDC = GetDC(IntPtr.Zero);
                usingCreateDC = false;
            }
            
            if (screenDC == IntPtr.Zero)
            {
                int finalError = Marshal.GetLastWin32Error();
                Logger.Error($"ScreenCaptureService: Failed to get desktop DC (CreateDC and GetDC both failed, last error: {finalError})");
                if (isService)
                {
                    Logger.Error("ScreenCaptureService: Service in Session 0 cannot capture screen. Desktop Duplication API or helper process required.");
                }
                return false;
            }

            // Create memory DC
            memoryDC = CreateCompatibleDC(screenDC);
            if (memoryDC == IntPtr.Zero)
            {
                Logger.Error("ScreenCaptureService: Failed to create memory DC");
                return false;
            }

            // Create compatible bitmap
            bitmapHandle = CreateCompatibleBitmap(screenDC, width, height);
            if (bitmapHandle == IntPtr.Zero)
            {
                Logger.Error("ScreenCaptureService: Failed to create compatible bitmap");
                return false;
            }

            // Select bitmap into memory DC
            oldBitmap = SelectObject(memoryDC, bitmapHandle);

            // Copy screen to memory DC using BitBlt
            const int SRCCOPY = 0x00CC0020;
            bool success = BitBlt(memoryDC, 0, 0, width, height, screenDC, 0, 0, SRCCOPY);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"ScreenCaptureService: BitBlt failed with error {error} (screenDC: {screenDC}, memoryDC: {memoryDC}, size: {width}x{height})");
                return false;
            }
            
            Logger.Debug($"ScreenCaptureService: Successfully captured screen using BitBlt ({width}x{height})");

            // Copy from memory DC to our bitmap
            using (Graphics g = Graphics.FromImage(_cachedBitmap!))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    BitBlt(hdc, 0, 0, width, height, memoryDC, 0, 0, SRCCOPY);
                    return true;
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ScreenCaptureService: Error copying screen: {ex.Message}", ex);
            return false;
        }
        finally
        {
            // Cleanup
            if (oldBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero)
            {
                SelectObject(memoryDC, oldBitmap);
            }
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }
            if (memoryDC != IntPtr.Zero)
            {
                DeleteDC(memoryDC);
            }
            if (screenDC != IntPtr.Zero)
            {
                if (usingCreateDC)
                {
                    DeleteDC(screenDC);
                }
                else
                {
                    ReleaseDC(IntPtr.Zero, screenDC);
                }
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    public void Dispose()
    {
        StopCapture();
        DesktopSecurity.UacStateChanged -= OnUacStateChanged;
        _captureTimer?.Dispose();
        
        lock (_captureLock)
        {
            _cachedGraphics?.Dispose();
            _cachedBitmap?.Dispose();
        }
    }
}
