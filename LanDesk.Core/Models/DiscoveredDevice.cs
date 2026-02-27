using System.Net;

namespace LanDesk.Core.Models;

/// <summary>
/// Represents a device discovered on the local network
/// </summary>
public class DiscoveredDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public IPAddress IpAddress { get; set; } = IPAddress.None;
    public int ControlPort { get; set; }
    public int DiscoveryPort { get; set; }
    public string OperatingSystem { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public bool IsOnline { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string PairingCode { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{DeviceName} ({IpAddress})";
    }

    public override bool Equals(object? obj)
    {
        if (obj is DiscoveredDevice other)
        {
            return DeviceId == other.DeviceId && IpAddress.Equals(other.IpAddress);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DeviceId, IpAddress);
    }
}
