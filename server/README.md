# LanDesk Discovery Server

A central registry server for LanDesk device discovery. Devices register with this server and can find each other by pairing code. Actual data transfer (screen streaming, input) happens directly between devices (P2P).

## Features

- **Device Registration**: Devices register with their IP address, pairing code, and device info
- **Device Discovery**: Find devices by pairing code or list all available devices
- **Heartbeat System**: Devices send periodic heartbeats to stay registered
- **Auto Cleanup**: Automatically removes stale devices that haven't sent heartbeats
- **RESTful API**: Simple HTTP/JSON API for all operations

## Installation

1. Install Python 3.7 or higher
2. Install dependencies:
```bash
pip install -r requirements.txt
```

## Running the Server

```bash
python landesk_server.py
```

The server will start on port 9061 by default (accessible from all network interfaces).

**Port Configuration:**
- **Default:** Port 9061 (no admin privileges required)
- **Port 80:** Requires administrator privileges on Windows

**To use port 80:**
1. Run the server as Administrator
2. Or specify port: `python landesk_server.py 80`
3. Or set environment variable: `set LANDESK_SERVER_PORT=80`

**To use a different port:**
```bash
python landesk_server.py 5000
# or
set LANDESK_SERVER_PORT=5000
python landesk_server.py
```

## API Endpoints

### GET `/`
Server status and endpoint information

### POST `/api/register`
Register a device with the server

**Request Body:**
```json
{
    "device_id": "unique-device-id",
    "device_name": "Computer Name",
    "ip_address": "10.246.80.105",
    "pairing_code": "1234-5678",
    "pairing_code": "1234-5678",
    "control_port": 9213,
    "input_port": 9226,
    "discovery_port": 9061,
    "version": "1.0.0",
    "operating_system": "Windows 10"
}
```

**Response:**
```json
{
    "status": "success",
    "action": "registered",
    "device_id": "unique-device-id",
    "message": "Device registered successfully"
}
```

### POST `/api/heartbeat`
Update device heartbeat (keeps device alive)

**Request Body:**
```json
{
    "device_id": "unique-device-id",
    "ip_address": "10.246.80.105"
}
```

### GET `/api/discover`
Get list of all registered devices

**Query Parameters:**
- `exclude_device_id` (optional): Device ID to exclude from results

**Response:**
```json
{
    "status": "success",
    "devices": [
        {
            "device_id": "device-1",
            "device_name": "Computer 1",
            "ip_address": "10.246.80.105",
            "pairing_code": "1234-5678",
            "pairing_code": "1234-5678",
            "control_port": 9213,
            "input_port": 9226,
            "discovery_port": 9061,
            "version": "1.0.0"
        }
    ],
    "count": 1
}
```

### GET `/api/find/<pairing_code>`
Find a device by pairing code

**Example:** `GET /api/find/123456789` or `GET /api/find/123-456-789`

**Response:**
```json
{
    "status": "success",
    "device": {
        "device_id": "device-1",
        "device_name": "Computer 1",
        "ip_address": "10.246.80.105",
        "pairing_code": "1234-5678",
        "pairing_code": "1234-5678",
        "control_port": 9213,
        "input_port": 9226,
        "discovery_port": 9061,
        "version": "1.0.0"
    }
}
```

### GET `/api/devices`
Get detailed list of all devices (includes online status, timestamps)

### GET `/api/health`
Health check endpoint for monitoring

**Response:**
```json
{
    "status": "healthy",
    "timestamp": "2024-01-01T12:00:00",
    "devices": 5,
    "server_uptime_seconds": 3600
}
```

### GET `/api/stats`
Get server statistics (if enabled)

**Query Parameters:**
- `include_recent_attempts` (optional): Include recent connection attempts (default: false)

**Response:**
```json
{
    "status": "success",
    "statistics": {
        "total_registrations": 100,
        "total_heartbeats": 5000,
        "total_discoveries": 50,
        "total_find_by_code": 30,
        "total_connection_attempts": 25,
        "total_unregistrations": 5,
        "devices_removed_stale": 10,
        "current_devices": 5,
        "current_online": 4,
        "server_uptime_seconds": 3600,
        "last_cleanup": "2024-01-01T12:00:00",
        "errors": {}
    },
    "recent_connection_attempts": [
        {
            "timestamp": "2024-01-01T12:00:00",
            "requester_device_id": "device-1",
            "requester_ip": "10.246.80.105",
            "target_device_id": "device-2",
            "target_device_name": "Computer 2",
            "target_ip": "10.246.80.106",
            "pairing_code": "123-456-789",
            "status": "success"
        }
    ]
}
```

**Note:** Connection attempts are logged in real-time. Check server logs or use `/api/stats?include_recent_attempts=true` to see recent attempts.

### POST `/api/unregister`
Unregister a device from the server

**Request Body:**
```json
{
    "device_id": "unique-device-id"
}
```

## How It Works

1. **Device Registration**: When a LanDesk device starts, it registers with the server, providing its IP address, pairing code, and device information.

2. **Device Discovery**: When a user wants to connect to another device:
   - They enter the pairing code
   - The app queries the server: `GET /api/find/<pairing_code>`
   - Server returns the device's IP address
   - App connects directly to that IP address (P2P)

3. **Heartbeat**: Devices send periodic heartbeats to keep their registration alive. Devices that don't send heartbeats for 60 seconds are automatically removed.

4. **Direct Connection**: Once devices find each other through the server, all data transfer (screen streaming, input control) happens directly between devices - the server is not involved in data transfer.

## Configuration

### Environment Variables

- **LANDESK_SERVER_PORT**: Server port (default: 9061)
- **LANDESK_DEVICE_TIMEOUT**: Device timeout in seconds (default: 60)
- **LANDESK_CLEANUP_INTERVAL**: Cleanup interval in seconds (default: 30)
- **LANDESK_MAX_DEVICES**: Maximum devices allowed (default: 10000)
- **LANDESK_ENABLE_STATS**: Enable statistics endpoint (default: true)

### Examples

```bash
# Custom port
set LANDESK_SERVER_PORT=5000
python landesk_server.py

# Custom timeout
set LANDESK_DEVICE_TIMEOUT=120
python landesk_server.py

# Disable statistics
set LANDESK_ENABLE_STATS=false
python landesk_server.py
```

## Features

- ✅ **Structured Logging**: Logs to both console and file (`landesk_server.log`)
- ✅ **Request Logging**: All requests are logged (except heartbeats to reduce spam)
- ✅ **Error Tracking**: Errors are tracked and available in statistics
- ✅ **Health Check**: `/api/health` endpoint for monitoring
- ✅ **Statistics**: `/api/stats` endpoint for server metrics
- ✅ **Input Validation**: Validates pairing codes, IP addresses, and required fields
- ✅ **Pairing Code Protection**: Prevents pairing code changes after registration
- ✅ **Device Limits**: Configurable maximum device limit
- ✅ **Automatic Cleanup**: Removes stale devices automatically
- ✅ **Thread-Safe**: All operations are thread-safe

## Production Deployment

### High-Performance Setup

The server is optimized to handle **hundreds of concurrent requests**. For production:

**Windows:**
```bash
# Use production startup script (uses Waitress)
start_server_production.bat 8080

# Or manually:
set LANDESK_USE_PRODUCTION_SERVER=true
set LANDESK_THREADS=8
set LANDESK_WORKERS=4
python landesk_server.py 8080
```

**Linux/Mac:**
```bash
# Use production startup script (uses Gunicorn)
chmod +x start_server_production.sh
./start_server_production.sh 8080

# Or manually:
export LANDESK_USE_PRODUCTION_SERVER=true
export LANDESK_THREADS=8
export LANDESK_WORKERS=4
python3 landesk_server.py 8080
```

### Performance Configuration

Environment variables for performance tuning:

- **LANDESK_USE_PRODUCTION_SERVER**: Use production WSGI server (default: true)
- **LANDESK_THREADS**: Threads per worker (default: 8)
- **LANDESK_WORKERS**: Worker processes (default: 4, Gunicorn only)
- **LANDESK_MAX_DEVICES**: Maximum devices (default: 10000)

**Recommended settings for high load:**
```bash
# For 500+ concurrent requests
set LANDESK_THREADS=16
set LANDESK_WORKERS=8
```

### Production Server Features

- ✅ **Multi-threaded**: Handles hundreds of concurrent requests
- ✅ **Production WSGI**: Uses Waitress (Windows) or Gunicorn (Linux)
- ✅ **Optimized locks**: RLock for better read performance
- ✅ **Connection pooling**: Built into production servers
- ✅ **Request queuing**: Automatic request queuing under load

### Additional Production Considerations

- Using a reverse proxy (nginx, Apache)
- Adding authentication/authorization
- Using a database instead of in-memory storage
- Adding SSL/TLS encryption
- Adding rate limiting
- Setting up log rotation
- Monitoring with health checks (`/api/health`)

## Logging

Logs are written to:
- **Console**: Standard output with timestamps
- **File**: `landesk_server.log` in the server directory

Log levels:
- **INFO**: Normal operations (registrations, unregistrations, etc.)
- **WARNING**: Non-critical issues (invalid requests, etc.)
- **ERROR**: Errors that need attention
- **DEBUG**: Detailed request/response logging (disabled by default)
