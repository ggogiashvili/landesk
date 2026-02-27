using System;

namespace LanDesk.Core.Configuration;

/// <summary>
/// Network configuration constants.
/// Ports chosen from SCCM/allowed firewall list: TCP 8530, 8531, 10123; UDP 25536.
/// </summary>
public static class NetworkConfiguration
{
    /// <summary>
    /// Default discovery port (UDP) - Used for server discovery. Uses allowed UDP port 25536.
    /// </summary>
    public const int DefaultDiscoveryPort = 25536;

    /// <summary>
    /// Default control port (TCP) - for screen streaming. Uses allowed TCP port 8530.
    /// </summary>
    public const int DefaultControlPort = 8530;

    /// <summary>
    /// Input control port (TCP) - for remote control input. Uses allowed TCP port 8531.
    /// </summary>
    public const int DefaultInputPort = 8531;

    /// <summary>
    /// Approval channel port (TCP, localhost only) - GUI connects here to approve/reject incoming connections. Uses allowed TCP port 10123.
    /// </summary>
    public const int ApprovalPort = 10123;

    /// <summary>
    /// Named pipe name for screen capture helper (service receives frames from GUI via this pipe)
    /// </summary>
    public const string ScreenCapturePipeName = "LanDeskScreenCapture";

    /// <summary>
    /// Discovery broadcast interval in milliseconds
    /// </summary>
    public const int DiscoveryIntervalMs = 5000;

    /// <summary>
    /// Device timeout in seconds (mark as offline after this time)
    /// </summary>
    public const int DeviceTimeoutSeconds = 30;

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public const int ConnectionTimeoutSeconds = 10;

    /// <summary>
    /// Maximum concurrent connections
    /// </summary>
    public const int MaxConcurrentConnections = 10;

    /// <summary>
    /// Discovery server IP address
    /// Can be set via environment variable LANDESK_SERVER_IP, or defaults to null (server discovery disabled)
    /// </summary>
    public static string DiscoveryServerIp => 
        Environment.GetEnvironmentVariable("LANDESK_SERVER_IP") ?? "";

    /// <summary>
    /// Discovery server port (HTTP API)
    /// Can be set via environment variable LANDESK_SERVER_PORT, or defaults to 10123 (SCCM-allowed, distinct from control 8530)
    /// </summary>
    public static int DiscoveryServerPort
    {
        get
        {
            var portStr = Environment.GetEnvironmentVariable("LANDESK_SERVER_PORT");
            if (int.TryParse(portStr, out var port))
                return port;
            return 10123; // Default (SCCM-allowed TCP)
        }
    }

    /// <summary>
    /// Whether server discovery is enabled (server IP is configured)
    /// </summary>
    public static bool IsServerDiscoveryEnabled => !string.IsNullOrWhiteSpace(DiscoveryServerIp);
}
