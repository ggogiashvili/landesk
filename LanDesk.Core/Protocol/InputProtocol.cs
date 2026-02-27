using System.Text;
using System.Text.Json;

namespace LanDesk.Core.Protocol;

/// <summary>
/// Defines the input protocol for remote control
/// </summary>
public static class InputProtocol
{
    public const string MOUSE_MOVE = "MOUSE_MOVE";
    public const string MOUSE_CLICK = "MOUSE_CLICK";
    public const string MOUSE_DOUBLE_CLICK = "MOUSE_DOUBLE_CLICK";
    public const string MOUSE_RIGHT_CLICK = "MOUSE_RIGHT_CLICK";
    public const string MOUSE_WHEEL = "MOUSE_WHEEL";
    public const string KEY_DOWN = "KEY_DOWN";
    public const string KEY_UP = "KEY_UP";
    public const string KEY_PRESS = "KEY_PRESS";

    /// <summary>
    /// UAC dialog actions (when remote sees UAC prompt - click Yes, No, or enter credentials)
    /// </summary>
    public const string UAC_YES = "UAC_YES";
    public const string UAC_NO = "UAC_NO";
    public const string UAC_CREDENTIALS = "UAC_CREDENTIALS";

    /// <summary>
    /// Creates a mouse move command
    /// </summary>
    public static byte[] CreateMouseMoveCommand(int x, int y)
    {
        var command = new InputCommand
        {
            Type = MOUSE_MOVE,
            X = x,
            Y = y
        };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a mouse click command
    /// </summary>
    public static byte[] CreateMouseClickCommand(int x, int y, bool isRightButton = false, bool isDoubleClick = false)
    {
        var command = new InputCommand
        {
            Type = isDoubleClick ? MOUSE_DOUBLE_CLICK : (isRightButton ? MOUSE_RIGHT_CLICK : MOUSE_CLICK),
            X = x,
            Y = y
        };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a mouse wheel command
    /// </summary>
    public static byte[] CreateMouseWheelCommand(int delta)
    {
        var command = new InputCommand
        {
            Type = MOUSE_WHEEL,
            Delta = delta
        };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a key press command
    /// </summary>
    public static byte[] CreateKeyCommand(string type, int virtualKey, bool isExtended = false)
    {
        var command = new InputCommand
        {
            Type = type,
            VirtualKey = virtualKey,
            IsExtended = isExtended
        };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a UAC "Yes" command (simulates clicking Yes on UAC prompt)
    /// </summary>
    public static byte[] CreateUacYesCommand()
    {
        var command = new InputCommand { Type = UAC_YES };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a UAC "No" command (simulates clicking No on UAC prompt)
    /// </summary>
    public static byte[] CreateUacNoCommand()
    {
        var command = new InputCommand { Type = UAC_NO };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Creates a UAC credentials command (types username, tab, password, enter)
    /// </summary>
    public static byte[] CreateUacCredentialsCommand(string username, string password)
    {
        var command = new InputCommand
        {
            Type = UAC_CREDENTIALS,
            Username = username ?? string.Empty,
            Password = password ?? string.Empty
        };
        return SerializeCommand(command);
    }

    /// <summary>
    /// Parses an input command
    /// </summary>
    public static InputCommand? ParseCommand(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<InputCommand>(json);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] SerializeCommand(InputCommand command)
    {
        var json = JsonSerializer.Serialize(command);
        return Encoding.UTF8.GetBytes(json);
    }
}

/// <summary>
/// Input command structure
/// </summary>
public class InputCommand
{
    public string Type { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int VirtualKey { get; set; }
    public int Delta { get; set; }
    public bool IsExtended { get; set; }
    /// <summary>For UAC_CREDENTIALS: username to type</summary>
    public string? Username { get; set; }
    /// <summary>For UAC_CREDENTIALS: password to type</summary>
    public string? Password { get; set; }
}
