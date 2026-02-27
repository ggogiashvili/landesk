using System.Net.Sockets;
using LanDesk.Core.Protocol;
using LanDesk.Core.Utilities;

namespace LanDesk.Core.Services;

/// <summary>
/// Service for sending input commands over TCP
/// </summary>
public class InputSenderService : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isConnected;

    /// <summary>
    /// Connects to a remote device for input sending
    /// </summary>
    public void Connect(TcpClient client)
    {
        // Close previous connection if any
        if (_client != null)
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
        }
        
        _client = client;
        _stream = client.GetStream();
        _isConnected = client.Connected;
        
        if (_isConnected)
        {
            Utilities.Logger.Info($"InputSenderService: Connected to {client.Client.RemoteEndPoint}");
        }
    }

    /// <summary>
    /// Sends a mouse move command
    /// </summary>
    public void SendMouseMove(int x, int y)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateMouseMoveCommand(x, y);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending mouse move: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a mouse click command
    /// </summary>
    public void SendMouseClick(int x, int y, bool isRightButton = false, bool isDoubleClick = false)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateMouseClickCommand(x, y, isRightButton, isDoubleClick);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending mouse click: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a mouse wheel command
    /// </summary>
    public void SendMouseWheel(int delta)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateMouseWheelCommand(delta);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending mouse wheel: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a key press command
    /// </summary>
    public void SendKeyPress(int virtualKey, bool isExtended = false)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateKeyCommand(InputProtocol.KEY_PRESS, virtualKey, isExtended);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending key press: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a key down command
    /// </summary>
    public void SendKeyDown(int virtualKey, bool isExtended = false)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateKeyCommand(InputProtocol.KEY_DOWN, virtualKey, isExtended);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending key down: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a key up command
    /// </summary>
    public void SendKeyUp(int virtualKey, bool isExtended = false)
    {
        if (!_isConnected || _stream == null)
            return;

        try
        {
            var command = InputProtocol.CreateKeyCommand(InputProtocol.KEY_UP, virtualKey, isExtended);
            SendCommand(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending key up: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends UAC "Yes" (clicks Yes on the remote UAC prompt). Requires remote to run as Windows Service.
    /// </summary>
    public void SendUacYes()
    {
        if (!_isConnected || _stream == null) return;
        try
        {
            SendCommand(InputProtocol.CreateUacYesCommand());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending UAC Yes: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends UAC "No" (clicks No on the remote UAC prompt). Requires remote to run as Windows Service.
    /// </summary>
    public void SendUacNo()
    {
        if (!_isConnected || _stream == null) return;
        try
        {
            SendCommand(InputProtocol.CreateUacNoCommand());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending UAC No: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends UAC credentials (types username, tab, password, enter). Requires remote to run as Windows Service.
    /// </summary>
    public void SendUacCredentials(string username, string password)
    {
        if (!_isConnected || _stream == null) return;
        try
        {
            SendCommand(InputProtocol.CreateUacCredentialsCommand(username ?? string.Empty, password ?? string.Empty));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending UAC credentials: {ex.Message}");
        }
    }

    private void SendCommand(byte[] command)
    {
        // Check connection status before sending
        if (_stream == null || !_stream.CanWrite || _client == null || !_client.Connected)
        {
            // Connection lost - update state
            _isConnected = false;
            return;
        }

        try
        {
            // Send command size first
            var sizeBytes = BitConverter.GetBytes(command.Length);
            _stream.Write(sizeBytes, 0, sizeBytes.Length);
            
            // Send command data
            _stream.Write(command, 0, command.Length);
            _stream.Flush();
        }
        catch (System.IO.IOException)
        {
            // Connection closed
            _isConnected = false;
            Utilities.Logger.Debug("InputSenderService: Connection closed while sending");
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Socket error
            _isConnected = false;
            Utilities.Logger.Debug("InputSenderService: Socket error while sending");
        }
    }

    public void Dispose()
    {
        _isConnected = false;
        try
        {
            _stream?.Close();
        }
        catch { }
        try
        {
            _client?.Close();
        }
        catch { }
        _stream = null;
        _client = null;
    }
}
