using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LanDesk.Core.Services;
using LanDesk.Core.Utilities;
using LanDesk.Core.Configuration;
using Microsoft.Extensions.Hosting;

namespace LanDesk.Service.Services;

public class LanDeskBackgroundService : BackgroundService
{
    private readonly DiscoveryService _discoveryService;
    private readonly ServerDiscoveryService? _serverDiscoveryService;
    private readonly ConnectionManager _connectionManager;
    private readonly ConnectionManager _inputConnectionManager;
    private readonly ScreenCaptureHelperService _screenCaptureHelper;
    private readonly ScreenStreamingService _screenStreaming;
    private readonly InputReceiverService _inputReceiver;
    private readonly ConnectionApprovalService _approvalService;
    private readonly HashSet<string> _approvedRemoteIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly object _approvedLock = new object();
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly string _pairingCode;
    
    // Server configuration - can be set via environment variables:
    // LANDESK_SERVER_IP - IP address of discovery server (e.g., "10.246.84.208" or "192.168.1.100")
    // LANDESK_SERVER_PORT - Port of discovery server (default: 10123)
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
        return NetworkConfiguration.DiscoveryServerPort; // Env LANDESK_SERVER_PORT or default 10123 (SCCM-allowed)
    }

    public LanDeskBackgroundService()
    {
        // Get or create persistent device ID and pairing code (never changes)
        _deviceId = PersistentStorage.GetOrCreateDeviceId();
        _deviceName = Environment.MachineName;
        _pairingCode = PersistentStorage.GetOrCreatePairingCode();

        // Initialize services
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
                _serverDiscoveryService = new ServerDiscoveryService(serverIp, serverPort, 
                    _deviceId, _deviceName, NetworkConfiguration.DefaultControlPort, _pairingCode);
                Logger.Info($"ServerDiscoveryService initialized for server at {serverIp}:{serverPort}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to initialize server discovery service: {ex.Message}");
        }
        
        _connectionManager = new ConnectionManager(NetworkConfiguration.DefaultControlPort);
        _inputConnectionManager = new ConnectionManager(NetworkConfiguration.DefaultInputPort);
        // Service runs in Session 0 and cannot capture the desktop directly. Use helper: GUI runs
        // ScreenCaptureHelperClient and sends frames via named pipe; we receive and stream them.
        _screenCaptureHelper = new ScreenCaptureHelperService(NetworkConfiguration.ScreenCapturePipeName);
        _screenCaptureHelper.Start();
        _screenStreaming = new ScreenStreamingService(_screenCaptureHelper);
        _inputReceiver = new InputReceiverService();
        _approvalService = new ConnectionApprovalService(NetworkConfiguration.ApprovalPort);

        _screenStreaming.StreamingStopped += OnStreamingStopped;

        // Setup event handlers
        Logger.Info($"Service: Setting up ConnectionEstablished event handler for screen connection manager...");
        _connectionManager.ConnectionEstablished += OnIncomingScreenConnection;
        Logger.Info($"Service: ConnectionEstablished event handler attached for screen streaming (requires approval)");
        
        Logger.Info($"Service: Setting up ConnectionEstablished event handler for input connection manager...");
        _inputConnectionManager.ConnectionEstablished += OnIncomingInputConnection;
        Logger.Info($"Service: Input ConnectionEstablished event handler attached");
    }

    private static string GetRemoteIp(TcpClient client)
    {
        try
        {
            var ep = client?.Client?.RemoteEndPoint as System.Net.EndPoint;
            if (ep == null) return string.Empty;
            var s = ep.ToString() ?? string.Empty;
            var colon = s.LastIndexOf(':');
            return colon >= 0 ? s.Substring(0, colon) : s;
        }
        catch { return string.Empty; }
    }

    private void OnStreamingStopped(object? sender, string? remoteEndPoint)
    {
        if (string.IsNullOrEmpty(remoteEndPoint)) return;
        var ip = remoteEndPoint.Contains(":") ? remoteEndPoint.Substring(0, remoteEndPoint.LastIndexOf(':')) : remoteEndPoint;
        lock (_approvedLock)
        {
            _approvedRemoteIps.Remove(ip);
            Logger.Info($"Service: Removed approved IP {ip} (stream ended)");
        }
        // Disconnect pipe so host GUI stops capturing and logging when no one is viewing
        _screenCaptureHelper.DisconnectCurrentClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.Info("=== LanDesk Service ExecuteAsync Started ===");
        Logger.Info($"Service Version: 1.0.1");
        Logger.Info($"OS: {Environment.OSVersion}");
        Logger.Info($"Machine: {Environment.MachineName}");
        Logger.Info($"User: {Environment.UserName} (Service Account)");
        Logger.Info($"Working Directory: {Environment.CurrentDirectory}");
        Logger.Info($"Log file: {Logger.LogFilePath}"); // Service logs to the same location as GUI app
        
        try
        {
            // Ensure Windows Firewall allows LanDesk ports (service runs as SYSTEM and can add rules)
            try
            {
                LanDesk.Core.Utilities.FirewallHelper.EnsureFirewallRules();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Service: Firewall setup failed (non-fatal): {ex.Message}");
            }

            // Register with discovery server immediately (with retries)
            if (_serverDiscoveryService != null)
            {
                Logger.Info("Service: Registering with discovery server...");
                int maxRetries = 5;
                int retryDelay = 2000; // 2 seconds
                bool registered = false;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        Logger.Info($"Service: Registration attempt {attempt}/{maxRetries}...");
                        var localIp = GetLocalIPAddress();
                        registered = await _serverDiscoveryService.RegisterAsync(localIp.ToString());
                        if (registered)
                        {
                            Logger.Info($"Service: Successfully registered with discovery server at {localIp}");
                            break; // Success, exit retry loop
                        }
                        else
                        {
                            Logger.Warning($"Service: Registration attempt {attempt} failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Service: Registration attempt {attempt} error: {ex.Message}");
                    }
                    
                    // Wait before retry (except on last attempt)
                    if (attempt < maxRetries && !registered)
                    {
                        await Task.Delay(retryDelay, stoppingToken);
                        retryDelay *= 2; // Exponential backoff
                    }
                }
                
                if (!registered)
                {
                    Logger.Error("Service: Failed to register with discovery server after all retries");
                }
            }
            else
            {
                Logger.Info("Service: Server discovery disabled (LANDESK_SERVER_IP not set)");
            }
            
            // Continue with service startup even if registration failed
            
            // Start all services
            Logger.Info("Service: Starting discovery service...");
            _discoveryService.StartListening();
            Logger.Info("Service: Discovery service started");
            
            Logger.Info($"Service: Starting screen streaming connection manager on port {NetworkConfiguration.DefaultControlPort}...");
            _connectionManager.StartListening();
            Logger.Info("Service: Screen streaming connection manager started");
            
            Logger.Info($"Service: Starting input connection manager on port {NetworkConfiguration.DefaultInputPort}...");
            _inputConnectionManager.StartListening();
            Logger.Info("Service: Input connection manager started");

            Logger.Info("Service: Starting connection approval service (localhost only)...");
            _approvalService.Start();
            Logger.Info($"Service: Approval channel listening on 127.0.0.1:{NetworkConfiguration.ApprovalPort} (open LanDesk app to approve connections)");

            // Log service start
            Logger.Info("=== LanDesk Service Started Successfully ===");
            Logger.Info($"Service: Device ID: {_deviceId}");
            Logger.Info($"Service: Device Name: {_deviceName}");
            Logger.Info($"Service: Pairing Code: {PairingCodeGenerator.FormatPairingCode(_pairingCode)}");
            Logger.Info($"Service: Listening on ports: {NetworkConfiguration.DefaultDiscoveryPort} (UDP), {NetworkConfiguration.DefaultControlPort} (TCP), {NetworkConfiguration.DefaultInputPort} (TCP), {NetworkConfiguration.ApprovalPort} (approval - localhost)");
            Logger.Info("Service: Ready to accept incoming connections");
            
            LogMessage("LanDesk Service started");
            LogMessage($"Device ID: {_deviceId}");
            LogMessage($"Device Name: {_deviceName}");
            LogMessage($"Pairing Code: {PairingCodeGenerator.FormatPairingCode(_pairingCode)}");
            LogMessage($"Listening on ports: {NetworkConfiguration.DefaultDiscoveryPort} (UDP), {NetworkConfiguration.DefaultControlPort} (TCP), {NetworkConfiguration.DefaultInputPort} (TCP)");

            // Keep service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
            
            Logger.Info("Service: Shutdown requested");
        }
        catch (Exception ex)
        {
            Logger.Error($"Service: Fatal error in ExecuteAsync", ex);
            LogMessage($"Service error: {ex.Message}");
        }
        finally
        {
            Logger.Info("Service: ExecuteAsync completed");
        }
    }

    private void OnIncomingScreenConnection(object? sender, System.Net.Sockets.TcpClient client)
    {
        _ = HandleIncomingScreenConnectionAsync(client);
    }

    private async Task HandleIncomingScreenConnectionAsync(System.Net.Sockets.TcpClient client)
    {
        var remoteEndPoint = client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
        try
        {
            Logger.Info($"=== Service: INCOMING SCREEN CONNECTION from {remoteEndPoint} (awaiting approval) ===");
            bool approved = await _approvalService.RequestApprovalAsync(remoteEndPoint);
            if (!approved)
            {
                Logger.Info($"Service: Connection from {remoteEndPoint} REJECTED or timed out - closing viewer connection (no approval client, user said No, or timeout)");
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
                return;
            }
            lock (_approvedLock)
            {
                _approvedRemoteIps.Add(GetRemoteIp(client));
            }
            Logger.Info($"Service: Connection from {remoteEndPoint} APPROVED - starting screen streaming (keep LanDesk app open on this PC for capture)");
            try
            {
                _screenStreaming.StartStreaming(client, frameRate: 30, quality: 75);
                Logger.Info($"Service: Screen sharing started with {remoteEndPoint}");
                LogMessage($"Screen sharing started with {remoteEndPoint}");
            }
            catch (Exception startEx)
            {
                Logger.Error($"Service: StartStreaming failed for {remoteEndPoint} - closing viewer connection. Exception: {startEx.Message}", startEx);
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Service: Error in screen connection handling for {remoteEndPoint} (before or during approval)", ex);
            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
        }
    }

    private void OnIncomingInputConnection(object? sender, System.Net.Sockets.TcpClient client)
    {
        var ip = GetRemoteIp(client);
        lock (_approvedLock)
        {
            if (!_approvedRemoteIps.Contains(ip))
            {
                Logger.Info($"Service: Rejecting input connection from {client.Client.RemoteEndPoint} (not approved - approve screen connection first)");
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
                return;
            }
        }
        try
        {
            Logger.Info($"=== Service: Incoming input connection from {client.Client.RemoteEndPoint} (approved) ===");
            _inputReceiver.StartReceiving(client);
            Logger.Info($"Service: Remote control enabled from {client.Client.RemoteEndPoint}");
            LogMessage($"Remote control enabled from {client.Client.RemoteEndPoint}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Service: Error starting input receiver", ex);
            LogMessage($"Error starting input receiver: {ex.Message}");
        }
    }

    private void LogMessage(string message)
    {
        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        
        // Write to Windows Event Log
        try
        {
            System.Diagnostics.EventLog.WriteEntry("LanDesk Service", logEntry, 
                System.Diagnostics.EventLogEntryType.Information);
        }
        catch
        {
            // If event log fails, write to file
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
                    "LanDesk", "service.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Ignore if logging fails
            }
        }
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

    public override void Dispose()
    {
        // Unregister from server
        if (_serverDiscoveryService != null)
        {
            Task.Run(async () => await _serverDiscoveryService.UnregisterAsync());
        }
        
        _screenStreaming!.StreamingStopped -= OnStreamingStopped;
        _screenStreaming?.StopStreaming();
        _screenStreaming?.Dispose();
        _screenCaptureHelper?.Dispose();
        _inputReceiver?.StopReceiving();
        _inputReceiver?.Dispose();
        _approvalService?.Dispose();
        _discoveryService?.Dispose();
        _serverDiscoveryService?.Dispose();
        _connectionManager?.Dispose();
        _inputConnectionManager?.Dispose();
        base.Dispose();
    }
}
