using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LanDesk.Core.Models;
using LanDesk.Core.Protocol;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Service responsible for discovering devices on the local network using UDP broadcast
/// </summary>
public class DiscoveryService : IDisposable
{
    private UdpClient? _udpClient;
    private UdpClient? _listenerClient;
    private bool _isDiscovering;
    private bool _isListening;
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _discoveredDevices;
    private readonly Timer? _cleanupTimer;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly int _controlPort;
    private readonly string _pairingCode;

    public event EventHandler<DiscoveredDevice>? DeviceDiscovered;
    public event EventHandler<DiscoveredDevice>? DeviceUpdated;
    public event EventHandler<string>? DeviceOffline;

    public DiscoveryService(string deviceId, string deviceName, int controlPort, string pairingCode)
    {
        _deviceId = deviceId;
        _deviceName = deviceName;
        _controlPort = controlPort;
        _pairingCode = pairingCode;
        _discoveredDevices = new ConcurrentDictionary<string, DiscoveredDevice>();
        
        // Cleanup timer to mark devices as offline after 30 seconds
        _cleanupTimer = new Timer(CleanupOfflineDevices, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Starts listening for discovery requests and responding
    /// </summary>
    public void StartListening()
    {
        if (_isListening)
            return;

        try
        {
            _listenerClient = new UdpClient(DiscoveryProtocol.DISCOVERY_PORT);
            _listenerClient.EnableBroadcast = true;
            _isListening = true;

            Task.Run(ListenForDiscoveryRequests);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start discovery listener: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stops listening for discovery requests
    /// </summary>
    public void StopListening()
    {
        _isListening = false;
        _listenerClient?.Close();
        _listenerClient?.Dispose();
        _listenerClient = null;
    }

    /// <summary>
    /// Starts discovering devices on the network
    /// </summary>
    public void StartDiscovery()
    {
        if (_isDiscovering)
            return;

        _isDiscovering = true;
        Task.Run(DiscoverDevices);
    }

    /// <summary>
    /// Stops discovering devices
    /// </summary>
    public void StopDiscovery()
    {
        _isDiscovering = false;
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
    }

    /// <summary>
    /// Gets all currently discovered devices
    /// </summary>
    public IEnumerable<DiscoveredDevice> GetDiscoveredDevices()
    {
        return _discoveredDevices.Values.Where(d => d.IsOnline).ToList();
    }

    /// <summary>
    /// Gets a specific device by ID
    /// </summary>
    public DiscoveredDevice? GetDevice(string deviceId)
    {
        _discoveredDevices.TryGetValue(deviceId, out var device);
        return device;
    }

    /// <summary>
    /// Gets a device by pairing code
    /// </summary>
    public DiscoveredDevice? GetDeviceByPairingCode(string pairingCode)
    {
        var normalizedCode = PairingCodeGenerator.NormalizePairingCode(pairingCode);
        return _discoveredDevices.Values.FirstOrDefault(d => 
            PairingCodeGenerator.NormalizePairingCode(d.PairingCode) == normalizedCode && d.IsOnline);
    }

    private async Task ListenForDiscoveryRequests()
    {
        while (_isListening && _listenerClient != null)
        {
            try
            {
                var result = await _listenerClient.ReceiveAsync();
                var request = DiscoveryProtocol.ParseDiscoveryRequest(result.Buffer);

                if (request != null)
                {
                    // Don't respond to our own requests
                    if (result.RemoteEndPoint.Address.Equals(GetLocalIPAddress()))
                        continue;

                    // Create response with our device information
                    var device = new DiscoveredDevice
                    {
                        DeviceId = _deviceId,
                        DeviceName = _deviceName,
                        IpAddress = GetLocalIPAddress(),
                        ControlPort = _controlPort,
                        DiscoveryPort = DiscoveryProtocol.DISCOVERY_PORT,
                        OperatingSystem = Environment.OSVersion.ToString(),
                        LastSeen = DateTime.Now,
                        IsOnline = true,
                        Version = "1.0.0",
                        PairingCode = _pairingCode
                    };

                    var response = DiscoveryProtocol.CreateDiscoveryResponse(device);
                    await _listenerClient.SendAsync(response, response.Length, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected when closing
                break;
            }
            catch (Exception ex)
            {
                // Log error but continue listening
                System.Diagnostics.Debug.WriteLine($"Error in discovery listener: {ex.Message}");
            }
        }
    }

    private async Task DiscoverDevices()
    {
        while (_isDiscovering)
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.EnableBroadcast = true;
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                var request = DiscoveryProtocol.CreateDiscoveryRequest();
                var sentAddresses = new HashSet<string>();

                // Send discovery request to global broadcast address
                var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryProtocol.DISCOVERY_PORT);
                await _udpClient.SendAsync(request, request.Length, broadcastEndPoint);
                sentAddresses.Add(broadcastEndPoint.Address.ToString());

                // Get all network interfaces and send to each subnet
                var networkSubnets = new HashSet<string>();
                
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var properties = networkInterface.GetIPProperties();
                    foreach (var unicastAddress in properties.UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        var subnetMask = unicastAddress.IPv4Mask;
                        var ipAddress = unicastAddress.Address;
                        var broadcastAddress = GetBroadcastAddress(ipAddress, subnetMask);
                        var broadcastKey = broadcastAddress.ToString();
                        
                        if (!sentAddresses.Contains(broadcastKey))
                        {
                            var subnetEndPoint = new IPEndPoint(broadcastAddress, DiscoveryProtocol.DISCOVERY_PORT);
                            await _udpClient.SendAsync(request, request.Length, subnetEndPoint);
                            sentAddresses.Add(broadcastKey);
                            networkSubnets.Add(GetNetworkPrefix(ipAddress, subnetMask));
                        }
                    }
                }

                // Send to additional subnets in the same network range
                // This allows discovery across different subnets (e.g., 10.246.80.x and 10.246.84.x)
                foreach (var networkPrefix in networkSubnets)
                {
                    var prefixParts = networkPrefix.Split('.');
                    if (prefixParts.Length >= 3)
                    {
                        // We're on a specific subnet like 10.246.80.x
                        var baseNetwork = $"{prefixParts[0]}.{prefixParts[1]}";
                        var currentSubnet = int.Parse(prefixParts[2]);
                        
                        // Try nearby subnets (current ± 10) and common subnet ranges
                        var subnetsToTry = new HashSet<int>();
                        
                        // Add nearby subnets
                        for (int offset = -10; offset <= 10; offset++)
                        {
                            var subnet = currentSubnet + offset;
                            if (subnet >= 0 && subnet <= 255)
                                subnetsToTry.Add(subnet);
                        }
                        
                        // Add common subnet ranges (0, 1, 10, 20, 50, 100, 200, 254, 255)
                        subnetsToTry.Add(0);
                        subnetsToTry.Add(1);
                        subnetsToTry.Add(10);
                        subnetsToTry.Add(20);
                        subnetsToTry.Add(50);
                        subnetsToTry.Add(100);
                        subnetsToTry.Add(200);
                        subnetsToTry.Add(254);
                        subnetsToTry.Add(255);
                        
                        foreach (var thirdOctet in subnetsToTry)
                        {
                            var testSubnet = IPAddress.Parse($"{baseNetwork}.{thirdOctet}.255");
                            var testKey = testSubnet.ToString();
                            
                            if (!sentAddresses.Contains(testKey))
                            {
                                try
                                {
                                    var testEndPoint = new IPEndPoint(testSubnet, DiscoveryProtocol.DISCOVERY_PORT);
                                    await _udpClient.SendAsync(request, request.Length, testEndPoint);
                                    sentAddresses.Add(testKey);
                                }
                                catch
                                {
                                    // Skip invalid addresses
                                }
                            }
                        }
                    }
                    else if (prefixParts.Length >= 2)
                    {
                        // We're on a /16 network, try common subnets
                        var baseNetwork = $"{prefixParts[0]}.{prefixParts[1]}";
                        var commonSubnets = new[] { 0, 1, 10, 20, 50, 80, 84, 100, 200, 254, 255 };
                        
                        foreach (var thirdOctet in commonSubnets)
                        {
                            var testSubnet = IPAddress.Parse($"{baseNetwork}.{thirdOctet}.255");
                            var testKey = testSubnet.ToString();
                            
                            if (!sentAddresses.Contains(testKey))
                            {
                                try
                                {
                                    var testEndPoint = new IPEndPoint(testSubnet, DiscoveryProtocol.DISCOVERY_PORT);
                                    await _udpClient.SendAsync(request, request.Length, testEndPoint);
                                    sentAddresses.Add(testKey);
                                }
                                catch
                                {
                                    // Skip invalid addresses
                                }
                            }
                        }
                    }
                }

                Utilities.Logger.Debug($"Discovery: Sent broadcast to {sentAddresses.Count} addresses/subnets");

                // Listen for responses with longer timeout to catch responses from different subnets
                var timeout = DateTime.Now.AddSeconds(5);
                while (DateTime.Now < timeout && _isDiscovering)
                {
                    try
                    {
                        if (_udpClient != null && _udpClient.Available > 0)
                        {
                            var result = await _udpClient.ReceiveAsync();
                            var response = DiscoveryProtocol.ParseDiscoveryResponse(result.Buffer);

                            if (response != null)
                            {
                                ProcessDiscoveryResponse(response, result.RemoteEndPoint.Address);
                            }
                        }
                        else
                        {
                            await Task.Delay(100);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Socket was disposed, break out of loop
                        break;
                    }
                    catch (SocketException ex)
                    {
                        // Socket errors are common during discovery, just continue
                        Utilities.Logger.Debug($"Discovery: Socket error during receive: {ex.SocketErrorCode} - {ex.Message}");
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        // Other errors during receive, log and continue
                        Utilities.Logger.Debug($"Discovery: Error during receive: {ex.Message}");
                        await Task.Delay(100);
                    }
                }

                try
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
                _udpClient = null;

                // Wait before next discovery cycle
                await Task.Delay(5000);
            }
            catch (ObjectDisposedException)
            {
                // Expected when stopping
                break;
            }
            catch (SocketException ex)
            {
                // Socket errors are common during UDP operations, especially with keep-alive
                // These are usually harmless and don't prevent discovery from working
                Utilities.Logger.Debug($"Discovery: Socket error (keep-alive/network): {ex.SocketErrorCode} - {ex.Message}");
                try
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                }
                catch { }
                _udpClient = null;
                await Task.Delay(5000);
            }
            catch (Exception ex)
            {
                // Other networking errors during broadcast, log as debug to avoid spamming
                Utilities.Logger.Debug($"Discovery: Error during discovery cycle: {ex.Message}");
                try
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                }
                catch { }
                _udpClient = null;
                await Task.Delay(5000);
            }
        }
    }

    private static string GetNetworkPrefix(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var subnetBytes = subnetMask.GetAddressBytes();
        var networkBytes = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            networkBytes[i] = (byte)(addressBytes[i] & subnetBytes[i]);
        }

        return new IPAddress(networkBytes).ToString();
    }

    private void ProcessDiscoveryResponse(DiscoveryResponse response, IPAddress ipAddress)
    {
        // Don't add ourselves
        if (response.DeviceId == _deviceId)
            return;

        var device = new DiscoveredDevice
        {
            DeviceId = response.DeviceId,
            DeviceName = response.DeviceName,
            IpAddress = ipAddress,
            ControlPort = response.ControlPort,
            DiscoveryPort = DiscoveryProtocol.DISCOVERY_PORT,
            OperatingSystem = response.OperatingSystem,
            LastSeen = DateTime.Now,
            IsOnline = true,
            Version = response.VersionString,
            PairingCode = response.PairingCode
        };

        var isNew = _discoveredDevices.TryAdd(device.DeviceId, device);
        
        if (isNew)
        {
            DeviceDiscovered?.Invoke(this, device);
        }
        else
        {
            var existing = _discoveredDevices[device.DeviceId];
            existing.LastSeen = DateTime.Now;
            existing.IsOnline = true;
            existing.IpAddress = ipAddress; // Update IP in case it changed
            DeviceUpdated?.Invoke(this, existing);
        }
    }

    private void CleanupOfflineDevices(object? state)
    {
        var now = DateTime.Now;
        var offlineDevices = new List<string>();

        foreach (var device in _discoveredDevices.Values)
        {
            if (device.IsOnline && (now - device.LastSeen).TotalSeconds > 30)
            {
                device.IsOnline = false;
                offlineDevices.Add(device.DeviceId);
            }
        }

        foreach (var deviceId in offlineDevices)
        {
            DeviceOffline?.Invoke(this, deviceId);
        }
    }

    private static IPAddress GetLocalIPAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            var properties = networkInterface.GetIPProperties();
            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                {
                    return address.Address;
                }
            }
        }

        return IPAddress.Loopback;
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var subnetBytes = subnetMask.GetAddressBytes();
        var broadcastBytes = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            broadcastBytes[i] = (byte)(addressBytes[i] | ~subnetBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }

    public void Dispose()
    {
        StopDiscovery();
        StopListening();
        _cleanupTimer?.Dispose();
    }
}
