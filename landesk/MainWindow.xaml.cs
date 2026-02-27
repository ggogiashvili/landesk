using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LanDesk.Core.Configuration;
using LanDesk.Core.Models;
using LanDesk.Core.Services;
using LanDesk.Core.Utilities;

namespace LanDesk;

public partial class MainWindow : Window
{
    private readonly DiscoveryService _discoveryService;
    private readonly ServerDiscoveryService? _serverDiscoveryService;
    private readonly ConnectionManager _connectionManager;
    private readonly ScreenCaptureService _screenCapture;
    private readonly ScreenStreamingService _screenStreaming;
    private readonly InputReceiverService _inputReceiver;
    private readonly ConnectionManager _inputConnectionManager;
    private readonly ObservableCollection<DiscoveredDevice> _devices;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly string _pairingCode;
    private System.Net.Sockets.TcpClient? _currentStreamingClient;
    private System.Net.Sockets.TcpClient? _currentInputClient;
    private ApprovalClientService? _approvalClient;
    private ScreenCaptureHelperClient? _helperClient;
    private ScreenCaptureService? _helperScreenCapture;
    private readonly HashSet<string> _approvedIncomingIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly object _approvedIncomingLock = new object();
    
    // Server configuration - can be set via environment variables:
    // LANDESK_SERVER_IP - IP address of discovery server (e.g., "10.246.84.208" or "192.168.1.100")
    // LANDESK_SERVER_PORT - Port of discovery server (default: 8530, SCCM-allowed)
    // If LANDESK_SERVER_IP is not set, server discovery is disabled
    private static string GetDiscoveryServerIp()
    {
        var envIp = Environment.GetEnvironmentVariable("LANDESK_SERVER_IP");
        if (!string.IsNullOrWhiteSpace(envIp))
            return envIp;
        
        // Fallback to hardcoded IP if environment variable not set
        // TODO: Remove this fallback and require environment variable
        return "10.246.84.208";
    }
    
    private static int GetDiscoveryServerPort()
    {
        return NetworkConfiguration.DiscoveryServerPort;
    }

    public MainWindow()
    {
        // Write to temp file immediately
        try
        {
            var tempLog = Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt");
            File.AppendAllText(tempLog, $"[{DateTime.Now}] MainWindow constructor called\n");
        }
        catch { }
        
        try
        {
            Logger.Info("Initializing MainWindow...");
            var tempLog = Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt");
            File.AppendAllText(tempLog, $"[{DateTime.Now}] About to call InitializeComponent\n");
            InitializeComponent();
            File.AppendAllText(tempLog, $"[{DateTime.Now}] InitializeComponent completed\n");
            Logger.Info("MainWindow XAML loaded");

            // Get or create persistent device ID and pairing code (never changes)
            _deviceId = PersistentStorage.GetOrCreateDeviceId();
            _deviceName = Environment.MachineName;
            _pairingCode = PersistentStorage.GetOrCreatePairingCode();
            
            Logger.Info($"Device ID: {_deviceId}");
            Logger.Info($"Device Name: {_deviceName}");
            Logger.Info($"Pairing Code: {PairingCodeGenerator.FormatPairingCode(_pairingCode)}");

            // Initialize services
            Logger.Info("Initializing services...");
            _discoveryService = new DiscoveryService(_deviceId, _deviceName, NetworkConfiguration.DefaultDiscoveryPort, _pairingCode);
            
            // Initialize server discovery service (only if server IP is configured)
            try
            {
                var serverIp = GetDiscoveryServerIp();
                var serverPort = GetDiscoveryServerPort();
                
                if (string.IsNullOrWhiteSpace(serverIp))
                {
                    Logger.Info("Server discovery disabled - LANDESK_SERVER_IP environment variable not set");
                }
                else
                {
                    var localIp = GetLocalIPAddress();
                    _serverDiscoveryService = new ServerDiscoveryService(serverIp, serverPort, 
                        _deviceId, _deviceName, NetworkConfiguration.DefaultControlPort, _pairingCode);
                    Logger.Info($"ServerDiscoveryService initialized for server at {serverIp}:{serverPort}");
                    
                    // Register with server immediately (with retries)
                    Logger.Info("Registering with discovery server...");
                    var serverServiceRef = _serverDiscoveryService; // Capture for closure
                    var localIpStr = localIp.ToString();
                    
                    // Register in background but with retries
                    _ = Task.Run(async () =>
                    {
                        int maxRetries = 5;
                        int retryDelay = 2000; // 2 seconds
                        
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            try
                            {
                                Logger.Info($"Registration attempt {attempt}/{maxRetries}...");
                                var registered = await serverServiceRef.RegisterAsync(localIpStr);
                                if (registered)
                                {
                                    Logger.Info($"Successfully registered with discovery server at {localIpStr}");
                                    Dispatcher.Invoke(() => UpdateStatus("Registered with discovery server"));
                                    return; // Success, exit retry loop
                                }
                                else
                                {
                                    Logger.Warning($"Registration attempt {attempt} failed");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning($"Registration attempt {attempt} error: {ex.Message}");
                            }
                            
                            // Wait before retry (except on last attempt)
                            if (attempt < maxRetries)
                            {
                                await Task.Delay(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                        
                        // All retries failed
                        Logger.Error("Failed to register with discovery server after all retries");
                        Dispatcher.Invoke(() => UpdateStatus("Warning: Server registration failed - some features may not work"));
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to initialize server discovery service: {ex.Message}");
            }
            
            _connectionManager = new ConnectionManager(NetworkConfiguration.DefaultControlPort); // Screen streaming port
            _inputConnectionManager = new ConnectionManager(NetworkConfiguration.DefaultInputPort); // Input port
            _screenCapture = new ScreenCaptureService();
            _screenStreaming = new ScreenStreamingService(_screenCapture);
            _inputReceiver = new InputReceiverService();
            Logger.Info("Services initialized");

            // Setup event handlers
            Logger.Info("Registering event handlers...");
            _discoveryService.DeviceDiscovered += OnDeviceDiscovered;
            _discoveryService.DeviceUpdated += OnDeviceUpdated;
            _discoveryService.DeviceOffline += OnDeviceOffline;

            _connectionManager.ConnectionEstablished += OnConnectionEstablished;
            
            // Handle incoming connections for screen sharing
            _connectionManager.ConnectionEstablished += OnIncomingConnection;
            _screenStreaming.StreamingStopped += OnLocalStreamingStopped;

            // Initialize device collection
            Logger.Info("Initializing device collection...");
            _devices = new ObservableCollection<DiscoveredDevice>();
            DeviceListView.ItemsSource = _devices;
            Logger.Info("Device collection initialized");

            // Check if service is running first
            bool serviceRunning = false;
            try
            {
                var service = ServiceController.GetServices()
                    .FirstOrDefault(s => s.ServiceName == "LanDesk Remote Desktop Service");
                serviceRunning = (service != null && service.Status == ServiceControllerStatus.Running);
                if (serviceRunning)
                {
                    Logger.Info("LanDesk Service is running - GUI app will NOT start listeners (service handles all incoming connections)");
                }
            }
            catch
            {
                // Service check failed
            }
            
            // Only start listeners if service is NOT running
            if (!serviceRunning)
            {
                // Start listening for discovery requests and connections
                try
                {
                    Logger.Info("Starting discovery service...");
                    _discoveryService.StartListening();
                    UpdateStatus("Discovery service started");
                    Logger.Info("Discovery service started successfully");
                }
                catch (Exception ex)
                {
                    var isPortConflict = ex.Message.Contains("already in use") || 
                                        ex.Message.Contains("address already") ||
                                        ex.InnerException?.Message?.Contains("address already") == true ||
                                        ex.Message.Contains("normally permitted");
                    
                    if (isPortConflict)
                    {
                        Logger.Warning($"Discovery: Port {NetworkConfiguration.DefaultDiscoveryPort} (UDP) is in use: {ex.Message}");
                        UpdateStatus($"Discovery: Port {NetworkConfiguration.DefaultDiscoveryPort} (UDP) is in use");
                    }
                    else
                    {
                        Logger.Warning($"Discovery: Failed to start: {ex.Message}");
                        UpdateStatus("Discovery warning: " + ex.Message);
                    }
                }

                try
                {
                    Logger.Info("Starting screen streaming service...");
                    _connectionManager.StartListening();
                    UpdateStatus("Screen streaming service started");
                    Logger.Info("Screen streaming service started successfully");
                }
                catch (Exception ex)
                {
                    var isPortConflict = ex.Message.Contains("already in use") || 
                                        ex.Message.Contains("address already") ||
                                        ex.InnerException?.Message?.Contains("address already") == true ||
                                        ex.Message.Contains("normally permitted");
                    
                    if (isPortConflict)
                    {
                        Logger.Warning($"Screen streaming: Port {NetworkConfiguration.DefaultControlPort} (TCP) is in use: {ex.Message}");
                        UpdateStatus($"Screen streaming: Port {NetworkConfiguration.DefaultControlPort} (TCP) is in use");
                    }
                    else
                    {
                        Logger.Warning($"Screen streaming: Failed to start: {ex.Message}");
                        UpdateStatus("Screen streaming warning: " + ex.Message);
                    }
                }

                try
                {
                    Logger.Info("Starting input service...");
                    _inputConnectionManager.StartListening();
                    _inputConnectionManager.ConnectionEstablished += OnIncomingInputConnection;
                    UpdateStatus("Input service started");
                    Logger.Info("Input service started successfully");
                }
                catch (Exception ex)
                {
                    var isPortConflict = ex.Message.Contains("already in use") || 
                                        ex.Message.Contains("address already") ||
                                        ex.InnerException?.Message?.Contains("address already") == true ||
                                        ex.Message.Contains("normally permitted");
                    
                    if (isPortConflict)
                    {
                        Logger.Warning($"Input service: Port {NetworkConfiguration.DefaultInputPort} (TCP) is in use: {ex.Message}");
                        UpdateStatus($"Input service: Port {NetworkConfiguration.DefaultInputPort} (TCP) is in use");
                    }
                    else
                    {
                        Logger.Warning($"Input service: Failed to start: {ex.Message}");
                        UpdateStatus("Input service warning: " + ex.Message);
                    }
                }
            }
            else
            {
                Logger.Info("Skipping listener startup - Service is running and will handle all incoming connections");
                UpdateStatus("Service is running - open this app to approve incoming connections");
                StartApprovalClient();
                StartScreenCaptureHelperClient();
                if (ApprovalHintText != null)
                {
                    ApprovalHintText.Text = "This window is for: pairing code, discovery, and approving incoming connections. Keep this app open for screen sharing (sends desktop to service).";
                    ApprovalHintText.Visibility = Visibility.Visible;
                }
            }

            // UpdateLocalPairingCode will be called in MainWindow_Loaded after window is fully initialized
            
            // Discover devices from server immediately (async, don't block)
            _ = Task.Run(async () =>
            {
                if (_serverDiscoveryService != null)
                {
                    // Wait a moment for registration to complete
                    await Task.Delay(1000);
                    
                    Logger.Info("Querying discovery server for available devices...");
                    try
                    {
                        var serverDevices = await _serverDiscoveryService.DiscoverDevicesAsync();
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var device in serverDevices)
                            {
                                if (!_devices.Any(d => d.DeviceId == device.DeviceId))
                                {
                                    device.PairingCode = PairingCodeGenerator.FormatPairingCode(device.PairingCode);
                                    _devices.Add(device);
                                    Logger.Info($"Discovered device from server: {device.DeviceName} ({device.PairingCode}) at {device.IpAddress}");
                                }
                            }
                        });
                        Logger.Info($"Found {serverDevices.Count} device(s) from discovery server");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error discovering devices from server: {ex.Message}");
                    }
                }
            });
            
            // Final status summary
            if (serviceRunning)
            {
                UpdateStatus("Ready - Service is running (handling incoming connections) - You can connect to other devices");
                Logger.Info("=== Service Status Summary ===");
                Logger.Info("LanDesk Service: RUNNING");
                Logger.Info($"Service handles: Discovery (UDP {NetworkConfiguration.DefaultDiscoveryPort}), Screen Streaming (TCP {NetworkConfiguration.DefaultControlPort}), Input Control (TCP {NetworkConfiguration.DefaultInputPort})");
                Logger.Info("GUI app can: Connect to other devices, Accept connections through service");
            }
            else
            {
                UpdateStatus("Ready - All services started");
                Logger.Info("=== Service Status Summary ===");
                Logger.Info("LanDesk Service: NOT running");
                Logger.Info("GUI app is handling all connections directly");
            }
            
            Logger.Info("MainWindow initialization completed successfully");
        }
        catch (Exception ex)
        {
            try
            {
                Logger.Error("Fatal error during MainWindow initialization", ex);
            }
            catch
            {
                // If logging fails, write to a simple text file
                try
                {
                    var simpleLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDesk", "startup-error.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(simpleLog)!);
                    File.AppendAllText(simpleLog, $"[{DateTime.Now}] FATAL ERROR in MainWindow: {ex.Message}\nStack: {ex.StackTrace}\n\n");
                }
                catch { }
            }
            
            var errorMsg = $"Failed to initialize application: {ex.Message}\n\n";
            try
            {
                errorMsg += $"Stack Trace:\n{ex.StackTrace}\n\n";
                errorMsg += $"Log file: {Logger.LogFilePath}";
            }
            catch 
            {
                errorMsg += $"\n\nCheck: %LOCALAPPDATA%\\LanDesk\\startup-error.txt";
            }
            
            // Show error and keep window open
            try
            {
                MessageBox.Show(errorMsg, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // If MessageBox fails, at least try to log it
                try
                {
                    var simpleLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDesk", "startup-error.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(simpleLog)!);
                    File.AppendAllText(simpleLog, $"[{DateTime.Now}] Could not show error dialog: {ex.Message}\n");
                }
                catch { }
            }
            
            // Don't throw - let the window show so user can see the error
            // The window should still be visible even if initialization partially failed
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure UI elements are fully loaded before updating
        UpdateLocalPairingCode();
    }

    private void UpdateLocalPairingCode()
    {
        try
        {
            if (LocalPairingCodeText != null)
            {
                LocalPairingCodeText.Text = PairingCodeGenerator.FormatPairingCode(_pairingCode);
                Logger.Info($"Updated pairing code display: {PairingCodeGenerator.FormatPairingCode(_pairingCode)}");
            }
            else
            {
                Logger.Warning("LocalPairingCodeText is null");
            }
            
            // Update device name display
            if (LocalDeviceNameText != null)
            {
                LocalDeviceNameText.Text = _deviceName;
                Logger.Info($"Updated device name display: {_deviceName}");
            }
            else
            {
                Logger.Warning("LocalDeviceNameText is null");
            }
            
            // Update local IP address display
            if (LocalIpAddressText != null)
            {
                try
                {
                    var localIp = GetLocalIPAddress();
                    var ipText = localIp?.ToString() ?? "Not Available";
                    LocalIpAddressText.Text = ipText;
                    Logger.Info($"Updated IP address display: {ipText}");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to get local IP address: {ex.Message}");
                    LocalIpAddressText.Text = "Not Available";
                }
            }
            else
            {
                Logger.Warning("LocalIpAddressText is null");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error in UpdateLocalPairingCode: {ex.Message}", ex);
        }
    }

    private void OnDeviceDiscovered(object? sender, DiscoveredDevice device)
    {
        Dispatcher.Invoke(() =>
        {
            // Format pairing code for display
            if (!string.IsNullOrEmpty(device.PairingCode))
            {
                device.PairingCode = PairingCodeGenerator.FormatPairingCode(device.PairingCode);
            }
            _devices.Add(device);
            UpdateStatus($"Discovered: {device.DeviceName} ({device.PairingCode})");
        });
    }

    private void OnDeviceUpdated(object? sender, DiscoveredDevice device)
    {
        Dispatcher.Invoke(() =>
        {
            // Format pairing code for display
            if (!string.IsNullOrEmpty(device.PairingCode))
            {
                device.PairingCode = PairingCodeGenerator.FormatPairingCode(device.PairingCode);
            }
            var existing = _devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing != null)
            {
                var index = _devices.IndexOf(existing);
                _devices[index] = device;
            }
        });
    }

    private void OnDeviceOffline(object? sender, string deviceId)
    {
        Dispatcher.Invoke(() =>
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device != null)
            {
                var index = _devices.IndexOf(device);
                _devices[index].IsOnline = false;
            }
        });
    }

    private void OnConnectionEstablished(object? sender, System.Net.Sockets.TcpClient client)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateStatus($"Incoming connection from {client.Client.RemoteEndPoint}");
        });
    }

    private static string GetRemoteIp(System.Net.Sockets.TcpClient client)
    {
        try
        {
            var s = client?.Client?.RemoteEndPoint?.ToString() ?? "";
            var colon = s.LastIndexOf(':');
            return colon >= 0 ? s.Substring(0, colon) : s;
        }
        catch { return ""; }
    }

    private void OnLocalStreamingStopped(object? sender, string? remoteEndPoint)
    {
        if (string.IsNullOrEmpty(remoteEndPoint)) return;
        var ip = remoteEndPoint.Contains(":") ? remoteEndPoint.Substring(0, remoteEndPoint.LastIndexOf(':')) : remoteEndPoint;
        lock (_approvedIncomingLock) { _approvedIncomingIps.Remove(ip); }
    }

    private void OnIncomingConnection(object? sender, System.Net.Sockets.TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        bool approved = false;
        Dispatcher.Invoke(() =>
        {
            approved = MessageBox.Show(
                $"Allow remote desktop connection from {remote}?",
                "Incoming Connection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        });
        if (!approved)
        {
            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
            UpdateStatus($"Connection from {remote} rejected");
            return;
        }
        lock (_approvedIncomingLock) { _approvedIncomingIps.Add(GetRemoteIp(client)); }
        Dispatcher.Invoke(() =>
        {
            if (_currentStreamingClient != null)
                _screenStreaming.StopStreaming();
            _currentStreamingClient = client;
            _screenStreaming.StartStreaming(client, frameRate: 20, quality: 80);
            UpdateStatus($"Screen sharing started with {remote}");
        });
    }

    private void OnIncomingInputConnection(object? sender, System.Net.Sockets.TcpClient client)
    {
        var ip = GetRemoteIp(client);
        lock (_approvedIncomingLock)
        {
            if (!_approvedIncomingIps.Contains(ip))
            {
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
                UpdateStatus($"Input connection from {client.Client.RemoteEndPoint} rejected (not approved)");
                return;
            }
        }
        Dispatcher.Invoke(() =>
        {
            if (_currentInputClient != null)
                _inputReceiver.StopReceiving();
            _currentInputClient = client;
            _inputReceiver.StartReceiving(client);
            UpdateStatus($"Remote control enabled from {client.Client.RemoteEndPoint}");
        });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _devices.Clear();
        
        // Start UDP discovery
        _discoveryService.StartDiscovery();
        UpdateStatus("Scanning network (UDP + Server)...");
        
        // Also query server for devices
        if (_serverDiscoveryService != null)
        {
            try
            {
                var serverDevices = await _serverDiscoveryService.DiscoverDevicesAsync();
                foreach (var device in serverDevices)
                {
                    if (!_devices.Any(d => d.DeviceId == device.DeviceId))
                    {
                        device.PairingCode = PairingCodeGenerator.FormatPairingCode(device.PairingCode);
                        _devices.Add(device);
                    }
                }
                Logger.Info($"Found {serverDevices.Count} device(s) via discovery server");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error querying discovery server: {ex.Message}");
            }
        }
    }

    private void StartDiscoveryButton_Click(object sender, RoutedEventArgs e)
    {
        _discoveryService.StartDiscovery();
        StartDiscoveryButton.IsEnabled = false;
        StopDiscoveryButton.IsEnabled = true;
        UpdateStatus("Discovery started...");
    }

    private void StopDiscoveryButton_Click(object sender, RoutedEventArgs e)
    {
        _discoveryService.StopDiscovery();
        StartDiscoveryButton.IsEnabled = true;
        StopDiscoveryButton.IsEnabled = false;
        UpdateStatus("Discovery stopped.");
    }

    private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConnectButton.IsEnabled = DeviceListView.SelectedItem != null &&
                                  DeviceListView.SelectedItem is DiscoveredDevice device &&
                                  device.IsOnline;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceListView.SelectedItem is not DiscoveredDevice device)
            return;

        try
        {
            Logger.Info($"=== Connection Attempt Started ===");
            Logger.Info($"Target Device: {device.DeviceName} ({device.DeviceId})");
            Logger.Info($"Target IP: {device.IpAddress}");
            Logger.Info($"Target Pairing Code: {device.PairingCode}");
            UpdateStatus($"Connecting to {device.DeviceName}...");
            ConnectButton.IsEnabled = false;

            var connection = await _connectionManager.ConnectToDeviceAsync(device);
            Logger.Info($"Connection result: {(connection != null && connection.Connected ? "SUCCESS" : "FAILED")}");

            if (connection != null && connection.Connected)
            {
                UpdateStatus($"Connected to {device.DeviceName}");
                
                // Open remote desktop window to view their screen
                var remoteDesktopWindow = new RemoteDesktopWindow(device);
                remoteDesktopWindow.Show();
            }
            else
            {
                UpdateStatus("Connection failed.");
                MessageBox.Show($"Failed to connect to {device.DeviceName}.\n\n" +
                              "The device may be offline or the connection was refused.",
                              "Connection Failed",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception during connection to {device.DeviceName}", ex);
            UpdateStatus($"Error: {ex.Message}");
            MessageBox.Show($"Error connecting to device: {ex.Message}",
                          "Error",
                          MessageBoxButton.OK,
                          MessageBoxImage.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = DeviceListView.SelectedItem != null;
        }
    }

    private void IpAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox == null || ConnectByIpButton == null) return;

        try
        {
            var text = textBox.Text.Trim();
            var isValid = !string.IsNullOrWhiteSpace(text) && 
                         (IsValidIpAddress(text) || IsValidDomainName(text));
            
            ConnectByIpButton.IsEnabled = isValid;
        }
        catch
        {
            ConnectByIpButton.IsEnabled = false;
        }
    }

    private bool IsValidIpAddress(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        
        return IPAddress.TryParse(input, out _);
    }

    private bool IsValidDomainName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        
        // Basic domain name validation
        // Must contain at least one dot and valid characters
        if (input.Length > 253) // Max domain name length
            return false;
        
        // Check for valid characters (letters, numbers, dots, hyphens)
        if (!System.Text.RegularExpressions.Regex.IsMatch(input, @"^[a-zA-Z0-9.-]+$"))
            return false;
        
        // Must contain at least one dot (for domain)
        if (!input.Contains('.'))
            return false;
        
        // Must not start or end with dot or hyphen
        if (input.StartsWith('.') || input.EndsWith('.') || 
            input.StartsWith('-') || input.EndsWith('-'))
            return false;
        
        return true;
    }

    private async void ConnectByIpButton_Click(object sender, RoutedEventArgs e)
    {
        var hostnameOrIp = IpAddressTextBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(hostnameOrIp))
        {
            MessageBox.Show("Please enter an IP address or domain name.", "Invalid Input", 
                          MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!IsValidIpAddress(hostnameOrIp) && !IsValidDomainName(hostnameOrIp))
        {
            MessageBox.Show("Please enter a valid IP address (e.g., 192.168.1.100) or domain name (e.g., computer.example.com).", 
                          "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Logger.Info($"=== Direct Connection by IP/Domain Started ===");
            Logger.Info($"Target: {hostnameOrIp}");
            UpdateStatus($"Connecting to {hostnameOrIp}...");
            ConnectByIpButton.IsEnabled = false;

            // Create a temporary device object for connection
            var tempDevice = new DiscoveredDevice
            {
                DeviceId = "direct-connection",
                DeviceName = hostnameOrIp,
                IpAddress = IPAddress.Any, // Will be resolved during connection
                ControlPort = NetworkConfiguration.DefaultControlPort,
                DiscoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
                PairingCode = "DIRECT",
                OperatingSystem = "Unknown",
                LastSeen = DateTime.Now,
                IsOnline = true
            };

            // Connect directly using IP/domain (no server needed)
            Logger.Info($"Connecting directly to {hostnameOrIp}:{NetworkConfiguration.DefaultControlPort} (screen streaming)...");
            var screenConnection = await _connectionManager.ConnectToDeviceAsync(hostnameOrIp, NetworkConfiguration.DefaultControlPort, hostnameOrIp);
            
            if (screenConnection == null || !screenConnection.Connected)
            {
                Logger.Warning($"Failed to connect to {hostnameOrIp}:{NetworkConfiguration.DefaultControlPort}");
                UpdateStatus("Connection failed.");
                MessageBox.Show($"Failed to connect to {hostnameOrIp}.\n\n" +
                              "Possible reasons:\n" +
                              "• The device is not running LanDesk\n" +
                              "• The device is offline\n" +
                              "• Firewall is blocking the connection\n" +
                              "• Incorrect IP address or domain name\n" +
                              $"• Port {NetworkConfiguration.DefaultControlPort} is not accessible",
                              "Connection Failed",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                ConnectByIpButton.IsEnabled = true;
                return;
            }

            Logger.Info($"Screen connection established to {hostnameOrIp}");

            // Resolve IP for the device object (for display)
            try
            {
                if (IPAddress.TryParse(hostnameOrIp, out var ip))
                {
                    tempDevice.IpAddress = ip;
                }
                else
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(hostnameOrIp);
                    tempDevice.IpAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) 
                                          ?? hostEntry.AddressList[0];
                    tempDevice.DeviceName = $"{hostnameOrIp} ({tempDevice.IpAddress})";
                }
            }
            catch
            {
                // Use the hostname as-is if resolution fails
            }

            UpdateStatus($"Connected to {hostnameOrIp}");
            
            // Open remote desktop window
            var remoteDesktopWindow = new RemoteDesktopWindow(tempDevice);
            remoteDesktopWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception during direct connection to {hostnameOrIp}: {ex.Message}", ex);
            UpdateStatus($"Error: {ex.Message}");
            MessageBox.Show($"Error connecting to {hostnameOrIp}: {ex.Message}",
                          "Error",
                          MessageBoxButton.OK,
                          MessageBoxImage.Error);
        }
        finally
        {
            ConnectByIpButton.IsEnabled = true;
        }
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = Logger.GetLogDirectory();
            Logger.Info($"Opening log folder: {logDir}");
            
            if (Directory.Exists(logDir))
            {
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            else
            {
                MessageBox.Show($"Log directory not found: {logDir}\n\n" +
                              $"Log file location: {Logger.LogFilePath}",
                              "Log Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open log folder", ex);
            MessageBox.Show($"Failed to open log folder: {ex.Message}\n\n" +
                          $"Log file: {Logger.LogFilePath}",
                          "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Application shutting down");
        Close();
    }

    public void ExplicitExit()
    {
        Logger.Info("MainWindow: Closing explicitly");
        Close();
    }

    private void StartApprovalClient()
    {
        if (_approvalClient != null) return;
        _approvalClient = new ApprovalClientService(NetworkConfiguration.ApprovalPort);
        _approvalClient.OnApprovalRequested = async (remoteEndPoint) =>
        {
            bool result = false;
            await Dispatcher.InvokeAsync(() =>
            {
                result = MessageBox.Show(
                    $"Allow remote desktop connection from {remoteEndPoint}?",
                    "Incoming Connection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
            });
            return result;
        };
        _approvalClient.Start();
        Logger.Info("Approval client started - you will be prompted to allow or deny incoming connections");
    }

    /// <summary>
    /// When the service is running, the service cannot capture the desktop (Session 0).
    /// This client runs in the user session, captures the screen, and sends frames to the service via named pipe.
    /// Keep the LanDesk app open so screen sharing works.
    /// </summary>
    private void StartScreenCaptureHelperClient()
    {
        if (_helperClient != null) return;
        try
        {
            _helperScreenCapture = new ScreenCaptureService();
            _helperClient = new ScreenCaptureHelperClient(NetworkConfiguration.ScreenCapturePipeName, _helperScreenCapture);
            _helperClient.Start();
            Logger.Info("Screen capture helper client started - sending desktop to service via pipe (keep this app open for screen sharing)");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Screen capture helper client failed to start: {ex.Message}. Open this app on the host for screen sharing to work.");
        }
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    private System.Net.IPAddress GetLocalIPAddress()
    {
        foreach (var networkInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                continue;

            var properties = networkInterface.GetIPProperties();
            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(address.Address))
                {
                    return address.Address;
                }
            }
        }

        return System.Net.IPAddress.Loopback;
    }

    private void PairingCodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox == null || ConnectByCodeButton == null) return;

        try
        {
            // Enable button if there's any text (no restrictions)
            var text = textBox.Text;
            ConnectByCodeButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
        }
        catch
        {
            ConnectByCodeButton.IsEnabled = false;
        }
    }

    private async void ConnectByCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var input = PairingCodeTextBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show("Please enter a pairing code, device name, or IP address.", "Invalid Input", 
                          MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ConnectByCodeButton.IsEnabled = false;
            
            // Check if input is an IP address or domain name
            if (IsValidIpAddress(input) || IsValidDomainName(input))
            {
                // Use direct IP/domain connection
                Logger.Info($"=== Direct Connection by IP/Domain Started ===");
                Logger.Info($"Target: {input}");
                UpdateStatus($"Connecting to {input}...");
                
                // Create a temporary device object for connection
                var tempDevice = new DiscoveredDevice
                {
                    DeviceId = "direct-connection",
                    DeviceName = input,
                    IpAddress = IPAddress.Any,
                    ControlPort = NetworkConfiguration.DefaultControlPort,
                    DiscoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
                    PairingCode = "DIRECT",
                    OperatingSystem = "Unknown",
                    LastSeen = DateTime.Now,
                    IsOnline = true
                };
                
                var connection = await _connectionManager.ConnectToDeviceAsync(input, NetworkConfiguration.DefaultControlPort, input);
                Logger.Info($"Connection result: {(connection != null && connection.Connected ? "SUCCESS" : "FAILED")}");

                if (connection != null && connection.Connected)
                {
                    UpdateStatus($"Connected to {input}");
                    
                    // Open remote desktop window
                    var remoteDesktopWindow = new RemoteDesktopWindow(tempDevice);
                    remoteDesktopWindow.Show();
                }
                else
                {
                    UpdateStatus("Connection failed.");
                    MessageBox.Show($"Failed to connect to {input}.\n\n" +
                                  "The device may be offline or the connection was refused.",
                                  "Connection Failed",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
                ConnectByCodeButton.IsEnabled = true;
                return;
            }
            
            // Check if input is a device name (search in discovered devices)
            var deviceByName = _devices.FirstOrDefault(d => 
                d.DeviceName.Equals(input, StringComparison.OrdinalIgnoreCase));
            
            if (deviceByName != null)
            {
                Logger.Info($"=== Connection by Device Name Started ===");
                Logger.Info($"Target Device Name: {input}");
                UpdateStatus($"Connecting to {deviceByName.DeviceName}...");
                
                var connection = await _connectionManager.ConnectToDeviceAsync(deviceByName);
                Logger.Info($"Connection result: {(connection != null && connection.Connected ? "SUCCESS" : "FAILED")}");

                if (connection != null && connection.Connected)
                {
                    UpdateStatus($"Connected to {deviceByName.DeviceName}");
                    
                    // Open remote desktop window
                    var remoteDesktopWindow = new RemoteDesktopWindow(deviceByName);
                    remoteDesktopWindow.Show();
                }
                else
                {
                    UpdateStatus("Connection failed.");
                    MessageBox.Show($"Failed to connect to {deviceByName.DeviceName}.\n\n" +
                                  "The device may be offline or the connection was refused.",
                                  "Connection Failed",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
                ConnectByCodeButton.IsEnabled = true;
                return;
            }
            
            // Try as pairing code - first check server, then UDP discovery
            var normalizedCode = PairingCodeGenerator.NormalizePairingCode(input);
            if (!PairingCodeGenerator.IsValidPairingCode(normalizedCode))
            {
                MessageBox.Show("Please enter a valid pairing code (format: XXX-XXX-XXX), device name, or IP address.", 
                              "Invalid Input", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                ConnectByCodeButton.IsEnabled = true;
                return;
            }

            Logger.Info($"=== Connection by Code Attempt Started ===");
            Logger.Info($"Target Pairing Code: {normalizedCode}");
            UpdateStatus($"Searching for device with code {PairingCodeGenerator.FormatPairingCode(normalizedCode)}...");
            
            // Try server discovery first
            DiscoveredDevice? device = null;
            if (_serverDiscoveryService != null)
            {
                try
                {
                    device = await _serverDiscoveryService.FindDeviceByCodeAsync(normalizedCode);
                    if (device != null)
                    {
                        Logger.Info($"Found device via server: {device.DeviceName} at {device.IpAddress}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error querying server for device: {ex.Message}");
                }
            }
            
            // If not found via server, try UDP discovery
            if (device == null)
            {
                _discoveryService.StartDiscovery();
                await Task.Delay(3000);
                device = _discoveryService.GetDeviceByPairingCode(normalizedCode);
            }

            if (device == null)
            {
                UpdateStatus("Device not found.");
                MessageBox.Show($"Device with code {PairingCodeGenerator.FormatPairingCode(normalizedCode)} not found.\n\n" +
                              "Please ensure the device is online and registered with the server.",
                              "Device Not Found",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                ConnectByCodeButton.IsEnabled = true;
                return;
            }

            // Connect to the device
            Logger.Info($"Found device: {device.DeviceName} ({device.DeviceId}) at {device.IpAddress}");
            UpdateStatus($"Connecting to {device.DeviceName}...");
            var finalConnection = await _connectionManager.ConnectToDeviceAsync(device);
            Logger.Info($"Connection result: {(finalConnection != null && finalConnection.Connected ? "SUCCESS" : "FAILED")}");

            if (finalConnection != null && finalConnection.Connected)
            {
                UpdateStatus($"Connected to {device.DeviceName}");
                
                // Open remote desktop window
                var remoteDesktopWindow = new RemoteDesktopWindow(device);
                remoteDesktopWindow.Show();
            }
            else
            {
                UpdateStatus("Connection failed.");
                MessageBox.Show($"Failed to connect to {device.DeviceName}.\n\n" +
                              "The device may be offline or the connection was refused.",
                              "Connection Failed",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception during connection: {ex.Message}", ex);
            UpdateStatus($"Error: {ex.Message}");
            MessageBox.Show($"Error connecting: {ex.Message}",
                          "Error",
                          MessageBoxButton.OK,
                          MessageBoxImage.Error);
        }
        finally
        {
            ConnectByCodeButton.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unregister from server
        if (_serverDiscoveryService != null)
        {
            Task.Run(async () => await _serverDiscoveryService.UnregisterAsync());
        }
        
        _approvalClient?.Dispose();
        _approvalClient = null;
        _helperClient?.Dispose();
        _helperClient = null;
        _helperScreenCapture?.Dispose();
        _helperScreenCapture = null;
        if (_screenStreaming != null)
            _screenStreaming.StreamingStopped -= OnLocalStreamingStopped;
        _screenStreaming?.StopStreaming();
        _screenStreaming?.Dispose();
        _screenCapture?.Dispose();
        _inputReceiver?.StopReceiving();
        _inputReceiver?.Dispose();
        _discoveryService?.Dispose();
        _serverDiscoveryService?.Dispose();
        _connectionManager?.Dispose();
        base.OnClosed(e);
    }
}

