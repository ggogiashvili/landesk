using System.Diagnostics;
using LanDesk.Core.Configuration;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Ensures Windows Firewall has allow rules for LanDesk ports (so the service and app can receive connections).
/// Uses netsh advfirewall; typically run at service startup (service runs as SYSTEM and can modify firewall).
/// </summary>
public static class FirewallHelper
{
    private const string RulePrefix = "LanDesk ";

    /// <summary>
    /// Ensures inbound and outbound firewall rules exist for discovery (UDP), screen (TCP), input (TCP), and approval (TCP).
    /// Idempotent: skips adding a rule if it already exists. Logs to Utilities.Logger.
    /// </summary>
    /// <returns>True if all rules are present or were added; false if any add failed (e.g. not running as admin).</returns>
    public static bool EnsureFirewallRules()
    {
        var rules = new[]
        {
            (Name: RulePrefix + "Discovery", Dir: "in", Protocol: "UDP", Port: NetworkConfiguration.DefaultDiscoveryPort),
            (Name: RulePrefix + "Screen", Dir: "in", Protocol: "TCP", Port: NetworkConfiguration.DefaultControlPort),
            (Name: RulePrefix + "Input", Dir: "in", Protocol: "TCP", Port: NetworkConfiguration.DefaultInputPort),
            (Name: RulePrefix + "Approval", Dir: "in", Protocol: "TCP", Port: NetworkConfiguration.ApprovalPort),
            (Name: RulePrefix + "Discovery Out", Dir: "out", Protocol: "UDP", Port: NetworkConfiguration.DefaultDiscoveryPort),
            (Name: RulePrefix + "Screen Out", Dir: "out", Protocol: "TCP", Port: NetworkConfiguration.DefaultControlPort),
            (Name: RulePrefix + "Input Out", Dir: "out", Protocol: "TCP", Port: NetworkConfiguration.DefaultInputPort),
            (Name: RulePrefix + "Approval Out", Dir: "out", Protocol: "TCP", Port: NetworkConfiguration.ApprovalPort),
        };

        bool allOk = true;
        foreach (var r in rules)
        {
            if (RuleExists(r.Name))
            {
                Logger.Debug($"FirewallHelper: Rule '{r.Name}' already exists, skipping.");
                continue;
            }
            if (!AddRule(r.Name, r.Dir, r.Protocol, r.Port))
                allOk = false;
        }

        if (allOk)
            Logger.Info("FirewallHelper: Firewall rules ensured (LanDesk ports allowed).");
        else
            Logger.Warning("FirewallHelper: One or more firewall rules could not be added (run as Administrator or run setup-firewall.ps1).");

        return allOk;
    }

    private static bool RuleExists(string displayName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                ArgumentList = { "advfirewall", "firewall", "show", "rule", $"name={displayName}" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Debug($"FirewallHelper: RuleExists({displayName}) error: {ex.Message}");
            return false;
        }
    }

    private static bool AddRule(string displayName, string direction, string protocol, int localPort)
    {
        try
        {
            // netsh advfirewall firewall add rule name="..." dir=in|out action=allow protocol=TCP|UDP localport=...
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                ArgumentList =
                {
                    "advfirewall", "firewall", "add", "rule",
                    $"name={displayName}",
                    $"dir={direction}",
                    "action=allow",
                    $"protocol={protocol}",
                    $"localport={localPort}",
                    "profile=domain,private,public"
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                Logger.Warning($"FirewallHelper: Failed to start netsh for rule '{displayName}'");
                return false;
            }
            var outStr = p.StandardOutput.ReadToEnd();
            var errStr = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            if (p.ExitCode != 0)
            {
                Logger.Warning($"FirewallHelper: netsh add rule '{displayName}' failed (exit {p.ExitCode}): {errStr?.Trim() ?? outStr?.Trim()}");
                return false;
            }
            Logger.Info($"FirewallHelper: Added firewall rule '{displayName}' ({protocol} {direction} port {localPort}).");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"FirewallHelper: AddRule({displayName}) error: {ex.Message}");
            return false;
        }
    }
}
