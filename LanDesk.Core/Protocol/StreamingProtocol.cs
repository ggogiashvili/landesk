using System.Text;

namespace LanDesk.Core.Protocol;

/// <summary>
/// Defines the streaming protocol for screen sharing
/// </summary>
public static class StreamingProtocol
{
    // Protocol constants
    public const string FRAME_HEADER = "FRAME";
    public const string START_STREAM = "START_STREAM";
    public const string STOP_STREAM = "STOP_STREAM";
    public const int HEADER_SIZE = 16; // Size of frame header in bytes

    /// <summary>
    /// Creates a frame header with size information
    /// </summary>
    public static byte[] CreateFrameHeader(int frameSize, int width, int height)
    {
        var header = new byte[HEADER_SIZE];
        var headerString = $"{FRAME_HEADER}:{frameSize}:{width}:{height}";
        var headerBytes = Encoding.ASCII.GetBytes(headerString);
        
        Array.Copy(headerBytes, header, Math.Min(headerBytes.Length, HEADER_SIZE));
        
        // Pad with zeros if needed
        for (int i = headerBytes.Length; i < HEADER_SIZE; i++)
        {
            header[i] = 0;
        }
        
        return header;
    }

    /// <summary>
    /// Parses a frame header
    /// </summary>
    public static FrameHeader? ParseFrameHeader(byte[] header)
    {
        if (header.Length < HEADER_SIZE)
            return null;

        try
        {
            var headerString = Encoding.ASCII.GetString(header).TrimEnd('\0');
            var parts = headerString.Split(':');

            if (parts.Length >= 4 && parts[0] == FRAME_HEADER)
            {
                return new FrameHeader
                {
                    FrameSize = int.Parse(parts[1]),
                    Width = int.Parse(parts[2]),
                    Height = int.Parse(parts[3])
                };
            }
        }
        catch
        {
            // Invalid header
        }

        return null;
    }

    /// <summary>
    /// Creates a start stream command
    /// </summary>
    public static byte[] CreateStartStreamCommand()
    {
        return Encoding.ASCII.GetBytes(START_STREAM);
    }

    /// <summary>
    /// Creates a stop stream command
    /// </summary>
    public static byte[] CreateStopStreamCommand()
    {
        return Encoding.ASCII.GetBytes(STOP_STREAM);
    }

    /// <summary>
    /// Checks if data is a command
    /// </summary>
    public static bool IsCommand(byte[] data, out string? command)
    {
        command = null;
        
        try
        {
            var text = Encoding.ASCII.GetString(data).TrimEnd('\0');
            if (text == START_STREAM || text == STOP_STREAM)
            {
                command = text;
                return true;
            }
        }
        catch
        {
            // Not a command
        }

        return false;
    }
}

/// <summary>
/// Frame header information
/// </summary>
public class FrameHeader
{
    public int FrameSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
