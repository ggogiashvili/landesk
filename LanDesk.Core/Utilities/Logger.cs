using System;
using System.IO;

namespace LanDesk.Core.Utilities;

/// <summary>
/// Simple file-based logger for LanDesk
/// </summary>
public static class Logger
{
    private static string? _logFilePath;
    private static readonly object _lock = new object();

    /// <summary>
    /// Gets the log file path
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (_logFilePath == null)
            {
                try
                {
                    // Try user's LocalApplicationData first (for GUI app)
                    var userLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDesk", "Logs");
                    
                    // For services running in Session 0 or as SYSTEM, use ProgramData
                    var isService = System.Diagnostics.Process.GetCurrentProcess().SessionId == 0 ||
                                    Environment.UserName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                                    Environment.UserDomainName.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase);
                    
                    string logDir;
                    if (isService)
                    {
                        // Service runs as SYSTEM/Session 0 - use ProgramData for shared access
                        logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LanDesk", "Logs");
                    }
                    else
                    {
                        // GUI app - use user's LocalApplicationData
                        logDir = userLogDir;
                    }
                    
                    Directory.CreateDirectory(logDir);
                    _logFilePath = Path.Combine(logDir, $"LanDesk_{DateTime.Now:yyyyMMdd}.log");
                    
                    // Log which location we're using
                    if (isService)
                    {
                        WriteLog("INFO", $"Service detected - logging to ProgramData: {_logFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to temp directory if all else fails
                    var tempLog = Path.Combine(Path.GetTempPath(), "LanDesk.log");
                    _logFilePath = tempLog;
                    try
                    {
                        File.AppendAllText(tempLog, $"[{DateTime.Now}] WARNING: Could not create log directory, using temp: {ex.Message}\n");
                    }
                    catch { }
                }
            }
            return _logFilePath;
        }
    }

    /// <summary>
    /// Logs an information message
    /// </summary>
    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    /// <summary>
    /// Logs a warning message
    /// </summary>
    public static void Warning(string message)
    {
        WriteLog("WARN", message);
    }

    /// <summary>
    /// Logs an error message
    /// </summary>
    public static void Error(string message, Exception? exception = null)
    {
        var errorMessage = message;
        if (exception != null)
        {
            errorMessage += $"\nException: {exception.GetType().Name}: {exception.Message}\nStack Trace: {exception.StackTrace}";
        }
        WriteLog("ERROR", errorMessage);
    }

    /// <summary>
    /// Logs a debug message
    /// </summary>
    public static void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }

    private static void WriteLog(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                
                // Also write to console if available
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // Ignore logging errors to prevent infinite loops
            }
        }
    }

    /// <summary>
    /// Gets the log directory path
    /// </summary>
    public static string GetLogDirectory()
    {
        return Path.GetDirectoryName(LogFilePath)!;
    }
}
