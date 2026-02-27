using System.Security.Cryptography;
using System.Text;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Generates unique device IDs based on machine characteristics
/// </summary>
public static class DeviceIdGenerator
{
    /// <summary>
    /// Generates a unique device ID based on machine name and MAC address
    /// </summary>
    public static string GenerateDeviceId()
    {
        var machineName = Environment.MachineName;
        var macAddress = GetMacAddress();
        
        var combined = $"{machineName}_{macAddress}";
        var bytes = Encoding.UTF8.GetBytes(combined);
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        
        return Convert.ToBase64String(hash).Substring(0, 16).Replace("/", "_").Replace("+", "-");
    }

    private static string GetMacAddress()
    {
        try
        {
            var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                             ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .FirstOrDefault();

            if (networkInterfaces != null)
            {
                var physicalAddress = networkInterfaces.GetPhysicalAddress();
                return string.Join("", physicalAddress.GetAddressBytes().Select(b => b.ToString("X2")));
            }
        }
        catch
        {
            // Fallback
        }

        return Guid.NewGuid().ToString("N").Substring(0, 12);
    }
}
