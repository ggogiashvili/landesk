#!/bin/bash
# LanDesk Discovery Server (Production) - Linux/Mac

echo "========================================"
echo "LanDesk Discovery Server (Production)"
echo "========================================"
echo ""

# Check if Python is installed
if ! command -v python3 &> /dev/null; then
    echo "ERROR: Python 3 is not installed"
    exit 1
fi

# Check if dependencies are installed
if ! python3 -c "import flask" &> /dev/null; then
    echo "Installing dependencies..."
    pip3 install -r requirements.txt
    if [ $? -ne 0 ]; then
        echo "ERROR: Failed to install dependencies"
        exit 1
    fi
fi

# Check if gunicorn is installed
if ! python3 -c "import gunicorn" &> /dev/null; then
    echo "Installing production server (gunicorn)..."
    pip3 install gunicorn
    if [ $? -ne 0 ]; then
        echo "WARNING: Failed to install gunicorn. Server will use development mode."
        echo "For production, install gunicorn: pip3 install gunicorn"
    fi
fi

# Get port from argument or environment variable
SERVER_PORT=${1:-${LANDESK_SERVER_PORT:-8080}}

# Configuration
export LANDESK_USE_PRODUCTION_SERVER=true
export LANDESK_THREADS=8
export LANDESK_WORKERS=4

echo "Starting production server on port $SERVER_PORT..."
echo "Configuration:"
echo "  Threads per worker: $LANDESK_THREADS"
echo "  Workers: $LANDESK_WORKERS"
echo "  Production mode: Enabled"
echo ""

python3 landesk_server.py $SERVER_PORT

if [ $? -ne 0 ]; then
    echo ""
    echo "========================================"
    echo "Server failed to start!"
    echo "========================================"
    exit 1
fi
