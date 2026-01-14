#!/bin/bash
# ============================================================
# .NET 8.0 SDK 安装脚本 (RK3568 / Ubuntu 20.04 ARM64)
# 用途：首次使用前安装 .NET SDK
# ============================================================

set -e

echo "============================================"
echo "  .NET 8.0 SDK 安装脚本"
echo "============================================"
echo ""

# 检查是否已安装
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo ".NET SDK 已安装，版本: $DOTNET_VERSION"
    echo ""
    read -p "是否重新安装？(y/N): " choice
    if [ "$choice" != "y" ] && [ "$choice" != "Y" ]; then
        echo "取消安装"
        exit 0
    fi
fi

echo "[1/4] 安装依赖..."
sudo apt-get update
sudo apt-get install -y wget libicu-dev libssl-dev

echo ""
echo "[2/4] 下载 .NET 安装脚本..."
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh

echo ""
echo "[3/4] 安装 .NET 8.0 SDK..."
sudo /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet

echo ""
echo "[4/4] 配置环境变量..."
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# 添加到 PATH
if ! grep -q "DOTNET_ROOT" ~/.bashrc; then
    echo 'export DOTNET_ROOT=/usr/share/dotnet' >> ~/.bashrc
    echo 'export PATH=$PATH:$DOTNET_ROOT' >> ~/.bashrc
fi

# 清理
rm -f /tmp/dotnet-install.sh

echo ""
echo "============================================"
echo "  安装完成！"
echo "============================================"
echo ""
echo ".NET 版本: $(dotnet --version)"
echo ""
echo "请运行以下命令使环境变量生效："
echo "  source ~/.bashrc"
echo ""
