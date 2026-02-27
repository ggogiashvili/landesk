# LanDesk Server Configuration

## Overview

The LanDesk application can connect to a discovery server to find devices across networks. The server IP and port are configurable via environment variables.

## Current Behavior

**By default**, the application is hardcoded to connect to `10.246.84.208:8080`. This is a fallback if environment variables are not set.

## Configuration via Environment Variables

You can configure the server IP and port using environment variables:

### Windows (Command Prompt)
```cmd
set LANDESK_SERVER_IP=192.168.1.100
set LANDESK_SERVER_PORT=8080
```

### Windows (PowerShell)
```powershell
$env:LANDESK_SERVER_IP="192.168.1.100"
$env:LANDESK_SERVER_PORT="8080"
```

### Windows (System-wide - Permanent)
1. Open **System Properties** → **Environment Variables**
2. Add new User or System variable:
   - Variable: `LANDESK_SERVER_IP`
   - Value: `192.168.1.100` (your server IP)
3. Add another variable:
   - Variable: `LANDESK_SERVER_PORT`
   - Value: `8080` (or `80` if server runs as admin)

### Linux/Mac
```bash
export LANDESK_SERVER_IP=192.168.1.100
export LANDESK_SERVER_PORT=8080
```

## How It Works

1. **Server Discovery**: The app connects to the configured server IP:port
2. **Device Registration**: When the app starts, it registers with the server
3. **Device Discovery**: The app queries the server to find other devices
4. **Pairing Code Lookup**: When you enter a pairing code, the app queries the server
5. **Direct Connection**: Once the IP is found, devices connect directly (P2P)

## Examples

### Example 1: Local Network Server
```cmd
set LANDESK_SERVER_IP=192.168.1.100
set LANDESK_SERVER_PORT=8080
```

### Example 2: Server on Different Port
```cmd
set LANDESK_SERVER_IP=10.246.84.208
set LANDESK_SERVER_PORT=80
```

### Example 3: Disable Server Discovery
```cmd
set LANDESK_SERVER_IP=
```
(Leave empty or don't set - app will use UDP discovery only)

## Server Requirements

The discovery server must be:
- Accessible from all devices that need to connect
- Running on the specified IP and port
- Firewall rules must allow incoming connections on that port

## Testing Connection

To test if the server is reachable:
```cmd
ping 192.168.1.100
curl http://192.168.1.100:8080/
```

## Notes

- If `LANDESK_SERVER_IP` is not set, the app falls back to hardcoded IP `10.246.84.208`
- If `LANDESK_SERVER_PORT` is not set, it defaults to `8080`
- The server IP can be any reachable IP address (local network, public IP, domain name)
- Port 80 requires administrator privileges on Windows
- Port 8080 works without admin privileges
