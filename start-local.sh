#!/bin/bash
# =============================================================================
# GymFlow Pro - LOCAL DEVELOPMENT STARTUP SCRIPT (macOS/Linux)
# =============================================================================
# This script starts the application locally with everything configured
# =============================================================================

echo ""
echo "╔═══════════════════════════════════════════════════════════════════╗"
echo "║                                                                   ║"
echo "║          🚀 GymFlow Pro - LOCAL DEVELOPMENT STARTUP 🚀           ║"
echo "║                                                                   ║"
echo "╚═══════════════════════════════════════════════════════════════════╝"
echo ""

# Step 1: Check prerequisites
echo "[1/3] Checking prerequisites..."

# Check .NET 8
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET 8 SDK not found. Please install it first."
    exit 1
fi
echo "✅ .NET 8 SDK found"

# Step 2: Build application
echo ""
echo "[2/3] Building application..."
dotnet build --configuration Debug > /dev/null 2>&1
if [ $? -ne 0 ]; then
    echo "❌ Build failed. Please check for errors."
    exit 1
fi
echo "✅ Build successful"

# Step 3: Start application
echo ""
echo "[3/3] Starting application..."
echo ""
echo "╔═══════════════════════════════════════════════════════════════════╗"
echo "║                                                                   ║"
echo "║                 🎉 APPLICATION STARTING 🎉                       ║"
echo "║                                                                   ║"
echo "║  🌐 API:     https://localhost:5001                              ║"
echo "║  📚 Swagger: https://localhost:5001/swagger/ui                   ║"
echo "║  ❤️  Health: https://localhost:5001/health                       ║"
echo "║                                                                   ║"
echo "║  Note: LocalDB is not available on macOS/Linux                   ║"
echo "║  Please use SQL Server in Docker or remote database              ║"
echo "║                                                                   ║"
echo "║  Press Ctrl+C to stop the application                            ║"
echo "║                                                                   ║"
echo "╚═══════════════════════════════════════════════════════════════════╝"
echo ""

cd GMS.Api
dotnet run --configuration Debug
