#!/usr/bin/env python3
"""
LanDesk Discovery Server
Acts as a central registry for device discovery and pairing codes.
Devices register with the server and can find each other by pairing code.
Actual data transfer (screen streaming, input) happens directly between devices (P2P).
"""

import json
import time
import threading
import logging
import sys
import os
from datetime import datetime, timedelta
from flask import Flask, request, jsonify
from flask_cors import CORS
from typing import Dict, Optional
from collections import defaultdict

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%Y-%m-%d %H:%M:%S',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('landesk_server.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app)  # Allow cross-origin requests

# Configuration
DEVICE_TIMEOUT = int(os.environ.get('LANDESK_DEVICE_TIMEOUT', 60))  # seconds
CLEANUP_INTERVAL = int(os.environ.get('LANDESK_CLEANUP_INTERVAL', 30))  # seconds
MAX_DEVICES = int(os.environ.get('LANDESK_MAX_DEVICES', 10000))  # maximum devices
ENABLE_STATS = os.environ.get('LANDESK_ENABLE_STATS', 'true').lower() == 'true'
USE_PRODUCTION_SERVER = os.environ.get('LANDESK_USE_PRODUCTION_SERVER', 'true').lower() == 'true'
WORKERS = int(os.environ.get('LANDESK_WORKERS', 4))  # Worker processes (gunicorn)
THREADS = int(os.environ.get('LANDESK_THREADS', 8))  # Threads per worker

# In-memory storage for devices
# Structure: {device_id: {device_info}}
# Using threading.RLock for better performance with multiple readers
devices: Dict[str, Dict] = {}
devices_lock = threading.RLock()  # RLock allows multiple readers

# Statistics
stats = {
    'total_registrations': 0,
    'total_heartbeats': 0,
    'total_discoveries': 0,
    'total_find_by_code': 0,
    'total_unregistrations': 0,
    'devices_removed_stale': 0,
    'connection_attempts': 0,  # Track connection attempts
    'start_time': datetime.now(),
    'last_cleanup': None,
    'errors': defaultdict(int),
    'recent_connection_attempts': []  # Store recent attempts for monitoring
}
stats_lock = threading.Lock()
MAX_RECENT_ATTEMPTS = 100  # Keep last 100 connection attempts

def update_stat(stat_name: str, increment: int = 1):
    """Thread-safe stat update"""
    with stats_lock:
        if stat_name not in stats:
            stats[stat_name] = 0
        if isinstance(stats[stat_name], (int, float)):
            stats[stat_name] += increment

def log_error(error_type: str, message: str):
    """Log error and update stats"""
    logger.error(f"{error_type}: {message}")
    with stats_lock:
        stats['errors'][error_type] += 1

def cleanup_stale_devices():
    """Periodically remove devices that haven't sent a heartbeat"""
    while True:
        try:
            current_time = datetime.now()
            devices_to_remove = []
            
            with devices_lock:
                for device_id, device_info in devices.items():
                    last_heartbeat = device_info.get('last_heartbeat')
                    if last_heartbeat:
                        time_diff = (current_time - last_heartbeat).total_seconds()
                        if time_diff > DEVICE_TIMEOUT:
                            devices_to_remove.append((device_id, device_info.get('device_name', 'Unknown')))
                
                for device_id, device_name in devices_to_remove:
                    del devices[device_id]
                    logger.info(f"Removed stale device: {device_id} ({device_name})")
                    update_stat('devices_removed_stale')
            
            with stats_lock:
                stats['last_cleanup'] = current_time
            
            if devices_to_remove:
                logger.info(f"Cleanup: Removed {len(devices_to_remove)} stale device(s)")
            
            time.sleep(CLEANUP_INTERVAL)
        except Exception as e:
            log_error('cleanup_error', str(e))

# Start cleanup thread
cleanup_thread = threading.Thread(target=cleanup_stale_devices, daemon=True)
cleanup_thread.start()
logger.info("Cleanup thread started")

# Request logging middleware (optimized for performance)
@app.before_request
def log_request():
    """Log all incoming requests (optimized - minimal overhead)"""
    # Only log non-heartbeat requests to reduce overhead
    if request.path != '/api/heartbeat':
        # Use debug level to avoid performance impact in production
        if logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"{request.method} {request.path} from {request.remote_addr}")

@app.after_request
def log_response(response):
    """Log response status (optimized)"""
    # Only log errors and non-heartbeat requests
    if request.path != '/api/heartbeat' and (response.status_code >= 400 or logger.isEnabledFor(logging.DEBUG)):
        logger.debug(f"{request.method} {request.path} -> {response.status_code}")
    return response

@app.route('/')
def index():
    """Server status endpoint"""
    with devices_lock:
        device_count = len(devices)
        online_count = sum(1 for d in devices.values() 
                          if d.get('last_heartbeat') and 
                          (datetime.now() - d.get('last_heartbeat', datetime.min)).total_seconds() <= DEVICE_TIMEOUT)
    
    with stats_lock:
        uptime = (datetime.now() - stats['start_time']).total_seconds()
    
    return jsonify({
        'status': 'running',
        'server': 'LanDesk Discovery Server',
        'version': '1.0.0',
        'devices_registered': device_count,
        'devices_online': online_count,
        'uptime_seconds': int(uptime),
        'server_time': datetime.now().isoformat(),
        'configuration': {
            'device_timeout': DEVICE_TIMEOUT,
            'cleanup_interval': CLEANUP_INTERVAL,
            'max_devices': MAX_DEVICES
        },
        'endpoints': {
            'register': '/api/register',
            'heartbeat': '/api/heartbeat',
            'discover': '/api/discover',
            'find_by_code': '/api/find/<pairing_code>',
            'list_devices': '/api/devices',
            'unregister': '/api/unregister',
            'health': '/api/health',
            'stats': '/api/stats'
        }
    })

@app.route('/api/health', methods=['GET'])
def health_check():
    """Health check endpoint for monitoring"""
    try:
        with devices_lock:
            device_count = len(devices)
        
        return jsonify({
            'status': 'healthy',
            'timestamp': datetime.now().isoformat(),
            'devices': device_count,
            'server_uptime_seconds': int((datetime.now() - stats['start_time']).total_seconds())
        }), 200
    except Exception as e:
        logger.error(f"Health check failed: {e}")
        return jsonify({
            'status': 'unhealthy',
            'error': str(e)
        }), 500

@app.route('/api/stats', methods=['GET'])
def get_stats():
    """Get server statistics (if enabled)"""
    if not ENABLE_STATS:
        return jsonify({'error': 'Statistics disabled'}), 403
    
    include_recent = request.args.get('include_recent_attempts', 'false').lower() == 'true'
    
    with stats_lock:
        with devices_lock:
            device_count = len(devices)
            online_count = sum(1 for d in devices.values() 
                              if d.get('last_heartbeat') and 
                              (datetime.now() - d.get('last_heartbeat', datetime.min)).total_seconds() <= DEVICE_TIMEOUT)
        
        uptime = (datetime.now() - stats['start_time']).total_seconds()
        
        response_data = {
            'status': 'success',
            'statistics': {
                'total_registrations': stats['total_registrations'],
                'total_heartbeats': stats['total_heartbeats'],
                'total_discoveries': stats['total_discoveries'],
                'total_find_by_code': stats['total_find_by_code'],
                'total_connection_attempts': stats.get('connection_attempts', 0),
                'total_unregistrations': stats['total_unregistrations'],
                'devices_removed_stale': stats['devices_removed_stale'],
                'current_devices': device_count,
                'current_online': online_count,
                'server_uptime_seconds': int(uptime),
                'last_cleanup': stats['last_cleanup'].isoformat() if stats['last_cleanup'] else None,
                'errors': dict(stats['errors'])
            },
            'timestamp': datetime.now().isoformat()
        }
        
        # Include recent connection attempts if requested
        if include_recent:
            response_data['recent_connection_attempts'] = stats.get('recent_connection_attempts', [])[-20:]  # Last 20
        
        return jsonify(response_data), 200

def validate_pairing_code(code: str) -> bool:
    """Validate pairing code format (9 digits)"""
    if not code:
        return False
    normalized = code.replace('-', '').replace(' ', '').strip()
    return len(normalized) == 9 and normalized.isdigit()

def validate_ip_address(ip: str) -> bool:
    """Basic IP address validation"""
    if not ip:
        return False
    parts = ip.split('.')
    if len(parts) != 4:
        return False
    try:
        return all(0 <= int(part) <= 255 for part in parts)
    except ValueError:
        return False

@app.route('/api/register', methods=['POST'])
def register_device():
    """
    Register a device with the server
    Expected JSON:
    {
        "device_id": "unique-device-id",
        "device_name": "Computer Name",
        "ip_address": "10.246.80.105",
        "pairing_code": "1234-5678",
        "pairing_code": "1234-5678",
        "control_port": 8530,
        "input_port": 8531,
        "discovery_port": 25536,
        "version": "1.0.0",
        "version": "1.0.0",
        "operating_system": "Windows 10"
    }
    """
    try:
        data = request.get_json()
        
        if not data:
            log_error('register_validation', 'No data provided')
            return jsonify({'error': 'No data provided'}), 400
        
        # Validate required fields
        required_fields = ['device_id', 'device_name', 'ip_address', 'pairing_code']
        for field in required_fields:
            if field not in data:
                log_error('register_validation', f'Missing required field: {field}')
                return jsonify({'error': f'Missing required field: {field}'}), 400
        
        device_id = data['device_id']
        pairing_code = data['pairing_code']
        ip_address = data['ip_address']
        
        # Validate pairing code
        if not validate_pairing_code(pairing_code):
            log_error('register_validation', f'Invalid pairing code format: {pairing_code}')
            return jsonify({'error': 'Invalid pairing code format. Must be 9 digits.'}), 400
        
        # Validate IP address
        if not validate_ip_address(ip_address):
            log_error('register_validation', f'Invalid IP address: {ip_address}')
            return jsonify({'error': 'Invalid IP address format'}), 400
        
        # Check device limit
        with devices_lock:
            if len(devices) >= MAX_DEVICES and device_id not in devices:
                log_error('register_limit', f'Maximum devices limit reached: {MAX_DEVICES}')
                return jsonify({'error': f'Maximum devices limit reached ({MAX_DEVICES})'}), 503
        
        # Normalize pairing code
        normalized_code = pairing_code.replace('-', '').replace(' ', '').upper()
        
        with devices_lock:
            is_new = device_id not in devices
            
            # If device exists, ensure pairing code doesn't change
            if not is_new:
                existing_code = devices[device_id].get('pairing_code', '').replace('-', '').replace(' ', '').upper()
                if existing_code != normalized_code:
                    logger.warning(f"Attempted to change pairing code for {device_id}. Keeping original.")
                    normalized_code = existing_code
                    pairing_code = devices[device_id].get('pairing_code', pairing_code)
            
            device_info = {
                'device_id': device_id,
                'device_name': data['device_name'],
                'ip_address': ip_address,
                'pairing_code': pairing_code,  # Keep original format
                'pairing_code_normalized': normalized_code,  # Store normalized for lookup
                'control_port': data.get('control_port', 8530),
                'input_port': data.get('input_port', 8531),
                'discovery_port': data.get('discovery_port', 25536),
                'version': data.get('version', '1.0.0'),
                'operating_system': data.get('operating_system', ''),
                'registered_at': devices[device_id].get('registered_at', datetime.now()) if not is_new else datetime.now(),
                'last_heartbeat': datetime.now()
            }
            
            devices[device_id] = device_info
        
        action = 'registered' if is_new else 'updated'
        logger.info(f"Device {action}: {device_id} ({data['device_name']}) at {ip_address} with code {pairing_code}")
        update_stat('total_registrations')
        
        return jsonify({
            'status': 'success',
            'action': action,
            'device_id': device_id,
            'message': f'Device {action} successfully'
        }), 200
        
    except Exception as e:
        log_error('register_exception', str(e))
        logger.exception("Error registering device")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/api/heartbeat', methods=['POST'])
def heartbeat():
    """
    Update device heartbeat (keeps device alive)
    Expected JSON:
    {
        "device_id": "unique-device-id",
        "ip_address": "10.246.80.105"  # Optional, updates if changed
    }
    """
    try:
        data = request.get_json()
        
        if not data or 'device_id' not in data:
            log_error('heartbeat_validation', 'Missing device_id')
            return jsonify({'error': 'Missing device_id'}), 400
        
        device_id = data['device_id']
        
        with devices_lock:
            if device_id in devices:
                devices[device_id]['last_heartbeat'] = datetime.now()
                
                # Update IP if provided and valid
                if 'ip_address' in data and data['ip_address']:
                    new_ip = data['ip_address']
                    if validate_ip_address(new_ip):
                        old_ip = devices[device_id]['ip_address']
                        if old_ip != new_ip:
                            logger.info(f"Device {device_id} IP changed: {old_ip} -> {new_ip}")
                            devices[device_id]['ip_address'] = new_ip
                    else:
                        logger.warning(f"Invalid IP address in heartbeat from {device_id}: {data['ip_address']}")
                
                update_stat('total_heartbeats')
                return jsonify({
                    'status': 'success',
                    'message': 'Heartbeat received'
                }), 200
            else:
                log_error('heartbeat_not_found', f'Device not found: {device_id}')
                return jsonify({'error': 'Device not found. Please register first.'}), 404
        
    except Exception as e:
        log_error('heartbeat_exception', str(e))
        logger.exception("Error processing heartbeat")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/api/discover', methods=['GET'])
def discover_devices():
    """
    Get list of all registered devices
    Query params:
    - exclude_device_id: Device ID to exclude from results (optional)
    """
    try:
        exclude_device_id = request.args.get('exclude_device_id')
        
        with devices_lock:
            device_list = []
            current_time = datetime.now()
            
            for device_id, device_info in devices.items():
                # Skip excluded device
                if exclude_device_id and device_id == exclude_device_id:
                    continue
                
                # Check if device is still alive
                last_heartbeat = device_info.get('last_heartbeat')
                if last_heartbeat:
                    time_diff = (current_time - last_heartbeat).total_seconds()
                    if time_diff > DEVICE_TIMEOUT:
                        continue  # Skip stale devices
                
                device_list.append({
                    'device_id': device_info['device_id'],
                    'device_name': device_info['device_name'],
                    'ip_address': device_info['ip_address'],
                    'pairing_code': device_info['pairing_code'],
                    'control_port': device_info.get('control_port', 8530),  # SCCM-allowed
                    'input_port': device_info.get('input_port', 8531),  # SCCM-allowed
                    'discovery_port': device_info.get('discovery_port', 25536),  # SCCM-allowed
                    'version': device_info.get('version', '1.0.0'),
                    'operating_system': device_info.get('operating_system', ''),
                    'last_seen': device_info['last_heartbeat'].isoformat() if device_info.get('last_heartbeat') else None
                })
        
        update_stat('total_discoveries')
        return jsonify({
            'status': 'success',
            'devices': device_list,
            'count': len(device_list)
        }), 200
        
    except Exception as e:
        log_error('discover_exception', str(e))
        logger.exception("Error discovering devices")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/api/find/<pairing_code>', methods=['GET'])
def find_by_code(pairing_code: str):
    """
    Find a device by pairing code
    Returns device information including IP address for direct connection
    This endpoint is called when a user tries to connect to another device
    """
    try:
        # Get requester info for logging
        requester_ip = request.remote_addr
        requester_device_id = request.headers.get('X-Device-ID', 'Unknown')
        
        # Normalize pairing code (remove dashes, spaces, case insensitive)
        normalized_code = pairing_code.replace('-', '').replace(' ', '').upper()
        
        if not validate_pairing_code(pairing_code):
            log_error('find_validation', f'Invalid pairing code format: {pairing_code}')
            return jsonify({'error': 'Invalid pairing code format'}), 400
        
        with devices_lock:
            current_time = datetime.now()
            
            for device_id, device_info in devices.items():
                # Use normalized code for comparison
                stored_code = device_info.get('pairing_code_normalized', 
                                             device_info.get('pairing_code', '').replace('-', '').replace(' ', '').upper())
                
                if stored_code == normalized_code:
                    # Check if device is still alive
                    last_heartbeat = device_info.get('last_heartbeat')
                    if last_heartbeat:
                        time_diff = (current_time - last_heartbeat).total_seconds()
                        if time_diff > DEVICE_TIMEOUT:
                            logger.warning(f"CONNECTION ATTEMPT: {requester_device_id} ({requester_ip}) tried to connect to {device_info['device_name']} ({device_id}) but device is OFFLINE")
                            log_error('find_offline', f'Device found but offline: {device_id}')
                            return jsonify({'error': 'Device found but is offline'}), 404
                    
                    # Log successful connection attempt
                    target_device_name = device_info['device_name']
                    target_ip = device_info['ip_address']
                    logger.info(f"🔗 CONNECTION ATTEMPT: Device '{requester_device_id}' ({requester_ip}) is trying to connect to '{target_device_name}' ({device_id}) at {target_ip}")
                    
                    # Track connection attempt
                    update_stat('total_find_by_code')
                    update_stat('connection_attempts')
                    
                    # Store recent connection attempt
                    with stats_lock:
                        attempt_info = {
                            'timestamp': current_time.isoformat(),
                            'requester_device_id': requester_device_id,
                            'requester_ip': requester_ip,
                            'target_device_id': device_id,
                            'target_device_name': target_device_name,
                            'target_ip': target_ip,
                            'pairing_code': pairing_code,
                            'status': 'success'
                        }
                        stats['recent_connection_attempts'].append(attempt_info)
                        # Keep only last N attempts
                        if len(stats['recent_connection_attempts']) > MAX_RECENT_ATTEMPTS:
                            stats['recent_connection_attempts'] = stats['recent_connection_attempts'][-MAX_RECENT_ATTEMPTS:]
                    
                    return jsonify({
                        'status': 'success',
                        'device': {
                            'device_id': device_info['device_id'],
                            'device_name': device_info['device_name'],
                            'ip_address': device_info['ip_address'],
                            'pairing_code': device_info['pairing_code'],
                            'control_port': device_info.get('control_port', 8530),
                            'input_port': device_info.get('input_port', 8531),
                            'discovery_port': device_info.get('discovery_port', 25536),
                            'version': device_info.get('version', '1.0.0'),
                            'operating_system': device_info.get('operating_system', '')
                        }
                    }), 200
        
        # Device not found
        logger.warning(f"❌ CONNECTION ATTEMPT FAILED: {requester_device_id} ({requester_ip}) tried to find device with code {pairing_code} but device NOT FOUND")
        
        # Track failed attempt
        with stats_lock:
            attempt_info = {
                'timestamp': datetime.now().isoformat(),
                'requester_device_id': requester_device_id,
                'requester_ip': requester_ip,
                'target_device_id': None,
                'target_device_name': None,
                'target_ip': None,
                'pairing_code': pairing_code,
                'status': 'not_found'
            }
            stats['recent_connection_attempts'].append(attempt_info)
            if len(stats['recent_connection_attempts']) > MAX_RECENT_ATTEMPTS:
                stats['recent_connection_attempts'] = stats['recent_connection_attempts'][-MAX_RECENT_ATTEMPTS:]
        
        log_error('find_not_found', f'Device not found with code: {pairing_code}')
        return jsonify({'error': 'Device not found'}), 404
        
    except Exception as e:
        log_error('find_exception', str(e))
        logger.exception("Error finding device")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/api/devices', methods=['GET'])
def list_devices():
    """Get detailed list of all devices (admin/debug endpoint)"""
    try:
        with devices_lock:
            device_list = []
            current_time = datetime.now()
            
            for device_id, device_info in devices.items():
                last_heartbeat = device_info.get('last_heartbeat')
                is_online = False
                if last_heartbeat:
                    time_diff = (current_time - last_heartbeat).total_seconds()
                    is_online = time_diff <= DEVICE_TIMEOUT
                
                device_list.append({
                    'device_id': device_info['device_id'],
                    'device_name': device_info['device_name'],
                    'ip_address': device_info['ip_address'],
                    'pairing_code': device_info['pairing_code'],
                    'control_port': device_info.get('control_port', 8530),  # SCCM-allowed
                    'input_port': device_info.get('input_port', 8531),  # SCCM-allowed
                    'discovery_port': device_info.get('discovery_port', 25536),  # SCCM-allowed
                    'version': device_info.get('version', '1.0.0'),
                    'operating_system': device_info.get('operating_system', ''),
                    'registered_at': device_info.get('registered_at').isoformat() if device_info.get('registered_at') else None,
                    'last_heartbeat': last_heartbeat.isoformat() if last_heartbeat else None,
                    'is_online': is_online,
                    'seconds_since_heartbeat': int((current_time - last_heartbeat).total_seconds()) if last_heartbeat else None
                })
        
        return jsonify({
            'status': 'success',
            'devices': device_list,
            'count': len(device_list),
            'server_time': datetime.now().isoformat()
        }), 200
        
    except Exception as e:
        log_error('list_devices_exception', str(e))
        logger.exception("Error listing devices")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/api/unregister', methods=['POST'])
def unregister_device():
    """
    Unregister a device from the server
    Expected JSON:
    {
        "device_id": "unique-device-id"
    }
    """
    try:
        data = request.get_json()
        
        if not data or 'device_id' not in data:
            log_error('unregister_validation', 'Missing device_id')
            return jsonify({'error': 'Missing device_id'}), 400
        
        device_id = data['device_id']
        
        with devices_lock:
            if device_id in devices:
                device_name = devices[device_id].get('device_name', 'Unknown')
                del devices[device_id]
                logger.info(f"Device unregistered: {device_id} ({device_name})")
                update_stat('total_unregistrations')
                return jsonify({
                    'status': 'success',
                    'message': 'Device unregistered successfully'
                }), 200
            else:
                log_error('unregister_not_found', f'Device not found: {device_id}')
                return jsonify({'error': 'Device not found'}), 404
        
    except Exception as e:
        log_error('unregister_exception', str(e))
        logger.exception("Error unregistering device")
        return jsonify({'error': 'Internal server error'}), 500

if __name__ == '__main__':
    # Determine port: use environment variable, command line arg, or default to 10123 (SCCM-allowed)
    # Port 80 requires admin privileges on Windows
    port = 10123  # Default (SCCM-allowed TCP)
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            logger.error(f"Invalid port number: {sys.argv[1]}. Using default port 10123.")
    elif 'LANDESK_SERVER_PORT' in os.environ:
        try:
            port = int(os.environ['LANDESK_SERVER_PORT'])
        except ValueError:
            logger.error(f"Invalid port in LANDESK_SERVER_PORT env var. Using default port 10123.")
    
    logger.info("=" * 60)
    logger.info("LanDesk Discovery Server")
    logger.info("=" * 60)
    if port == 80:
        logger.warning("Port 80 requires administrator privileges on Windows!")
        logger.warning("If you see permission errors, run as Administrator or use port 10123")
        logger.info("=" * 60)
    logger.info(f"Server starting on http://0.0.0.0:{port}")
    
    # Get and display all local IP addresses
    try:
        import socket
        local_ips = []
        
        # Method 1: Get from network interfaces using socket
        try:
            hostname = socket.gethostname()
            host_ips = socket.gethostbyname_ex(hostname)[2]
            for ip in host_ips:
                if ip not in ['127.0.0.1', '::1'] and not ip.startswith('169.254.'):  # Exclude loopback and link-local
                    local_ips.append(ip)
        except:
            pass
        
        # Method 2: Try connecting to external address to find active interface
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("8.8.8.8", 80))
            active_ip = s.getsockname()[0]
            s.close()
            if active_ip and active_ip not in local_ips and active_ip != '127.0.0.1':
                local_ips.append(active_ip)
        except:
            pass
        
        # Method 3: Use netifaces if available (more reliable)
        try:
            import netifaces
            for interface in netifaces.interfaces():
                addrs = netifaces.ifaddresses(interface)
                if netifaces.AF_INET in addrs:
                    for addr_info in addrs[netifaces.AF_INET]:
                        ip = addr_info.get('addr')
                        if ip and ip not in local_ips and ip != '127.0.0.1' and not ip.startswith('169.254.'):
                            local_ips.append(ip)
        except ImportError:
            pass
        except:
            pass
        
        # Remove duplicates and sort
        local_ips = sorted(list(set(local_ips)))
        
        if local_ips:
            logger.info("Server accessible at:")
            for ip in local_ips:
                logger.info(f"  http://{ip}:{port}")
            logger.info(f"  http://localhost:{port} (local only)")
        else:
            logger.info(f"Server accessible at: http://localhost:{port} (local only)")
            logger.info("  (Could not determine network IP addresses)")
    except Exception as e:
        logger.debug(f"Could not determine local IP addresses: {e}")
        logger.info(f"Server accessible at: http://localhost:{port} (local only)")
    
    logger.info("Configuration:")
    logger.info(f"  Device Timeout: {DEVICE_TIMEOUT}s")
    logger.info(f"  Cleanup Interval: {CLEANUP_INTERVAL}s")
    logger.info(f"  Max Devices: {MAX_DEVICES}")
    logger.info(f"  Statistics: {'Enabled' if ENABLE_STATS else 'Disabled'}")
    logger.info(f"  Production Server: {'Enabled' if USE_PRODUCTION_SERVER else 'Disabled (Development Mode)'}")
    if USE_PRODUCTION_SERVER:
        logger.info(f"  Threads: {THREADS}")
        logger.info(f"  Workers: {WORKERS} (Gunicorn only)")
    logger.info("Endpoints:")
    logger.info("  GET  /                    - Server status")
    logger.info("  GET  /api/health          - Health check")
    logger.info("  GET  /api/stats           - Server statistics")
    logger.info("  POST /api/register        - Register device")
    logger.info("  POST /api/heartbeat       - Update heartbeat")
    logger.info("  GET  /api/discover        - List all devices")
    logger.info("  GET  /api/find/<code>     - Find device by pairing code")
    logger.info("  GET  /api/devices         - List all devices (detailed)")
    logger.info("  POST /api/unregister      - Unregister device")
    logger.info("=" * 60)
    logger.info("Log file: landesk_server.log")
    logger.info("=" * 60)
    
    # Use production server if configured (variable already set at module level)
    if USE_PRODUCTION_SERVER:
        # Try to use production WSGI server
        try:
            # Try waitress first (works on Windows and Linux)
            try:
                from waitress import serve
                logger.info("Using Waitress production server")
                logger.info(f"Workers: {os.environ.get('LANDESK_WORKERS', '4')}")
                logger.info(f"Threads: {os.environ.get('LANDESK_THREADS', '8')}")
                serve(app, host='0.0.0.0', port=port, 
                      threads=int(os.environ.get('LANDESK_THREADS', 8)),
                      channel_timeout=120,
                      cleanup_interval=30,
                      asyncore_use_poll=True)
                # serve() blocks and never returns, so we never reach here
            except ImportError:
                # Fall back to gunicorn (Linux/Mac)
                try:
                    import gunicorn.app.wsgiapp as wsgi
                    logger.info("Using Gunicorn production server")
                    workers = int(os.environ.get('LANDESK_WORKERS', 4))
                    threads = int(os.environ.get('LANDESK_THREADS', 8))
                    sys.argv = ['gunicorn', 
                               '-w', str(workers),
                               '-k', 'gthread',
                               '--threads', str(threads),
                               '-b', f'0.0.0.0:{port}',
                               '--timeout', '120',
                               '--access-logfile', '-',
                               '--error-logfile', '-',
                               'landesk_server:app']
                    wsgi.run()
                    # wsgi.run() blocks and never returns, so we never reach here
                except ImportError:
                    logger.warning("Production servers not installed. Using Flask development server.")
                    logger.warning("For better performance, install: pip install waitress (Windows) or gunicorn (Linux)")
        except Exception as e:
            logger.error(f"Error starting production server: {e}")
            logger.info("Falling back to Flask development server")
    
    # Fallback to Flask development server (for development only)
    logger.warning("=" * 60)
    logger.warning("WARNING: Using Flask development server (not for production!)")
    logger.warning("For production, install waitress or gunicorn and set LANDESK_USE_PRODUCTION_SERVER=true")
    logger.warning("=" * 60)
    
    # Try to run server, with error handling for port permissions
    try:
        app.run(host='0.0.0.0', port=port, debug=False, threaded=True)
    except OSError as e:
        if port == 80 and ("permission" in str(e).lower() or "access" in str(e).lower() or "forbidden" in str(e).lower()):
            logger.error("=" * 60)
            logger.error("ERROR: Cannot bind to port 80 - Administrator privileges required!")
            logger.error("=" * 60)
            logger.error("Solutions:")
            logger.error("1. Run this script as Administrator (Right-click -> Run as Administrator)")
            logger.error("2. Use a different port (e.g., 10123):")
            logger.error(f"   python landesk_server.py 10123")
            logger.error("3. Set environment variable:")
            logger.error("   set LANDESK_SERVER_PORT=10123")
            logger.error("   python landesk_server.py")
            logger.error("=" * 60)
        else:
            logger.error(f"ERROR: Failed to start server: {e}")
        sys.exit(1)
