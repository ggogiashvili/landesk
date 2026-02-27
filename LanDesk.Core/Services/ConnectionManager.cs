using System.Net;
using System.Net.Sockets;
using System.Linq;
using LanDesk.Core.Models;

namespace LanDesk.Core.Services;

/// <summary>
/// Manages TCP connections to remote devices
/// </summary>
public class ConnectionManager : IDisposable
{
    private readonly Dictionary<string, TcpClient> _activeConnections;
    private TcpListener? _listener;
    private bool _isListening;
    private readonly int _controlPort;

    public event EventHandler<TcpClient>? ConnectionEstablished;
    public event EventHandler<string>? ConnectionClosed;

    public ConnectionManager(int controlPort)
    {
        _controlPort = controlPort;
        _activeConnections = new Dictionary<string, TcpClient>();
    }

    /// <summary>
    /// Starts listening for incoming connections
    /// </summary>
    public void StartListening()
    {
        if (_isListening)
        {
            Utilities.Logger.Warning($"ConnectionManager: Already listening on port {_controlPort}");
            return;
        }

        try
        {
            Utilities.Logger.Info($"ConnectionManager: Starting listener on port {_controlPort}");
            Utilities.Logger.Info($"ConnectionManager: Creating TcpListener on IPAddress.Any:{_controlPort}");
            _listener = new TcpListener(IPAddress.Any, _controlPort);
            Utilities.Logger.Info($"ConnectionManager: TcpListener created, calling Start()...");
            _listener.Start();
            _isListening = true;
            Utilities.Logger.Info($"ConnectionManager: Successfully started listening on port {_controlPort}");
            Utilities.Logger.Info($"ConnectionManager: Local endpoint: {_listener.LocalEndpoint}");
            Utilities.Logger.Info($"ConnectionManager: Pending connections: {_listener.Pending()}");

            Utilities.Logger.Info($"ConnectionManager: Starting AcceptConnections task...");
            Task.Run(AcceptConnections);
            Utilities.Logger.Info($"ConnectionManager: AcceptConnections task started");
        }
        catch (SocketException sex) when (sex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // Port is in use - likely by service, log as DEBUG instead of ERROR
            Utilities.Logger.Debug($"ConnectionManager: Port {_controlPort} is in use (likely by LanDesk Service) - this is expected");
            throw new InvalidOperationException($"Failed to start connection listener: {sex.Message}", sex);
        }
        catch (Exception ex)
        {
            // Only log as ERROR if it's not a port conflict
            var isPortConflict = ex.Message.Contains("already in use") || 
                                ex.Message.Contains("address already") ||
                                ex.Message.Contains("normally permitted");
            
            if (isPortConflict)
            {
                Utilities.Logger.Debug($"ConnectionManager: Port {_controlPort} is in use - this may be expected");
            }
            else
            {
                Utilities.Logger.Error($"ConnectionManager: Failed to start listener on port {_controlPort}", ex);
            }
            throw new InvalidOperationException($"Failed to start connection listener: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stops listening for incoming connections
    /// </summary>
    public void StopListening()
    {
        _isListening = false;
        _listener?.Stop();
        _listener = null;
    }

    /// <summary>
    /// Establishes a connection to a remote device
    /// </summary>
    public async Task<TcpClient?> ConnectToDeviceAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        // Use the device's control port by default
        return await ConnectToDeviceAsync(device.IpAddress, device.ControlPort, device.DeviceName, cancellationToken);
    }

    /// <summary>
    /// Establishes a connection to a remote device at a specific port
    /// </summary>
    public async Task<TcpClient?> ConnectToDeviceAsync(IPAddress ipAddress, int port, string deviceName, CancellationToken cancellationToken = default)
    {
        TcpClient? tcpClient = null;
        try
        {
            Utilities.Logger.Info($"ConnectionManager: Attempting TCP connection to {deviceName} at {ipAddress}:{port} (Manager ControlPort: {_controlPort})");
            
            // Create a timeout token (10 seconds default)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            
            tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            tcpClient.ReceiveBufferSize = 524288; // 512KB
            tcpClient.SendBufferSize = 524288; // 512KB
            
            try
            {
                await tcpClient.ConnectAsync(ipAddress, port, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                Utilities.Logger.Warning($"Connection to {deviceName} at {ipAddress}:{port} timed out after 10 seconds. Possible causes: firewall blocking port {port}, service not running, or network issue.");
                tcpClient?.Dispose();
                return null;
            }

            if (tcpClient.Connected)
            {
                Utilities.Logger.Info($"TCP connection established to {deviceName} at {ipAddress}:{port}");
                var connectionId = $"{deviceName}_{Guid.NewGuid()}";
                _activeConnections[connectionId] = tcpClient;
                return tcpClient;
            }
            else
            {
                Utilities.Logger.Warning($"TCP connection to {deviceName} completed but client is not connected");
                tcpClient?.Dispose();
            }
        }
        catch (SocketException sex)
        {
            var errorMsg = sex.SocketErrorCode switch
            {
                SocketError.TimedOut => $"Connection to {deviceName} timed out. Check firewall rules for port {port}.",
                SocketError.ConnectionRefused => $"Connection to {deviceName} was refused. Service may not be running on port {port}.",
                SocketError.HostUnreachable => $"Host {deviceName} ({ipAddress}) is unreachable. Check network connectivity.",
                SocketError.NetworkUnreachable => $"Network unreachable. Check routing to {ipAddress}.",
                _ => $"Socket error connecting to {deviceName}: {sex.SocketErrorCode}"
            };
            Utilities.Logger.Error($"{errorMsg} (Port: {port})", sex);
            tcpClient?.Dispose();
        }
        catch (Exception ex)
        {
            Utilities.Logger.Error($"Failed to connect to {deviceName} at {ipAddress}:{port}", ex);
            tcpClient?.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Establishes a connection to a remote device using IP address or domain name
    /// Supports both IP addresses (e.g., 192.168.1.100) and domain names (e.g., computer.example.com)
    /// </summary>
    public async Task<TcpClient?> ConnectToDeviceAsync(string hostnameOrIp, int port, string deviceName = "", CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                deviceName = hostnameOrIp;

            Utilities.Logger.Info($"Attempting TCP connection to {deviceName} at {hostnameOrIp}:{port}");
            
            // Resolve hostname to IP address (works for both IP addresses and domain names)
            IPAddress? ipAddress = null;
            try
            {
                // Try parsing as IP address first
                if (IPAddress.TryParse(hostnameOrIp, out var parsedIp))
                {
                    ipAddress = parsedIp;
                    Utilities.Logger.Debug($"Parsed as IP address: {ipAddress}");
                }
                else
                {
                    // Resolve domain name to IP address
                    Utilities.Logger.Info($"Resolving domain name: {hostnameOrIp}");
                    var hostEntry = await Dns.GetHostEntryAsync(hostnameOrIp);
                    
                    // Prefer IPv4 addresses
                    ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    
                    // Fallback to any address if no IPv4 found
                    if (ipAddress == null && hostEntry.AddressList.Length > 0)
                    {
                        ipAddress = hostEntry.AddressList[0];
                    }
                    
                    if (ipAddress == null)
                    {
                        Utilities.Logger.Error($"Could not resolve {hostnameOrIp} to an IP address");
                        return null;
                    }
                    
                    Utilities.Logger.Info($"Resolved {hostnameOrIp} to {ipAddress}");
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.Error($"Failed to resolve hostname/IP {hostnameOrIp}: {ex.Message}", ex);
                return null;
            }

            // Connect using resolved IP address
            return await ConnectToDeviceAsync(ipAddress, port, deviceName, cancellationToken);
        }
        catch (Exception ex)
        {
            Utilities.Logger.Error($"Failed to connect to {hostnameOrIp}:{port}", ex);
            return null;
        }
    }

    /// <summary>
    /// Closes a connection
    /// </summary>
    public void CloseConnection(string connectionId)
    {
        if (_activeConnections.TryGetValue(connectionId, out var client))
        {
            try
            {
                client.Close();
            }
            catch { }

            _activeConnections.Remove(connectionId);
            ConnectionClosed?.Invoke(this, connectionId);
        }
    }

    /// <summary>
    /// Gets all active connections
    /// </summary>
    public IEnumerable<KeyValuePair<string, TcpClient>> GetActiveConnections()
    {
        return _activeConnections.ToList();
    }

    private async Task AcceptConnections()
    {
        Utilities.Logger.Info($"ConnectionManager: Started accepting connections on port {_controlPort}");
        Utilities.Logger.Info($"ConnectionManager: Listener active: {_listener != null}, IsListening: {_isListening}");
        
        while (_isListening && _listener != null)
        {
            try
            {
                Utilities.Logger.Info($"ConnectionManager: Waiting for connection on port {_controlPort}... (Listener: {_listener != null}, IsListening: {_isListening})");
                var tcpClient = await _listener!.AcceptTcpClientAsync();
                if (tcpClient == null) continue;
                Utilities.Logger.Info($"ConnectionManager: AcceptTcpClientAsync returned! Client: {tcpClient != null}");
                tcpClient.NoDelay = true;
                tcpClient.ReceiveBufferSize = 524288; // 512KB
                tcpClient.SendBufferSize = 524288; // 512KB
                Utilities.Logger.Info($"ConnectionManager: *** ACCEPTED connection on port {_controlPort} from {tcpClient.Client?.RemoteEndPoint} ***");
                Utilities.Logger.Info($"ConnectionManager: Client connected: {tcpClient.Connected}");
                Utilities.Logger.Info($"ConnectionManager: Invoking ConnectionEstablished event...");
                
                // Check if there are any subscribers to the event
                if (ConnectionEstablished == null)
                {
                    Utilities.Logger.Warning($"ConnectionManager: WARNING - No subscribers to ConnectionEstablished event! Connection will be ignored.");
                }
                else
                {
                    Utilities.Logger.Info($"ConnectionManager: ConnectionEstablished event has {ConnectionEstablished.GetInvocationList().Length} subscriber(s)");
                }
                
                ConnectionEstablished?.Invoke(this, tcpClient);
                Utilities.Logger.Info($"ConnectionManager: ConnectionEstablished event invoked");
            }
            catch (ObjectDisposedException)
            {
                // Expected when stopping - listener was disposed
                Utilities.Logger.Info($"ConnectionManager: Listener disposed, stopping accept loop on port {_controlPort}");
                break;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // Socket errors during shutdown are expected
                if (!_isListening)
                {
                    // We're stopping, this is expected
                    Utilities.Logger.Info($"ConnectionManager: Socket error during shutdown on port {_controlPort}: {ex.Message}");
                    break;
                }
                else
                {
                    // Unexpected socket error while listening
                    Utilities.Logger.Warning($"ConnectionManager: Socket error accepting connection on port {_controlPort}: {ex.SocketErrorCode} - {ex.Message}");
                    // Continue listening - might be a temporary issue
                    await Task.Delay(1000); // Wait a bit before retrying
                }
            }
            catch (Exception ex)
            {
                // Other unexpected errors
                if (_isListening)
                {
                    Utilities.Logger.Warning($"ConnectionManager: Error accepting connection on port {_controlPort}: {ex.Message}");
                    // Continue listening - might be a temporary issue
                    await Task.Delay(1000); // Wait a bit before retrying
                }
                else
                {
                    // We're stopping, this is expected
                    Utilities.Logger.Debug($"ConnectionManager: Error during shutdown on port {_controlPort}: {ex.Message}");
                    break;
                }
            }
        }
        Utilities.Logger.Info($"ConnectionManager: Stopped accepting connections on port {_controlPort}");
    }

    public void Dispose()
    {
        StopListening();
        
        foreach (var connection in _activeConnections.Values)
        {
            try
            {
                connection.Close();
            }
            catch { }
        }
        
        _activeConnections.Clear();
    }
}
