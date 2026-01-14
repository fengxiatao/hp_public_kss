#!/bin/bash
#
# 构建 V4L2+MPP 摄像头库和 GStreamer 视频播放器库
# 
# 依赖：
# - Rockchip MPP 库
# - RGA 库（可选，用于硬件加速）
# - GStreamer 1.0 开发库
#
# 安装依赖（Ubuntu/Debian）:
#   sudo apt-get install -y \
#       libgstreamer1.0-dev \
#       libgstreamer-plugins-base1.0-dev \
#       gstreamer1.0-plugins-base \
#       gstreamer1.0-plugins-good \
#       gstreamer1.0-gl \
#       gstreamer1.0-rockchip
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build"

echo "=========================================="
echo "构建 V4L2+MPP 和 GStreamer 视频播放器库"
echo "=========================================="

# 创建构建目录
mkdir -p "${BUILD_DIR}"
cd "${BUILD_DIR}"

# 运行 CMake
echo ""
echo ">>> 运行 CMake 配置..."
cmake .. -DCMAKE_BUILD_TYPE=Release

# 编译
echo ""
echo ">>> 编译中..."
make -j$(nproc)

echo ""
echo "=========================================="
echo "构建完成！"
echo "=========================================="
echo ""
echo "生成的库文件："
echo "  ${BUILD_DIR}/libv4l2_mpp_camera.so  - V4L2+MPP 摄像头库"
echo "  ${BUILD_DIR}/libgst_video_player.so - GStreamer 视频播放器库"
echo ""
echo "安装到系统（需要 sudo）："
echo "  sudo cp ${BUILD_DIR}/lib*.so /usr/lib/"
echo ""
echo "或者复制到应用程序目录："
echo "  cp ${BUILD_DIR}/lib*.so /path/to/your/app/"
echo ""

# 询问是否安装
read -p "是否安装到 /usr/lib/？(y/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "安装中..."
    sudo cp "${BUILD_DIR}/libv4l2_mpp_camera.so" /usr/lib/
    sudo cp "${BUILD_DIR}/libgst_video_player.so" /usr/lib/
    sudo ldconfig
    echo "安装完成！"
fi
