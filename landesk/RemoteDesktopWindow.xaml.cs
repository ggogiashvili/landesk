using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanDesk.Core.Models;
using LanDesk.Core.Services;

namespace LanDesk;

public partial class RemoteDesktopWindow : Window
{
    private readonly DiscoveredDevice _device;
    private readonly ScreenReceiverService _screenReceiver;
    private readonly InputSenderService _inputSender;
    private System.Net.Sockets.TcpClient? _screenConnection;
    private System.Net.Sockets.TcpClient? _inputConnection;
    private bool _isReceiving;
    private bool _firstFrameReceived = false; // Track if we've received the first frame
    private System.Drawing.Size _remoteScreenSize = new System.Drawing.Size(1920, 1080); // Default, will be updated
    private System.Windows.Size _imageDisplaySize = new System.Windows.Size(1920, 1080);
    
    private static readonly int SCREEN_PORT = LanDesk.Core.Configuration.NetworkConfiguration.DefaultControlPort;
    private static readonly int INPUT_PORT = LanDesk.Core.Configuration.NetworkConfiguration.DefaultInputPort;

    public RemoteDesktopWindow(DiscoveredDevice device)
    {
        InitializeComponent();
        _device = device;
        _screenReceiver = new ScreenReceiverService();
        _inputSender = new InputSenderService();
        
        DeviceNameText.Text = $"Remote Desktop - {device.DeviceName}";
        StatusText.Text = "Connecting...";
        
        _screenReceiver.FrameReceived += OnFrameReceived;
        _screenReceiver.ReceivingStopped += OnScreenReceivingStopped;
        
        Loaded += RemoteDesktopWindow_Loaded;
        Closing += RemoteDesktopWindow_Closing;
        
        // Capture keyboard events - use Preview events to capture before other controls
        PreviewKeyDown += RemoteDesktopWindow_PreviewKeyDown;
        PreviewKeyUp += RemoteDesktopWindow_PreviewKeyUp;
        KeyDown += RemoteDesktopWindow_KeyDown;
        KeyUp += RemoteDesktopWindow_KeyUp;
        Focusable = true;
        
        // Make window focusable for keyboard input
        Loaded += (s, e) => 
        { 
            Focus(); 
            RemoteScreenImage.Focusable = false; // Prevent image from stealing focus
        };
    }

    private async void RemoteDesktopWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LanDesk.Core.Utilities.Logger.Info($"=== RemoteDesktopWindow Connection Started ===");
            LanDesk.Core.Utilities.Logger.Info($"Target Device: {_device.DeviceName} ({_device.DeviceId})");
            LanDesk.Core.Utilities.Logger.Info($"Target IP: {_device.IpAddress}");
            StatusText.Text = "Connecting to remote device...";
            
            // Connect to remote device for screen streaming
            // Support both IP address and hostname/domain name
            LanDesk.Core.Utilities.Logger.Info($"Connecting to screen streaming port ({SCREEN_PORT})...");
            var screenConnectionManager = new ConnectionManager(SCREEN_PORT);
            
            // Use hostname string if IP is Any (direct connection), otherwise use IP
            if (_device.IpAddress == System.Net.IPAddress.Any || _device.IpAddress == null)
            {
                // Direct connection - use device name as hostname
                _screenConnection = await screenConnectionManager.ConnectToDeviceAsync(_device.DeviceName, SCREEN_PORT, _device.DeviceName);
            }
            else
            {
                // Use IP address
                _screenConnection = await screenConnectionManager.ConnectToDeviceAsync(_device.IpAddress, SCREEN_PORT, _device.DeviceName);
            }
            LanDesk.Core.Utilities.Logger.Info($"Screen connection result: {(_screenConnection != null && _screenConnection.Connected ? "SUCCESS" : "FAILED")}");
            
            // Connect to remote device for input
            LanDesk.Core.Utilities.Logger.Info($"Connecting to input control port ({INPUT_PORT})...");
            var inputConnectionManager = new ConnectionManager(INPUT_PORT);
            
            // Use hostname string if IP is Any (direct connection), otherwise use IP
            if (_device.IpAddress == System.Net.IPAddress.Any || _device.IpAddress == null)
            {
                // Direct connection - use device name as hostname
                _inputConnection = await inputConnectionManager.ConnectToDeviceAsync(_device.DeviceName, INPUT_PORT, _device.DeviceName);
            }
            else
            {
                // Use IP address
                _inputConnection = await inputConnectionManager.ConnectToDeviceAsync(_device.IpAddress, INPUT_PORT, _device.DeviceName);
            }
            LanDesk.Core.Utilities.Logger.Info($"Input connection result: {(_inputConnection != null && _inputConnection.Connected ? "SUCCESS" : "FAILED")}");
            
            if (_screenConnection != null && _screenConnection.Connected)
            {
                LanDesk.Core.Utilities.Logger.Info("Screen connection established - starting screen receiver...");
                StatusText.Text = "Connected - Receiving screen...";
                _frameDisplayCount = 0;
                _screenReceiver.StartReceiving(_screenConnection);
                _isReceiving = true;
                StartNoVideoReminderTimer();
                LanDesk.Core.Utilities.Logger.Info("Screen receiver started successfully");
            }
            else
            {
                LanDesk.Core.Utilities.Logger.Error("Screen connection failed - cannot receive screen");
                StatusText.Text = "Screen connection failed";
                MessageBox.Show("Failed to connect to remote device for screen sharing.", "Connection Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            if (_inputConnection != null && _inputConnection.Connected)
            {
                LanDesk.Core.Utilities.Logger.Info("Input connection established - starting input sender...");
                _inputSender.Connect(_inputConnection);
                StatusText.Text = "Connected - Ready for remote control";
                LanDesk.Core.Utilities.Logger.Info("Input sender started successfully - remote control ready");
                
                // Send an initial mouse move to "wake up" the remote side and ensure input is active
                await Task.Delay(100); // Small delay to ensure connection is fully established
                if (_remoteScreenSize.Width > 0 && _remoteScreenSize.Height > 0)
                {
                    // Send a small mouse move to activate input on remote side
                    _inputSender.SendMouseMove(_remoteScreenSize.Width / 2, _remoteScreenSize.Height / 2);
                    LanDesk.Core.Utilities.Logger.Info("Sent initial mouse move to activate remote input");
                }
            }
            else
            {
                LanDesk.Core.Utilities.Logger.Warning("Input connection failed - screen only mode");
                StatusText.Text = "Connected - Screen only (input failed)";
                MessageBox.Show("Screen sharing connected, but remote control failed to connect.\n\n" +
                              "You can view the screen but cannot control it.",
                              "Partial Connection", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            // Activate and focus the window to capture keyboard input
            Activate();
            Focus();
            BringIntoView();
            
            // Ensure window is on top and active
            Topmost = true;
            await Task.Delay(50);
            Topmost = false;
            
            LanDesk.Core.Utilities.Logger.Info("Window activated and focused - ready for input");
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Error("Exception during RemoteDesktopWindow connection", ex);
            StatusText.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Error connecting: {ex.Message}", "Error", 
                          MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private volatile bool _isUpdatingFrame = false;
    private System.Windows.Threading.DispatcherTimer? _noVideoReminderTimer;

    private void OnScreenReceivingStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_firstFrameReceived && _isReceiving)
            {
                _isReceiving = false;
                _noVideoReminderTimer?.Stop();
                _noVideoReminderTimer = null;
                StatusText.Text = "Connection closed before video started. If you clicked Approve on the remote PC, keep LanDesk open there and try again.";
                WaitingVideoText.Text = "Connection closed before video started. If you approved, keep LanDesk open on the remote PC and try connecting again.";
                WaitingVideoOverlay.Visibility = Visibility.Visible;
                LanDesk.Core.Utilities.Logger.Info("RemoteDesktopWindow: Screen stream ended without receiving any frame - host may have rejected, timed out, or had an error after approval.");
            }
        });
    }

    private void StartNoVideoReminderTimer()
    {
        _noVideoReminderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _noVideoReminderTimer.Tick += (s, e) =>
        {
            _noVideoReminderTimer?.Stop();
            if (!_firstFrameReceived && _isReceiving)
            {
                LanDesk.Core.Utilities.Logger.Warning("No video received after 15s - remind user to open LanDesk on remote PC");
                StatusText.Text = "No video yet. Ask the remote user to open the LanDesk app (needed for screen sharing).";
                Dispatcher.Invoke(() =>
                {
                    WaitingVideoText.Text = "No video yet. Ask the remote user to open the LanDesk app on the host PC.";
                });
            }
        };
        _noVideoReminderTimer.Start();
    }

    private int _frameDisplayCount = 0;

    private void OnFrameReceived(object? sender, Bitmap bitmap)
    {
        var n = System.Threading.Interlocked.Increment(ref _frameDisplayCount);
        if (n <= 5)
            LanDesk.Core.Utilities.Logger.Info($"RemoteDesktopWindow: OnFrameReceived frame #{n} ({bitmap?.Width}x{bitmap?.Height})");
        else
            LanDesk.Core.Utilities.Logger.Debug($"OnFrameReceived: Frame received {bitmap?.Width}x{bitmap?.Height}, _isUpdatingFrame={_isUpdatingFrame}");
        
        // Skip frame if UI is still processing previous frame (frame dropping for smooth playback)
        if (_isUpdatingFrame)
        {
            LanDesk.Core.Utilities.Logger.Debug("OnFrameReceived: Skipping frame - UI still updating previous frame");
            bitmap?.Dispose();
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (!_isReceiving)
            {
                bitmap?.Dispose();
                return;
            }

            _isUpdatingFrame = true;
            try
            {
                // Update remote screen size
                _remoteScreenSize = new System.Drawing.Size(bitmap.Width, bitmap.Height);
                
                // Mark that we've received the first frame
                if (!_firstFrameReceived)
                {
                    _firstFrameReceived = true;
                    _noVideoReminderTimer?.Stop();
                    _noVideoReminderTimer = null;
                    WaitingVideoOverlay.Visibility = Visibility.Collapsed;
                    LanDesk.Core.Utilities.Logger.Info($"First frame received: {bitmap.Width}x{bitmap.Height} - Mouse input now enabled");

                    // After first frame is received, send initial mouse move if input is connected
                    // This "wakes up" the remote side and ensures input is active
                    if (_inputConnection != null && _inputConnection.Connected && _inputSender != null && _remoteScreenSize.Width > 0)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Send a small mouse move to center to activate input on remote side
                                _inputSender.SendMouseMove(_remoteScreenSize.Width / 2, _remoteScreenSize.Height / 2);
                                LanDesk.Core.Utilities.Logger.Debug("Sent initial mouse move after first frame to activate remote input");
                            }
                            catch (Exception ex)
                            {
                                LanDesk.Core.Utilities.Logger.Debug($"Failed to send initial mouse move: {ex.Message}");
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                
                // Convert Bitmap to BitmapImage for WPF (more efficient method)
                var bitmapImage = BitmapToBitmapImage(bitmap);
                
                // Dispose old source to free memory
                if (RemoteScreenImage.Source is BitmapImage oldImage)
                {
                    oldImage.StreamSource?.Dispose();
                }
                
                RemoteScreenImage.Source = bitmapImage;
                
                // Update display size for coordinate mapping
                if (RemoteScreenImage.Source != null)
                {
                    _imageDisplaySize = new System.Windows.Size(
                        RemoteScreenImage.Source.Width,
                        RemoteScreenImage.Source.Height);
                }
                
                if (StatusText.Text == "Connected - Receiving screen...")
                {
                    StatusText.Text = "Receiving screen... - Click to control";
                }
                

            }
            catch (Exception ex)
            {
                LanDesk.Core.Utilities.Logger.Error($"Error displaying frame: {ex.Message}", ex);
            }
            finally
            {
                bitmap?.Dispose();
                _isUpdatingFrame = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Render); // Use Render priority for smooth updates
    }
    
    private System.Drawing.Point MapLocalToRemote(System.Windows.Point localPoint)
    {
        try
        {
            if (_remoteScreenSize.Width == 0 || _remoteScreenSize.Height == 0)
            {
                LanDesk.Core.Utilities.Logger.Debug($"MapLocalToRemote: Remote screen size not available, using local point: {localPoint}");
                return new System.Drawing.Point((int)localPoint.X, (int)localPoint.Y);
            }

            // Get image bounds relative to the window
            var imageBounds = GetImageBounds();
            if (imageBounds.Width == 0 || imageBounds.Height == 0)
            {
                // This is expected before the first frame is received - log as DEBUG, not WARNING
                if (_firstFrameReceived)
                {
                    LanDesk.Core.Utilities.Logger.Debug($"MapLocalToRemote: Image bounds invalid after first frame: {imageBounds}");
                }
                // Return center of remote screen as fallback
                return new System.Drawing.Point(_remoteScreenSize.Width / 2, _remoteScreenSize.Height / 2);
            }

            // Calculate relative position within the image bounds (localPoint is already relative to window)
            var relativeX = (localPoint.X - imageBounds.Left) / imageBounds.Width;
            var relativeY = (localPoint.Y - imageBounds.Top) / imageBounds.Height;

            // Clamp to valid range [0, 1]
            relativeX = Math.Max(0, Math.Min(1, relativeX));
            relativeY = Math.Max(0, Math.Min(1, relativeY));

            // Map to remote screen coordinates
            var remoteX = (int)(relativeX * _remoteScreenSize.Width);
            var remoteY = (int)(relativeY * _remoteScreenSize.Height);

            LanDesk.Core.Utilities.Logger.Debug($"MapLocalToRemote: Window({localPoint.X:F1},{localPoint.Y:F1}) -> ImageBounds({imageBounds.Left:F1},{imageBounds.Top:F1},{imageBounds.Width:F1},{imageBounds.Height:F1}) -> Relative({relativeX:F3},{relativeY:F3}) -> Remote({remoteX},{remoteY})");
            
            return new System.Drawing.Point(remoteX, remoteY);
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Error("MapLocalToRemote: Error mapping coordinates", ex);
            return new System.Drawing.Point((int)localPoint.X, (int)localPoint.Y);
        }
    }

    private System.Windows.Rect GetImageBounds()
    {
        if (RemoteScreenImage.Source == null)
            return new System.Windows.Rect();

        var imageWidth = RemoteScreenImage.Source.Width;
        var imageHeight = RemoteScreenImage.Source.Height;

        if (imageWidth == 0 || imageHeight == 0)
            return new System.Windows.Rect();

        // Get the Border container (parent of the Image)
        var border = RemoteScreenImage.Parent as FrameworkElement;
        if (border == null)
            return new System.Windows.Rect();

        var containerWidth = border.ActualWidth;
        var containerHeight = border.ActualHeight;

        if (containerWidth == 0 || containerHeight == 0)
            return new System.Windows.Rect();

        // Calculate scale factor (Uniform stretch maintains aspect ratio)
        var scaleX = containerWidth / imageWidth;
        var scaleY = containerHeight / imageHeight;
        var scale = Math.Min(scaleX, scaleY);

        // Calculate actual display size (maintains aspect ratio)
        var displayWidth = imageWidth * scale;
        var displayHeight = imageHeight * scale;

        // Calculate centered position within the Border
        var leftInBorder = (containerWidth - displayWidth) / 2;
        var topInBorder = (containerHeight - displayHeight) / 2;

        // Transform Border position to window coordinates
        var borderTransform = border.TransformToAncestor(this);
        var borderTopLeft = borderTransform.Transform(new System.Windows.Point(0, 0));

        // Return bounds relative to the window
        return new System.Windows.Rect(
            borderTopLeft.X + leftInBorder,
            borderTopLeft.Y + topInBorder,
            displayWidth,
            displayHeight);
    }
    
    private void Border_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HandleMouseMove(e);
    }

    private void RemoteScreenImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HandleMouseMove(e);
    }

    private void HandleMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        // Don't process mouse input until we've received the first frame
        if (!_isReceiving || !_firstFrameReceived || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        try
        {
            // Get position relative to the window (not the image)
            var position = e.GetPosition(this);
            var remotePoint = MapLocalToRemote(position);
            _inputSender.SendMouseMove(remotePoint.X, remotePoint.Y);
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Debug($"Error in HandleMouseMove: {ex.Message}");
        }
    }

    private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandleMouseDown(e);
    }

    private void RemoteScreenImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandleMouseDown(e);
        e.Handled = true;
    }

    private void HandleMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        // Don't process mouse input until we've received the first frame
        if (!_isReceiving || !_firstFrameReceived || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        try
        {
            // Focus window to capture keyboard
            Focus();
            
            // Get position relative to the window (not the image)
            var position = e.GetPosition(this);
            var remotePoint = MapLocalToRemote(position);
            var isRightButton = e.RightButton == System.Windows.Input.MouseButtonState.Pressed || 
                              e.ChangedButton == System.Windows.Input.MouseButton.Right;
            _inputSender.SendMouseClick(remotePoint.X, remotePoint.Y, isRightButton);
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Debug($"Error in HandleMouseDown: {ex.Message}");
        }
    }

    private void Border_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Mouse up is handled in MouseDown for click
    }

    private void RemoteScreenImage_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Mouse up is handled in MouseDown for click
    }

    private DateTime _lastClickTime = DateTime.MinValue;
    private System.Windows.Point _lastClickPosition = new System.Windows.Point(-1, -1);

    private void Border_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        HandleMouseWheel(e);
    }

    private void RemoteScreenImage_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        HandleMouseWheel(e);
    }

    private void HandleMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        // Don't process mouse input until we've received the first frame
        if (!_isReceiving || !_firstFrameReceived || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        try
        {
            _inputSender.SendMouseWheel(e.Delta);
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Debug($"Error in HandleMouseWheel: {ex.Message}");
        }
    }

    private void HandleDoubleClick(System.Windows.Point position)
    {
        if (!_isReceiving || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        var now = DateTime.Now;
        
        // Check for double-click (within 500ms and 5 pixels)
        if ((now - _lastClickTime).TotalMilliseconds < 500 &&
            Math.Abs(position.X - _lastClickPosition.X) < 5 &&
            Math.Abs(position.Y - _lastClickPosition.Y) < 5)
        {
            var remotePoint = MapLocalToRemote(position);
            _inputSender.SendMouseClick(remotePoint.X, remotePoint.Y, false, true);
            _lastClickTime = DateTime.MinValue; // Reset to prevent triple-click
        }
        else
        {
            _lastClickTime = now;
            _lastClickPosition = position;
        }
    }

    private void RemoteDesktopWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    private void RemoteDesktopWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    private void HandleKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!_isReceiving || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        try
        {
            var virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
            if (virtualKey != 0 && virtualKey != 91) // Ignore Windows key
            {
                _inputSender.SendKeyDown(virtualKey, IsExtendedKey(e.Key));
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in HandleKeyDown: {ex.Message}");
        }
    }

    private void RemoteDesktopWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyUp(e);
    }

    private void RemoteDesktopWindow_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyUp(e);
    }

    private void HandleKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        if (!_isReceiving || _inputConnection == null || !_inputConnection.Connected || _inputSender == null)
            return;

        try
        {
            var virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
            if (virtualKey != 0 && virtualKey != 91) // Ignore Windows key
            {
                _inputSender.SendKeyUp(virtualKey, IsExtendedKey(e.Key));
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in HandleKeyUp: {ex.Message}");
        }
    }

    private bool IsExtendedKey(System.Windows.Input.Key key)
    {
        // Extended keys include arrow keys, numpad keys, etc.
        return key == System.Windows.Input.Key.Left ||
               key == System.Windows.Input.Key.Right ||
               key == System.Windows.Input.Key.Up ||
               key == System.Windows.Input.Key.Down ||
               key == System.Windows.Input.Key.Insert ||
               key == System.Windows.Input.Key.Delete ||
               key == System.Windows.Input.Key.Home ||
               key == System.Windows.Input.Key.End ||
               key == System.Windows.Input.Key.PageUp ||
               key == System.Windows.Input.Key.PageDown;
    }

    private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
    {
        var memory = new MemoryStream();
        try
        {
            // Save as BMP (fastest for memory-to-memory transfer, no compression overhead)
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // Loads image immediately so stream can be closed
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // Needed for cross-thread access
            
            // After Freeze(), the image is loaded into memory, so we can dispose the stream
            memory.Dispose();
            
            return bitmapImage;
        }
        catch (Exception ex)
        {
            memory?.Dispose();
            LanDesk.Core.Utilities.Logger.Error($"BitmapToBitmapImage: Error converting bitmap: {ex.Message}", ex);
            throw;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            FullScreenButton.Content = "Full Screen";
        }
        else
        {
            WindowState = WindowState.Maximized;
            WindowStyle = WindowStyle.None;
            FullScreenButton.Content = "Exit Full Screen";
        }
    }

    private void UacYesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputConnection == null || !_inputConnection.Connected || _inputSender == null) return;
        try
        {
            _inputSender.SendUacYes();
            LanDesk.Core.Utilities.Logger.Info("RemoteDesktopWindow: Sent UAC Yes");
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Debug($"UAC Yes failed: {ex.Message}");
        }
    }

    private void UacNoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputConnection == null || !_inputConnection.Connected || _inputSender == null) return;
        try
        {
            _inputSender.SendUacNo();
            LanDesk.Core.Utilities.Logger.Info("RemoteDesktopWindow: Sent UAC No");
        }
        catch (Exception ex)
        {
            LanDesk.Core.Utilities.Logger.Debug($"UAC No failed: {ex.Message}");
        }
    }

    private void UacCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputConnection == null || !_inputConnection.Connected || _inputSender == null) return;
        var dialog = new UacCredentialsWindow { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Username))
        {
            try
            {
                _inputSender.SendUacCredentials(dialog.Username, dialog.Password ?? string.Empty);
                LanDesk.Core.Utilities.Logger.Info("RemoteDesktopWindow: Sent UAC credentials");
            }
            catch (Exception ex)
            {
                LanDesk.Core.Utilities.Logger.Debug($"UAC credentials failed: {ex.Message}");
            }
        }
    }

    private void RemoteDesktopWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isReceiving = false;
        _firstFrameReceived = false;
        
        try
        {
            _screenReceiver?.StopReceiving();
        }
        catch { }
        
        try
        {
            _inputSender?.Dispose();
        }
        catch { }
        
        try
        {
            _screenReceiver?.Dispose();
        }
        catch { }
        
        try
        {
            _screenConnection?.Close();
            _screenConnection?.Dispose();
        }
        catch { }
        
        try
        {
            _inputConnection?.Close();
            _inputConnection?.Dispose();
        }
        catch { }
        
        _screenConnection = null;
        _inputConnection = null;
    }
}
