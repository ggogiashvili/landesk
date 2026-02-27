using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Utility for handling Windows Desktop security and switching
/// Allows processes (especially services) to interact with the UAC/Secure desktop
/// </summary>
public static class DesktopSecurity
{
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;
    private const int UOI_NAME = 2;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] StringBuilder pvInfo, uint nLength, out uint lpnLengthNeeded);

    // Windows Terminal Services APIs for session management
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private static string _lastDesktopName = string.Empty;
    private static bool _isUacActive = false;

    /// <summary>
    /// Event fired when UAC state changes (becomes active or inactive)
    /// </summary>
    public static event EventHandler<bool>? UacStateChanged;

    /// <summary>
    /// Checks if running as a Windows service (with SYSTEM privileges or in Session 0)
    /// </summary>
    public static bool IsRunningAsService()
    {
        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            if (sessionId == 0) return true;
            
            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
            return currentUser?.IsSystem ?? false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Checks if UAC/secure desktop is currently active
    /// </summary>
    public static bool IsUacActive()
    {
        try
        {
            IntPtr hInputDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
            if (hInputDesktop == IntPtr.Zero) return false;

            try
            {
                string desktopName = GetDesktopName(hInputDesktop);
                return desktopName.Equals("Winlogon", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CloseDesktop(hInputDesktop);
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// Ensures the current thread is attached to the active input desktop (e.g., Default or Winlogon/UAC)
    /// </summary>
    /// <returns>True if the desktop was switched or is already correct</returns>
    public static bool EnsureCorrectDesktop(out bool switched)
    {
        switched = false;
        try
        {
            IntPtr hInputDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
            if (hInputDesktop == IntPtr.Zero)
            {
                // In Session 0, if no user is logged in, this might fail
                return false;
            }

            try
            {
                string currentInputDesktopName = GetDesktopName(hInputDesktop);
                bool uacNowActive = currentInputDesktopName.Equals("Winlogon", StringComparison.OrdinalIgnoreCase);
                
                // Fire event if UAC state changed
                if (uacNowActive != _isUacActive)
                {
                    _isUacActive = uacNowActive;
                    Logger.Info($"DesktopSecurity: UAC state changed. Active: {_isUacActive}");
                    UacStateChanged?.Invoke(null, _isUacActive);
                }

                if (currentInputDesktopName != _lastDesktopName)
                {
                    if (SetThreadDesktop(hInputDesktop))
                    {
                        Logger.Info($"DesktopSecurity: Switched thread desktop to '{currentInputDesktopName}'");
                        _lastDesktopName = currentInputDesktopName;
                        switched = true;
                        return true;
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Debug($"DesktopSecurity: SetThreadDesktop failed for '{currentInputDesktopName}'. Error: {error}");
                    }
                }
                else
                {
                    return true; // Already on the correct desktop
                }
            }
            finally
            {
                CloseDesktop(hInputDesktop);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"DesktopSecurity: Error ensuring correct desktop: {ex.Message}");
        }

        return false;
    }

    private static string GetDesktopName(IntPtr hDesktop)
    {
        StringBuilder sb = new StringBuilder(256);
        if (GetUserObjectInformation(hDesktop, UOI_NAME, sb, (uint)sb.Capacity, out _))
        {
            return sb.ToString();
        }
        return string.Empty;
    }

    /// <summary>
    /// Gets the active console session ID (the session where the user is logged in)
    /// Returns 0xFFFFFFFF if no active session or on error
    /// </summary>
    public static uint GetActiveConsoleSessionId()
    {
        try
        {
            return WTSGetActiveConsoleSessionId();
        }
        catch (Exception ex)
        {
            Logger.Debug($"DesktopSecurity: Error getting active console session ID: {ex.Message}");
            return 0xFFFFFFFF; // Invalid session ID
        }
    }

    /// <summary>
    /// Checks if we're in Session 0 (service session) and if there's an active user session
    /// </summary>
    public static bool HasActiveUserSession()
    {
        try
        {
            uint currentSession = (uint)Process.GetCurrentProcess().SessionId;
            uint activeSession = GetActiveConsoleSessionId();
            
            // If we're in Session 0 and there's an active console session, we need session-aware capture
            if (currentSession == 0 && activeSession != 0xFFFFFFFF && activeSession != 0)
            {
                Logger.Debug($"DesktopSecurity: Service in Session 0, active user session is {activeSession}");
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Launches a helper process in the active user session for screen capture
    /// Returns process handle and ID, or null if failed
    /// </summary>
    public static HelperProcessInfo? LaunchHelperProcessInUserSession(string exePath, string arguments)
    {
        try
        {
            uint activeSession = GetActiveConsoleSessionId();
            if (activeSession == 0xFFFFFFFF || activeSession == 0)
            {
                Logger.Error("DesktopSecurity: No active user session found");
                return null;
            }

            // Get user token for the active session
            if (!WTSQueryUserToken(activeSession, out IntPtr userToken) || userToken == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"DesktopSecurity: Failed to get user token for session {activeSession}. Error: {error}");
                return null;
            }

            try
            {
                // Create environment block for the user
                IntPtr environment = IntPtr.Zero;
                bool envCreated = CreateEnvironmentBlock(out environment, userToken, false);
                
                try
                {
                    var si = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        dwFlags = 0,
                        wShowWindow = 0
                    };

                    var pi = new PROCESS_INFORMATION();

                    string commandLine = $"\"{exePath}\" {arguments}";
                    uint creationFlags = CREATE_NO_WINDOW;
                    if (envCreated)
                    {
                        creationFlags |= CREATE_UNICODE_ENVIRONMENT;
                    }

                    bool success = CreateProcessAsUser(
                        userToken,
                        exePath,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        creationFlags,
                        envCreated ? environment : IntPtr.Zero,
                        null,
                        ref si,
                        out pi);

                    if (!success)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Error($"DesktopSecurity: CreateProcessAsUser failed. Error: {error}");
                        return null;
                    }

                    Logger.Info($"DesktopSecurity: Helper process launched with PID {pi.dwProcessId} in user session {activeSession}");

                    // Close thread handle - we only need process handle
                    if (pi.hThread != IntPtr.Zero)
                    {
                        CloseHandle(pi.hThread);
                    }

                    return new HelperProcessInfo
                    {
                        ProcessHandle = pi.hProcess,
                        ProcessId = pi.dwProcessId
                    };
                }
                finally
                {
                    if (envCreated && environment != IntPtr.Zero)
                    {
                        DestroyEnvironmentBlock(environment);
                    }
                }
            }
            finally
            {
                if (userToken != IntPtr.Zero)
                {
                    CloseHandle(userToken);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"DesktopSecurity: Exception launching helper process: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// Checks if a helper process is still running
    /// </summary>
    public static bool IsProcessRunning(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero) return false;
        
        uint result = WaitForSingleObject(processHandle, 0);
        // WAIT_TIMEOUT (0x102) means process is still running
        // WAIT_OBJECT_0 (0) means process has exited
        return result == WAIT_TIMEOUT;
    }

    /// <summary>
    /// Information about a launched helper process
    /// </summary>
    public class HelperProcessInfo
    {
        public IntPtr ProcessHandle { get; set; }
        public int ProcessId { get; set; }
    }
}
