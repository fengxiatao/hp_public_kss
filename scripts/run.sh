#!/bin/bash
# ============================================================
# FaceLocker 运行脚本 (RK3568 / Ubuntu 20.04 ARM64)
# 用途：直接运行已编译的应用（不重新编译）
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PUBLISH_DIR="$PROJECT_DIR/publish"
APP_NAME="FaceLocker"

# 检查是否已编译
if [ ! -f "$PUBLISH_DIR/$APP_NAME" ]; then
    echo "错误：应用未编译，请先运行 ./scripts/build-and-run.sh"
    exit 1
fi

cd "$PUBLISH_DIR"

# ============================================================
# 设置动态库搜索路径
# ============================================================
# 动态库依赖：
#   - libv4l2_mpp_camera.so  : 项目编译的 V4L2+MPP 摄像头库
#   - librockchip_mpp.so     : 瑞芯微 MPP 硬件解码器
#   - librga.so              : 瑞芯微 RGA 图像处理加速
#   - libbaidu_face_api.so   : 百度人脸离线 SDK
#   - OpenCV 相关 .so        : 图像处理（随应用发布）
# ============================================================

export LD_LIBRARY_PATH="\
/opt/face_offline_sdk/lib/armv8:\
$PUBLISH_DIR:\
/usr/lib:\
/usr/lib/aarch64-linux-gnu:\
/usr/local/lib:\
$LD_LIBRARY_PATH"

# 显示环境变量
export DISPLAY="${DISPLAY:-:0}"

# .NET 配置
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# 检查关键动态库
echo "检查动态库..."
MISSING_LIBS=0

check_lib() {
    if ldconfig -p | grep -q "$1" || [ -f "$PUBLISH_DIR/$1" ] || [ -f "/usr/lib/$1" ]; then
        echo "  ✓ $1"
    else
        echo "  ✗ $1 (未找到)"
        MISSING_LIBS=1
    fi
}

check_lib "libv4l2_mpp_camera.so"
check_lib "librockchip_mpp.so"
check_lib "librga.so"

# 检查百度 SDK
if [ -f "/opt/face_offline_sdk/lib/armv8/libbaidu_face_api.so" ]; then
    echo "  ✓ libbaidu_face_api.so"
else
    echo "  ✗ libbaidu_face_api.so (未找到)"
    MISSING_LIBS=1
fi

if [ $MISSING_LIBS -eq 1 ]; then
    echo ""
    echo "警告：部分动态库未找到，应用可能无法正常运行"
    echo "请确保已安装所有依赖"
    echo ""
fi

echo ""
echo "启动 FaceLocker..."
echo "LD_LIBRARY_PATH: $LD_LIBRARY_PATH"
echo ""

./"$APP_NAME"
