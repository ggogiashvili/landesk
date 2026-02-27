using System;
using System.IO;
using Microsoft.Win32;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Handles persistent storage of device information (pairing code, device ID)
/// </summary>
public static class PersistentStorage
{
    private const string REGISTRY_KEY = @"SOFTWARE\LanDesk";
    private const string PAIRING_CODE_VALUE = "PairingCode";
    private const string DEVICE_ID_VALUE = "DeviceId";
    
    // Fallback file location if registry fails - use CommonApplicationData for access by all users/service
    private static string ConfigFilePath => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
            "LanDesk", "config.json");

    /// <summary>
    /// Gets or creates a persistent pairing code
    /// </summary>
    public static string GetOrCreatePairingCode()
    {
        // Try registry first (HKLM for system-wide access)
        try
        {
            // Use HKLM to share between Service (SYSTEM) and App (User)
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(REGISTRY_KEY, false); // Read-only access is enough for checking
            if (key != null)
            {
                var existingCode = key.GetValue(PAIRING_CODE_VALUE) as string;
                if (!string.IsNullOrEmpty(existingCode) && PairingCodeGenerator.IsValidPairingCode(existingCode))
                {
                    Logger.Debug($"Loaded persistent pairing code from registry: {PairingCodeGenerator.FormatPairingCode(existingCode)}");
                    return existingCode;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Registry access failed, trying file storage: {ex.Message}");
        }

        // Try file storage
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json);
                if (config != null && !string.IsNullOrEmpty(config.PairingCode) && 
                    PairingCodeGenerator.IsValidPairingCode(config.PairingCode))
                {
                    Logger.Debug($"Loaded persistent pairing code from file: {PairingCodeGenerator.FormatPairingCode(config.PairingCode)}");
                    return config.PairingCode;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"File storage read failed: {ex.Message}");
        }

        // Generate new code and save it
        var newCode = PairingCodeGenerator.GeneratePairingCode();
        SavePairingCode(newCode);
        Logger.Info($"Generated new persistent pairing code: {PairingCodeGenerator.FormatPairingCode(newCode)}");
        return newCode;
    }

    /// <summary>
    /// Saves a pairing code persistently
    /// </summary>
    public static void SavePairingCode(string pairingCode)
    {
        if (!PairingCodeGenerator.IsValidPairingCode(pairingCode))
            return;

        // Try registry first
        try
        {
            // Need writable access to HKLM
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(REGISTRY_KEY, true);
            if (key != null)
            {
                key.SetValue(PAIRING_CODE_VALUE, pairingCode);
                Logger.Debug($"Saved pairing code to registry");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Registry save failed, trying file storage: {ex.Message}");
        }

        // Try file storage
        try
        {
            var configDir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var config = new ConfigData { PairingCode = pairingCode };
            var json = System.Text.Json.JsonSerializer.Serialize(config);
            File.WriteAllText(ConfigFilePath, json);
            Logger.Debug($"Saved pairing code to file: {ConfigFilePath}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save pairing code: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets or creates a persistent device ID
    /// </summary>
    public static string GetOrCreateDeviceId()
    {
        // Try registry first
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(REGISTRY_KEY, false);
            if (key != null)
            {
                var existingId = key.GetValue(DEVICE_ID_VALUE) as string;
                if (!string.IsNullOrEmpty(existingId))
                {
                    Logger.Debug($"Loaded persistent device ID from registry: {existingId}");
                    return existingId;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Registry access failed, trying file storage: {ex.Message}");
        }

        // Try file storage
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json);
                if (config != null && !string.IsNullOrEmpty(config.DeviceId))
                {
                    Logger.Debug($"Loaded persistent device ID from file: {config.DeviceId}");
                    return config.DeviceId;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"File storage read failed: {ex.Message}");
        }

        // Generate new ID and save it
        var newId = DeviceIdGenerator.GenerateDeviceId();
        SaveDeviceId(newId);
        Logger.Info($"Generated new persistent device ID: {newId}");
        return newId;
    }

    /// <summary>
    /// Saves a device ID persistently
    /// </summary>
    public static void SaveDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        // Try registry first
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(REGISTRY_KEY, true);
            if (key != null)
            {
                key.SetValue(DEVICE_ID_VALUE, deviceId);
                Logger.Debug($"Saved device ID to registry");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Registry save failed, trying file storage: {ex.Message}");
        }

        // Try file storage
        try
        {
            var configDir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            ConfigData config;
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                config = System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json) ?? new ConfigData();
            }
            else
            {
                config = new ConfigData();
            }

            config.DeviceId = deviceId;
            var newJson = System.Text.Json.JsonSerializer.Serialize(config);
            File.WriteAllText(ConfigFilePath, newJson);
            Logger.Debug($"Saved device ID to file: {ConfigFilePath}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save device ID: {ex.Message}");
        }
    }

    private class ConfigData
    {
        public string? PairingCode { get; set; }
        public string? DeviceId { get; set; }
    }
}
