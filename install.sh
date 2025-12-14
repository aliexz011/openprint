#!/bin/bash

# OpenPrint Installation Script
# For Ubuntu 22.04 LTS and newer

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

INSTALL_DIR="/opt/openprint"
SERVICE_USER="openprint"
LOG_DIR="/var/log/openprint"

echo -e "${GREEN}======================================${NC}"
echo -e "${GREEN}   OpenPrint Installation Script     ${NC}"
echo -e "${GREEN}======================================${NC}"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: Please run as root (sudo)${NC}"
    exit 1
fi

# Check if .NET 8 is installed
echo -e "${YELLOW}Checking .NET SDK...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${YELLOW}Installing .NET 8 SDK...${NC}"

    # Add Microsoft package repository
    wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb

    apt-get update
    apt-get install -y dotnet-sdk-8.0
else
    DOTNET_VERSION=$(dotnet --version)
    echo -e "${GREEN}Found .NET version: ${DOTNET_VERSION}${NC}"
fi

# Create service user
echo -e "${YELLOW}Creating service user...${NC}"
if ! id "$SERVICE_USER" &>/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin $SERVICE_USER
    echo -e "${GREEN}Created user: $SERVICE_USER${NC}"
else
    echo -e "${GREEN}User $SERVICE_USER already exists${NC}"
fi

# Add user to required groups for printer access
echo -e "${YELLOW}Adding user to printer groups...${NC}"
usermod -aG lp $SERVICE_USER 2>/dev/null || true
usermod -aG dialout $SERVICE_USER 2>/dev/null || true

# Create installation directory
echo -e "${YELLOW}Creating installation directory...${NC}"
mkdir -p $INSTALL_DIR
mkdir -p $LOG_DIR

# Build the application
echo -e "${YELLOW}Building OpenPrint...${NC}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

dotnet publish -c Release -r linux-x64 --self-contained true -o $INSTALL_DIR

# Copy configuration if not exists
if [ ! -f "$INSTALL_DIR/appsettings.json" ]; then
    cp appsettings.json $INSTALL_DIR/
    echo -e "${GREEN}Configuration file copied${NC}"
fi

# Set permissions
echo -e "${YELLOW}Setting permissions...${NC}"
chown -R $SERVICE_USER:$SERVICE_USER $INSTALL_DIR
chown -R $SERVICE_USER:$SERVICE_USER $LOG_DIR
chmod +x $INSTALL_DIR/OpenPrint

# Install systemd service
echo -e "${YELLOW}Installing systemd service...${NC}"
cp openprint.service /etc/systemd/system/
systemctl daemon-reload

# Enable and start service
echo -e "${YELLOW}Enabling and starting service...${NC}"
systemctl enable openprint
systemctl start openprint

# Check status
sleep 2
if systemctl is-active --quiet openprint; then
    echo ""
    echo -e "${GREEN}======================================${NC}"
    echo -e "${GREEN}   Installation Complete!            ${NC}"
    echo -e "${GREEN}======================================${NC}"
    echo ""
    echo -e "Service status: ${GREEN}Running${NC}"
    echo ""
    echo "Web interface:  http://127.0.0.1:5050/"
    echo "API endpoint:   http://127.0.0.1:5050/api/"
    echo ""
    echo "Useful commands:"
    echo "  sudo systemctl status openprint   - Check status"
    echo "  sudo systemctl restart openprint  - Restart service"
    echo "  sudo systemctl stop openprint     - Stop service"
    echo "  sudo journalctl -u openprint -f   - View logs"
    echo ""
    echo "Configuration: $INSTALL_DIR/appsettings.json"
    echo ""
else
    echo -e "${RED}Service failed to start. Check logs:${NC}"
    echo "  sudo journalctl -u openprint -n 50"
    exit 1
fi

# Test the API
echo -e "${YELLOW}Testing API...${NC}"
sleep 1
if curl -s http://127.0.0.1:5050/api/health | grep -q "healthy"; then
    echo -e "${GREEN}API is responding correctly${NC}"
else
    echo -e "${YELLOW}API test failed - service may still be starting${NC}"
fi

echo ""
echo -e "${GREEN}Done!${NC}"
