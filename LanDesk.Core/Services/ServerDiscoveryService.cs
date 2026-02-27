using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanDesk.Core.Configuration;
using LanDesk.Core.Models;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for discovering devices through a central discovery server
/// </summary>
public class ServerDiscoveryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly int _controlPort;
    private readonly string _pairingCode;
    private Timer? _heartbeatTimer;
    private bool _isRegistered;
    private readonly object _registrationLock = new object();

    public event EventHandler<DiscoveredDevice>? DeviceDiscovered;
#pragma warning disable CS0067 // Event never used - kept for API consistency
    public event EventHandler<DiscoveredDevice>? DeviceUpdated;
#pragma warning restore CS0067

    public ServerDiscoveryService(string serverIp, int serverPort, string deviceId, string deviceName, int controlPort, string pairingCode)
    {
        _serverUrl = $"http://{serverIp}:{serverPort}";
        _deviceId = deviceId;
        _deviceName = deviceName;
        _controlPort = controlPort;
        _pairingCode = pairingCode;
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// Registers this device with the discovery server
    /// </summary>
    public async Task<bool> RegisterAsync(string localIpAddress)
    {
        try
        {
            var registrationData = new
            {
                device_id = _deviceId,
                device_name = _deviceName,
                ip_address = localIpAddress,
                pairing_code = _pairingCode,
                control_port = _controlPort,
                input_port = NetworkConfiguration.DefaultInputPort,
                discovery_port = NetworkConfiguration.DefaultDiscoveryPort,
                version = "1.0.0",
                operating_system = Environment.OSVersion.ToString()
            };

            var json = JsonSerializer.Serialize(registrationData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_serverUrl}/api/register", content);
            
            if (response.IsSuccessStatusCode)
            {
                lock (_registrationLock)
                {
                    _isRegistered = true;
                }
                
                // Start heartbeat timer (IP will be detected dynamically in SendHeartbeat)
                _heartbeatTimer = new Timer(SendHeartbeat, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                
                Logger.Info($"ServerDiscoveryService: Successfully registered with server at {_serverUrl}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.Warning($"ServerDiscoveryService: Failed to register with server: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"ServerDiscoveryService: Error registering with server: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unregisters this device from the discovery server
    /// </summary>
    public async Task UnregisterAsync()
    {
        try
        {
            lock (_registrationLock)
            {
                _isRegistered = false;
            }

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            var unregisterData = new
            {
                device_id = _deviceId
            };

            var json = JsonSerializer.Serialize(unregisterData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync($"{_serverUrl}/api/unregister", content);
            Logger.Info("ServerDiscoveryService: Unregistered from server");
        }
        catch (Exception ex)
        {
            Logger.Debug($"ServerDiscoveryService: Error unregistering from server: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds a device by pairing code through the server
    /// </summary>
    public async Task<DiscoveredDevice?> FindDeviceByCodeAsync(string pairingCode)
    {
        try
        {
            var normalizedCode = pairingCode.Replace("-", "").Replace(" ", "").ToUpper();
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/find/{normalizedCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (result.TryGetProperty("device", out var deviceElement))
                {
                    var device = new DiscoveredDevice
                    {
                        DeviceId = deviceElement.GetProperty("device_id").GetString() ?? "",
                        DeviceName = deviceElement.GetProperty("device_name").GetString() ?? "",
                        IpAddress = IPAddress.Parse(deviceElement.GetProperty("ip_address").GetString() ?? ""),
                        ControlPort = deviceElement.TryGetProperty("control_port", out var port) ? port.GetInt32() : NetworkConfiguration.DefaultControlPort,
                        DiscoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
                        OperatingSystem = deviceElement.TryGetProperty("operating_system", out var os) ? os.GetString() ?? "" : "",
                        LastSeen = DateTime.Now,
                        IsOnline = true,
                        Version = deviceElement.TryGetProperty("version", out var ver) ? ver.GetString() ?? "1.0.0" : "1.0.0",
                        PairingCode = deviceElement.GetProperty("pairing_code").GetString() ?? ""
                    };

                    Logger.Info($"ServerDiscoveryService: Found device {device.DeviceName} at {device.IpAddress} via server");
                    return device;
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.Debug($"ServerDiscoveryService: Device with pairing code {pairingCode} not found on server");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"ServerDiscoveryService: Error finding device by code: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Discovers all devices registered with the server
    /// </summary>
    public async Task<List<DiscoveredDevice>> DiscoverDevicesAsync()
    {
        var devices = new List<DiscoveredDevice>();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/discover?exclude_device_id={_deviceId}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (result.TryGetProperty("devices", out var devicesArray))
                {
                    foreach (var deviceElement in devicesArray.EnumerateArray())
                    {
                        try
                        {
                            var device = new DiscoveredDevice
                            {
                                DeviceId = deviceElement.GetProperty("device_id").GetString() ?? "",
                                DeviceName = deviceElement.GetProperty("device_name").GetString() ?? "",
                                IpAddress = IPAddress.Parse(deviceElement.GetProperty("ip_address").GetString() ?? ""),
                                ControlPort = deviceElement.TryGetProperty("control_port", out var port) ? port.GetInt32() : NetworkConfiguration.DefaultControlPort,
                                DiscoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
                                OperatingSystem = deviceElement.TryGetProperty("operating_system", out var os) ? os.GetString() ?? "" : "",
                                LastSeen = DateTime.Now,
                                IsOnline = true,
                                Version = deviceElement.TryGetProperty("version", out var ver) ? ver.GetString() ?? "1.0.0" : "1.0.0",
                                PairingCode = deviceElement.GetProperty("pairing_code").GetString() ?? ""
                            };

                            devices.Add(device);
                            DeviceDiscovered?.Invoke(this, device);
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"ServerDiscoveryService: Error parsing device: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"ServerDiscoveryService: Error discovering devices: {ex.Message}");
        }

        return devices;
    }

    private async void SendHeartbeat(object? state)
    {
        // Get current local IP address (may have changed)
        var currentIp = GetCurrentLocalIPAddress();
        if (string.IsNullOrEmpty(currentIp))
            return;

        lock (_registrationLock)
        {
            if (!_isRegistered)
                return;
        }

        try
        {
            var heartbeatData = new
            {
                device_id = _deviceId,
                ip_address = currentIp
            };

            var json = JsonSerializer.Serialize(heartbeatData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync($"{_serverUrl}/api/heartbeat", content);
        }
        catch (Exception ex)
        {
            Logger.Debug($"ServerDiscoveryService: Error sending heartbeat: {ex.Message}");
        }
    }

    private static string GetCurrentLocalIPAddress()
    {
        try
        {
            foreach (var networkInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;

                var properties = networkInterface.GetIPProperties();
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch { }

        return string.Empty;
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _httpClient?.Dispose();
    }
}
