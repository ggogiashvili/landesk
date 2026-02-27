using System.Security.Cryptography;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Generates numeric pairing codes similar to AnyDesk (9-digit codes)
/// </summary>
public static class PairingCodeGenerator
{
    private const int CODE_LENGTH = 9;
    private const int MIN_CODE = 100000000; // 9 digits starting from 100000000
    private const int MAX_CODE = 999999999; // 9 digits max

    /// <summary>
    /// Generates a unique 9-digit numeric pairing code
    /// </summary>
    public static string GeneratePairingCode()
    {
        // Generate a random 9-digit number
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        
        var randomValue = BitConverter.ToUInt32(bytes, 0);
        var code = (int)(MIN_CODE + (randomValue % (MAX_CODE - MIN_CODE + 1)));
        
        return code.ToString("D9"); // Ensure 9 digits with leading zeros if needed
    }

    /// <summary>
    /// Validates if a string is a valid 9-digit pairing code
    /// </summary>
    public static bool IsValidPairingCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        if (code.Length != CODE_LENGTH)
            return false;

        return code.All(char.IsDigit);
    }

    /// <summary>
    /// Formats a pairing code with dashes for readability (e.g., 123-456-789)
    /// </summary>
    public static string FormatPairingCode(string code)
    {
        if (!IsValidPairingCode(code))
            return code;

        return $"{code.Substring(0, 3)}-{code.Substring(3, 3)}-{code.Substring(6, 3)}";
    }

    /// <summary>
    /// Removes formatting from a pairing code (removes dashes, spaces, etc.)
    /// </summary>
    public static string NormalizePairingCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        return new string(code.Where(char.IsDigit).ToArray());
    }
}
