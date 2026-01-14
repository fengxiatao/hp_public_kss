#!/bin/bash
# ============================================================
# FaceLocker 系统依赖安装脚本 (RK3568 / Ubuntu 20.04 ARM64)
# 用途：安装编译和运行所需的系统依赖
# ============================================================

set -e

echo "============================================"
echo "  FaceLocker 系统依赖安装脚本"
echo "============================================"
echo ""

# 检查是否为 root 用户
if [ "$EUID" -ne 0 ]; then
    echo "请使用 sudo 运行此脚本"
    echo "  sudo ./install-dependencies.sh"
    exit 1
fi

echo "[1/5] 更新软件源..."
apt-get update

echo ""
echo "[2/5] 安装编译工具..."
apt-get install -y \
    build-essential \
    cmake \
    pkg-config \
    git

echo ""
echo "[3/5] 安装系统依赖库..."
apt-get install -y \
    libicu-dev \
    libssl-dev \
    libfontconfig1-dev \
    libfreetype6-dev \
    libx11-dev \
    libxcursor-dev \
    libxrandr-dev \
    libxi-dev \
    libxinerama-dev \
    libgl1-mesa-dev \
    libdrm-dev \
    libegl1-mesa-dev \
    libgbm-dev \
    libv4l-dev \
    v4l-utils

echo ""
echo "[4/5] 安装 MPP 相关依赖..."
# 检查 MPP 库是否存在
if [ -f "/usr/lib/aarch64-linux-gnu/librockchip_mpp.so" ]; then
    echo "      MPP 库已安装"
else
    echo "      警告：MPP 库未找到，请确保已安装 Rockchip MPP"
    echo "      通常 MPP 库由系统镜像预装"
fi

echo ""
echo "[5/5] 配置摄像头权限..."
CURRENT_USER=${SUDO_USER:-$USER}
usermod -aG video $CURRENT_USER 2>/dev/null || true
echo "      已将用户 $CURRENT_USER 添加到 video 组"

# 配置百度人脸 SDK 库路径
BAIDU_SDK_PATH="/opt/face_offline_sdk/lib/armv8"
if [ -d "$BAIDU_SDK_PATH" ]; then
    if ! grep -q "$BAIDU_SDK_PATH" /etc/ld.so.conf.d/facelocker.conf 2>/dev/null; then
        echo "$BAIDU_SDK_PATH" > /etc/ld.so.conf.d/facelocker.conf
        ldconfig
        echo "      已配置百度人脸 SDK 库路径"
    fi
else
    echo "      警告：百度人脸 SDK 未找到 ($BAIDU_SDK_PATH)"
fi

echo ""
echo "============================================"
echo "  依赖安装完成！"
echo "============================================"
echo ""
echo "下一步："
echo "  1. 如未安装 .NET SDK，运行: ./scripts/install-dotnet.sh"
echo "  2. 编译并运行项目: ./scripts/build-and-run.sh"
echo ""
echo "注意：请重新登录以使 video 组权限生效"
echo ""
