#!/bin/bash

# Readarr Metadata Service Startup Script

echo "==================================="
echo "Readarr Metadata Service"
echo "==================================="

# Check if Python 3 is available
if ! command -v python3 &> /dev/null; then
    echo "❌ Python 3 is not installed. Please install Python 3.7 or later."
    exit 1
fi

# Check if we're in the right directory
if [ ! -f "app.py" ]; then
    echo "❌ app.py not found. Please run this script from the metadata_service directory."
    exit 1
fi

# Check if virtual environment exists
if [ ! -d "venv" ]; then
    echo "📦 Creating virtual environment..."
    python3 -m venv venv
    if [ $? -ne 0 ]; then
        echo "❌ Failed to create virtual environment"
        exit 1
    fi
fi

# Activate virtual environment
echo "🔧 Activating virtual environment..."
source venv/bin/activate

# Install/update dependencies
echo "📥 Installing dependencies..."
pip install --quiet -r requirements.txt
if [ $? -ne 0 ]; then
    echo "❌ Failed to install dependencies"
    exit 1
fi

echo "✅ Setup complete!"
echo ""
echo "🚀 Starting Readarr Metadata Service..."
echo "   Service will be available at: http://localhost:8787"
echo "   Configure Readarr to use this URL as Metadata Provider Source"
echo ""
echo "   Press Ctrl+C to stop the service"
echo ""

# Start the service
python app.py