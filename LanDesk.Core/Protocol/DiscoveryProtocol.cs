using System.Net;
using System.Text;
using System.Text.Json;
using LanDesk.Core.Models;

namespace LanDesk.Core.Protocol;

/// <summary>
/// Defines the discovery protocol messages for LAN device discovery
/// </summary>
public static class DiscoveryProtocol
{
    // Protocol constants (SCCM/allowed firewall ports)
    public const int DISCOVERY_PORT = 25536; // UDP port for discovery
    public const int CONTROL_PORT = 8530;    // TCP port for screen streaming
    public const string DISCOVERY_MAGIC = "LANDESK_DISCOVERY";
    public const string RESPONSE_MAGIC = "LANDESK_RESPONSE";
    public const int PROTOCOL_VERSION = 1;

    /// <summary>
    /// Creates a discovery request message
    /// </summary>
    public static byte[] CreateDiscoveryRequest()
    {
        var request = new DiscoveryRequest
        {
            Magic = DISCOVERY_MAGIC,
            Version = PROTOCOL_VERSION,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(request);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Parses a discovery request message
    /// </summary>
    public static DiscoveryRequest? ParseDiscoveryRequest(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var request = JsonSerializer.Deserialize<DiscoveryRequest>(json);
            
            if (request?.Magic == DISCOVERY_MAGIC)
            {
                return request;
            }
        }
        catch
        {
            // Invalid message
        }
        
        return null;
    }

    /// <summary>
    /// Creates a discovery response message
    /// </summary>
    public static byte[] CreateDiscoveryResponse(DiscoveredDevice device)
    {
        var response = new DiscoveryResponse
        {
            Magic = RESPONSE_MAGIC,
            Version = PROTOCOL_VERSION,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            ControlPort = device.ControlPort,
            OperatingSystem = device.OperatingSystem,
            Timestamp = DateTime.UtcNow,
            VersionString = device.Version,
            PairingCode = device.PairingCode
        };

        var json = JsonSerializer.Serialize(response);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Parses a discovery response message
    /// </summary>
    public static DiscoveryResponse? ParseDiscoveryResponse(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var response = JsonSerializer.Deserialize<DiscoveryResponse>(json);
            
            if (response?.Magic == RESPONSE_MAGIC)
            {
                return response;
            }
        }
        catch
        {
            // Invalid message
        }
        
        return null;
    }
}

/// <summary>
/// Discovery request message structure
/// </summary>
public class DiscoveryRequest
{
    public string Magic { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Discovery response message structure
/// </summary>
public class DiscoveryResponse
{
    public string Magic { get; set; } = string.Empty;
    public int Version { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int ControlPort { get; set; }
    public string OperatingSystem { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string VersionString { get; set; } = "1.0.0";
    public string PairingCode { get; set; } = string.Empty;
}
