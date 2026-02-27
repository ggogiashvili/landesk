using System;
using System.Runtime.InteropServices;
using LanDesk.Core.Protocol;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for injecting input events using Windows SendInput API
/// </summary>
public class InputInjectionService
{
    /// <summary>
    /// Injects an input command
    /// </summary>
    public bool InjectInput(InputCommand command)
    {
        try
        {
            // Check if UAC prompt is active
            if (LanDesk.Core.Utilities.DesktopSecurity.IsUacActive())
            {
                // If running as a service (SYSTEM privileges), allow UAC interaction (like AnyDesk)
                // Otherwise, block remote input for security (only local user can interact)
                if (LanDesk.Core.Utilities.DesktopSecurity.IsRunningAsService())
                {
                    LanDesk.Core.Utilities.Logger.Debug("InputInjectionService: UAC prompt active - allowing remote input (running as service with SYSTEM privileges)");
                    // Continue to inject input - service has privileges to interact with UAC
                }
                else
                {
                    LanDesk.Core.Utilities.Logger.Debug("InputInjectionService: Remote input blocked - UAC prompt is active (local user must interact)");
                    return false; // Block all remote input during UAC when not running as service
                }
            }

            // Explicit UAC actions: only when running as service (can interact with UAC desktop)
            if (command.Type == InputProtocol.UAC_YES || command.Type == InputProtocol.UAC_NO || command.Type == InputProtocol.UAC_CREDENTIALS)
            {
                if (!LanDesk.Core.Utilities.DesktopSecurity.IsRunningAsService())
                {
                    LanDesk.Core.Utilities.Logger.Debug("InputInjectionService: UAC Yes/No/Credentials require running as Windows Service");
                    return false;
                }
                LanDesk.Core.Utilities.DesktopSecurity.EnsureCorrectDesktop(out _);
                switch (command.Type)
                {
                    case InputProtocol.UAC_YES:
                        return SendUacYes();
                    case InputProtocol.UAC_NO:
                        return SendUacNo();
                    case InputProtocol.UAC_CREDENTIALS:
                        return SendUacCredentials(command.Username ?? string.Empty, command.Password ?? string.Empty);
                    default:
                        return false;
                }
            }

            // Ensure we are on the correct desktop (UAC support)
            LanDesk.Core.Utilities.DesktopSecurity.EnsureCorrectDesktop(out _);

            switch (command.Type)
            {
                case InputProtocol.MOUSE_MOVE:
                    return MoveMouse(command.X, command.Y);

                case InputProtocol.MOUSE_CLICK:
                    return ClickMouse(command.X, command.Y, false);

                case InputProtocol.MOUSE_RIGHT_CLICK:
                    return ClickMouse(command.X, command.Y, true);

                case InputProtocol.MOUSE_DOUBLE_CLICK:
                    return DoubleClickMouse(command.X, command.Y);

                case InputProtocol.MOUSE_WHEEL:
                    return ScrollMouse(command.Delta);

                case InputProtocol.KEY_DOWN:
                    return KeyDown(command.VirtualKey, command.IsExtended);

                case InputProtocol.KEY_UP:
                    return KeyUp(command.VirtualKey, command.IsExtended);

                case InputProtocol.KEY_PRESS:
                    return KeyPress(command.VirtualKey, command.IsExtended);

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error injecting input: {ex.Message}");
            return false;
        }
    }

    private bool MoveMouse(int x, int y)
    {
        // Get screen dimensions
        var screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
        var screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

        // Clamp coordinates to screen bounds
        x = Math.Max(0, Math.Min(screenWidth - 1, x));
        y = Math.Max(0, Math.Min(screenHeight - 1, y));

        // Convert to normalized coordinates (0-65535)
        var normalizedX = (int)((x * 65535.0) / screenWidth);
        var normalizedY = (int)((y * 65535.0) / screenHeight);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE | MOUSEEVENTF_VIRTUALDESK,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        
        // Small delay to ensure input is processed
        if (result)
        {
            System.Threading.Thread.Sleep(1);
        }
        
        return result;
    }

    private bool ClickMouse(int x, int y, bool rightButton)
    {
        // Get screen dimensions for normalized coordinates
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        
        // Clamp coordinates to screen bounds
        x = Math.Max(0, Math.Min(screenWidth - 1, x));
        y = Math.Max(0, Math.Min(screenHeight - 1, y));
        
        // Move to position first (this ensures the mouse is at the correct location)
        MoveMouse(x, y);
        System.Threading.Thread.Sleep(5);

        // Get screen dimensions for normalized coordinates
        var normalizedX = (int)((x * 65535.0) / screenWidth);
        var normalizedY = (int)((y * 65535.0) / screenHeight);

        var inputs = new INPUT[2];

        // Mouse down
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | (rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN),
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Mouse up
        inputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | (rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP),
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var result = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT))) == 2;
        
        // Small delay to ensure click is processed
        if (result)
        {
            System.Threading.Thread.Sleep(5);
        }
        
        return result;
    }

    private bool DoubleClickMouse(int x, int y)
    {
        ClickMouse(x, y, false);
        System.Threading.Thread.Sleep(50);
        ClickMouse(x, y, false);
        return true;
    }

    private bool ScrollMouse(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = MOUSEEVENTF_WHEEL,
                    mouseData = (uint)delta,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private bool KeyDown(int virtualKey, bool isExtended)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    dwFlags = isExtended ? KEYEVENTF_EXTENDEDKEY : 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private bool KeyUp(int virtualKey, bool isExtended)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    dwFlags = KEYEVENTF_KEYUP | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0),
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private bool KeyPress(int virtualKey, bool isExtended)
    {
        KeyDown(virtualKey, isExtended);
        System.Threading.Thread.Sleep(10);
        KeyUp(virtualKey, isExtended);
        return true;
    }

    /// <summary>VK_RETURN = 0x0D, VK_TAB = 0x09</summary>
    private const int VK_RETURN = 0x0D;
    private const int VK_TAB = 0x09;

    private bool SendUacYes()
    {
        // UAC default button is usually "Yes" - press Enter
        LanDesk.Core.Utilities.Logger.Info("InputInjectionService: Sending UAC Yes (Enter)");
        return KeyPress(VK_RETURN, false);
    }

    private bool SendUacNo()
    {
        // Move focus to No (Tab) then Enter
        LanDesk.Core.Utilities.Logger.Info("InputInjectionService: Sending UAC No (Tab+Enter)");
        if (!KeyPress(VK_TAB, false)) return false;
        System.Threading.Thread.Sleep(50);
        return KeyPress(VK_RETURN, false);
    }

    private bool SendUacCredentials(string username, string password)
    {
        LanDesk.Core.Utilities.Logger.Info("InputInjectionService: Sending UAC credentials (username, tab, password, enter)");
        if (!TypeUnicodeString(username)) return false;
        System.Threading.Thread.Sleep(30);
        if (!KeyPress(VK_TAB, false)) return false;
        System.Threading.Thread.Sleep(30);
        if (!TypeUnicodeString(password)) return false;
        System.Threading.Thread.Sleep(30);
        return KeyPress(VK_RETURN, false);
    }

    /// <summary>Types a string using Unicode SendInput (for UAC username/password fields)</summary>
    private bool TypeUnicodeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        var inputs = new INPUT[text.Length * 2]; // key down + key up per char
        int i = 0;
        foreach (char c in text)
        {
            inputs[i++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            inputs[i++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_KEYUP | KEYEVENTF_UNICODE,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        return sent == inputs.Length;
    }

    #region Windows API

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion
}
