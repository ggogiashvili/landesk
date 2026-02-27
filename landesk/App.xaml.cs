using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LanDesk.Core.Utilities;

namespace LanDesk;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\LanDesk_SingleInstance_Mutex";
    private LanDesk.Utilities.TrayIconManager? _trayIconManager;

    /// <summary>Append to a file without throwing when another process has it locked (e.g. multiple instances starting).</summary>
    private static void SafeAppend(string path, string text)
    {
        try { File.AppendAllText(path, text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static App()
    {
        SafeAppend(Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt"), $"[{DateTime.Now}] App class static constructor called\n");
    }
    
    public App()
    {
        SafeAppend(Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt"), $"[{DateTime.Now}] App constructor called\n");
    }
    
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // This is called from XAML Startup event instead of StartupUri
        // This gives us more control over when MainWindow is created
        var tempLog = Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt");
        var simpleLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDesk", "startup-debug.txt");
        
        try { Directory.CreateDirectory(Path.GetDirectoryName(simpleLog)!); } catch { }
        SafeAppend(simpleLog, $"[{DateTime.Now}] Application_Startup called\n");
        SafeAppend(tempLog, $"[{DateTime.Now}] Application_Startup called\n");
        
        try
        {
            // Initialize logging
            try
            {
                SafeAppend(simpleLog, $"[{DateTime.Now}] Initializing logger...\n");
                Logger.Info("=== LanDesk Application Starting ===");
                Logger.Info($"Version: 1.0.1");
                Logger.Info($"OS: {Environment.OSVersion}");
                Logger.Info($"Machine: {Environment.MachineName}");
                Logger.Info($"User: {Environment.UserName}");
                Logger.Info($"Working Directory: {Environment.CurrentDirectory}");
                Logger.Info($"Log file: {Logger.LogFilePath}");
                SafeAppend(simpleLog, $"[{DateTime.Now}] Logger initialized\n");
            }
            catch (Exception logEx)
            {
                SafeAppend(simpleLog, $"[{DateTime.Now}] Logging initialization failed: {logEx.Message}\nStack: {logEx.StackTrace}\n");
            }
            
            // Ensure single instance
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                Logger.Info("App: Another instance is already running. Shutting down.");
                // We could try to find the other window and bring it to front here
                Shutdown();
                return;
            }

            // Set auto-startup in registry (for current user)
            SetAutoStartup(true);

            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            
            // Create MainWindow manually with error handling
            try
            {
                var mainWindow = new MainWindow();
                
                // Initialize tray icon
                _trayIconManager = new LanDesk.Utilities.TrayIconManager(mainWindow);
                
                // Check if we should start minimized (to tray)
                bool startMinimized = false;
                foreach (string arg in e.Args)
                {
                    if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || 
                        arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                        break;
                    }
                }

                if (startMinimized)
                {
                    Logger.Info("App: Starting minimized to tray");
                    // Don't call mainWindow.Show()
                }
                else
                {
                    mainWindow.Show();
                }
            }
            catch (Exception mwEx)
            {
                Logger.Error("Failed to create MainWindow", mwEx);
                MessageBox.Show($"Failed to create main window: {mwEx.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            
            Logger.Info("Application startup completed");
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error in Application_Startup", ex);
            MessageBox.Show($"Failed to start application: {ex.Message}", "Fatal Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void SetAutoStartup(bool enable)
    {
        try
        {
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string appPath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                    key.SetValue("LanDesk", $"\"{appPath}\" --minimized");
                    Logger.Info("App: Auto-startup enabled in registry");
                }
                else
                {
                    key.DeleteValue("LanDesk", false);
                    Logger.Info("App: Auto-startup disabled in registry");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"App: Failed to set auto-startup in registry: {ex.Message}");
        }
    }
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Write to a simple log file FIRST, before anything else
        var simpleLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDesk", "startup-debug.txt");
        var tempLog = Path.Combine(Path.GetTempPath(), "LanDesk-startup.txt");
        
        try { Directory.CreateDirectory(Path.GetDirectoryName(simpleLog)!); } catch { }
        SafeAppend(simpleLog, $"[{DateTime.Now}] OnStartup called\n");
        SafeAppend(tempLog, $"[{DateTime.Now}] OnStartup called\n");
        
        try
        {
            // Initialize logging FIRST, even before base.OnStartup
            try
            {
                SafeAppend(simpleLog, $"[{DateTime.Now}] Initializing logger...\n");
                Logger.Info("=== LanDesk Application Starting ===");
                Logger.Info($"Version: 1.0.1");
                Logger.Info($"OS: {Environment.OSVersion}");
                Logger.Info($"Machine: {Environment.MachineName}");
                Logger.Info($"User: {Environment.UserName}");
                Logger.Info($"Working Directory: {Environment.CurrentDirectory}");
                Logger.Info($"Log file: {Logger.LogFilePath}");
                SafeAppend(simpleLog, $"[{DateTime.Now}] Logger initialized\n");
            }
            catch (Exception logEx)
            {
                SafeAppend(simpleLog, $"[{DateTime.Now}] Logging initialization failed: {logEx.Message}\nStack: {logEx.StackTrace}\n");
            }
            
            SafeAppend(simpleLog, $"[{DateTime.Now}] Calling base.OnStartup...\n");
            SafeAppend(tempLog, $"[{DateTime.Now}] Calling base.OnStartup...\n");
            
            try
            {
                base.OnStartup(e);
                SafeAppend(simpleLog, $"[{DateTime.Now}] base.OnStartup completed\n");
                SafeAppend(tempLog, $"[{DateTime.Now}] base.OnStartup completed\n");
            }
            catch (Exception baseEx)
            {
                SafeAppend(simpleLog, $"[{DateTime.Now}] EXCEPTION in base.OnStartup: {baseEx.Message}\nStack: {baseEx.StackTrace}\n");
                SafeAppend(tempLog, $"[{DateTime.Now}] EXCEPTION in base.OnStartup: {baseEx.Message}\nStack: {baseEx.StackTrace}\n");
                throw;
            }
            
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            
            SafeAppend(simpleLog, $"[{DateTime.Now}] Exception handlers registered\n");
            Logger.Info("Application startup completed - creating MainWindow");
            SafeAppend(simpleLog, $"[{DateTime.Now}] OnStartup completed successfully\n");
        }
        catch (Exception ex)
        {
            SafeAppend(simpleLog, $"[{DateTime.Now}] EXCEPTION in OnStartup: {ex.Message}\nStack: {ex.StackTrace}\n");
            try { Logger.Error("Fatal error in OnStartup", ex); }
            catch { SafeAppend(simpleLog, $"[{DateTime.Now}] FATAL ERROR: {ex.Message}\nStack: {ex.StackTrace}\n"); }
            
            var errorMsg = $"Failed to start application: {ex.Message}\n\n" +
                          $"Stack Trace:\n{ex.StackTrace}\n\n" +
                          $"Debug log: {simpleLog}";
            
            try
            {
                errorMsg += $"\n\nLog file: {Logger.LogFilePath}";
            }
            catch { }
            
            try
            {
                MessageBox.Show(
                    errorMsg,
                    "Fatal Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // If MessageBox fails, at least we have the log file
            }
            
            Shutdown(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled exception in dispatcher", e.Exception);
        
        var errorMsg = $"An unhandled exception occurred:\n\n{e.Exception.Message}\n\n" +
                      $"Log file: {Logger.LogFilePath}\n\n" +
                      $"Stack Trace:\n{e.Exception.StackTrace}";
        
        MessageBox.Show(
            errorMsg,
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        
        if (exception != null)
        {
            Logger.Error("Fatal unhandled exception", exception);
        }
        else
        {
            Logger.Error("Fatal error occurred (non-Exception object)");
        }
        
        var message = exception != null 
            ? $"Fatal error: {exception.Message}\n\nLog file: {Logger.LogFilePath}\n\nStack Trace:\n{exception.StackTrace}"
            : $"A fatal error occurred.\n\nLog file: {Logger.LogFilePath}";
            
        MessageBox.Show(message, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
