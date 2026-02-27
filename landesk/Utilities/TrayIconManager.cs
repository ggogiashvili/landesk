using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using LanDesk.Core.Utilities;

namespace LanDesk.Utilities;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _mainWindow;
    private bool _isDisposed;

    public TrayIconManager(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _notifyIcon = new NotifyIcon();
        
        // Use application icon if available, otherwise fallback to system icon
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Icon = icon ?? SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Logger.Warning($"TrayIconManager: Failed to load application icon: {ex.Message}");
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Visible = true;
        _notifyIcon.Text = "LanDesk Remote Desktop";
        
        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
        
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open LanDesk", null, (s, e) => RestoreWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
        
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    public void RestoreWindow()
    {
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
    }

    public void ShowBalloonTip(string title, string tip, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, tip, icon);
    }

    private void ExitApplication()
    {
        Logger.Info("TrayIconManager: User requested exit from context menu");
        Dispose();
        if (_mainWindow is MainWindow main)
        {
            main.ExplicitExit();
        }
        else
        {
            _mainWindow.Close();
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _isDisposed = true;
        }
    }
}
