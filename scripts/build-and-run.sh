#!/bin/bash
# ============================================================
# FaceLocker 一键编译运行脚本 (RK3568 / Ubuntu 20.04 ARM64)
# 用途：在 RK3568 设备上自动编译并运行项目
# ============================================================

set -e

# 配置
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
APP_NAME="FaceLocker"
BUILD_DIR="$PROJECT_DIR/bin/Release/net8.0/linux-arm64"
PUBLISH_DIR="$PROJECT_DIR/publish"

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${CYAN}============================================${NC}"
echo -e "${CYAN}  FaceLocker 一键编译运行脚本${NC}"
echo -e "${CYAN}============================================${NC}"
echo ""

cd "$PROJECT_DIR"

# ============================================================
# 步骤 1: 检查 .NET SDK
# ============================================================
echo -e "${YELLOW}[1/6] 检查 .NET SDK...${NC}"
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo -e "      .NET SDK 版本: ${GREEN}$DOTNET_VERSION${NC}"
else
    echo -e "${RED}错误：未安装 .NET SDK${NC}"
    echo ""
    echo "请先安装 .NET 8.0 SDK，执行以下命令："
    echo "  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh"
    echo "  chmod +x dotnet-install.sh"
    echo "  sudo ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet"
    echo "  sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet"
    echo ""
    exit 1
fi

# ============================================================
# 步骤 2: 检查并编译原生库
# ============================================================
echo ""
echo -e "${YELLOW}[2/6] 检查并编译原生库 (v4l2_mpp_camera)...${NC}"
V4L2_LIB="$PROJECT_DIR/native/v4l2_mpp_camera/build/libv4l2_mpp_camera.so"
V4L2_SRC="$PROJECT_DIR/native/v4l2_mpp_camera"

if [ -f "$V4L2_LIB" ]; then
    echo -e "      原生库已存在: ${GREEN}$V4L2_LIB${NC}"
else
    if [ -d "$V4L2_SRC" ]; then
        echo "      正在编译 v4l2_mpp_camera..."
        cd "$V4L2_SRC/build"
        rm -rf CMakeCache.txt CMakeFiles 2>/dev/null || true
        cmake .. > /dev/null 2>&1
        make -j$(nproc)
        echo -e "      编译完成: ${GREEN}$V4L2_LIB${NC}"
        cd "$PROJECT_DIR"
    else
        echo -e "${RED}错误：未找到原生库源码目录 $V4L2_SRC${NC}"
        exit 1
    fi
fi

# 复制到系统库路径（需要 sudo）
if [ ! -f "/usr/lib/libv4l2_mpp_camera.so" ]; then
    echo "      安装原生库到系统路径（需要 sudo 权限）..."
    sudo cp "$V4L2_LIB" /usr/lib/
    sudo ldconfig
    echo -e "      原生库安装完成"
fi

# ============================================================
# 步骤 3: 还原 NuGet 包
# ============================================================
echo ""
echo -e "${YELLOW}[3/6] 还原 NuGet 包...${NC}"
dotnet restore -r linux-arm64 --verbosity quiet
echo -e "      ${GREEN}NuGet 包还原完成${NC}"

# ============================================================
# 步骤 4: 编译项目
# ============================================================
echo ""
echo -e "${YELLOW}[4/6] 编译项目...${NC}"
dotnet build -c Release -r linux-arm64 --no-restore --verbosity quiet
echo -e "      ${GREEN}编译完成${NC}"

# ============================================================
# 步骤 5: 发布项目
# ============================================================
echo ""
echo -e "${YELLOW}[5/6] 发布项目...${NC}"
rm -rf "$PUBLISH_DIR" 2>/dev/null || true
dotnet publish -c Release -r linux-arm64 --self-contained false -o "$PUBLISH_DIR" --no-build --verbosity quiet
echo -e "      发布目录: ${GREEN}$PUBLISH_DIR${NC}"

# 复制原生库到发布目录
cp "$V4L2_LIB" "$PUBLISH_DIR/"

# ============================================================
# 步骤 6: 运行应用
# ============================================================
echo ""
echo -e "${YELLOW}[6/6] 启动应用...${NC}"
echo ""
echo -e "${GREEN}============================================${NC}"
echo -e "${GREEN}  编译完成，正在启动 FaceLocker...${NC}"
echo -e "${GREEN}============================================${NC}"
echo ""

cd "$PUBLISH_DIR"

# ============================================================
# 设置动态库搜索路径
# ============================================================
export LD_LIBRARY_PATH="\
/opt/face_offline_sdk/lib/armv8:\
$PUBLISH_DIR:\
/usr/lib:\
/usr/lib/aarch64-linux-gnu:\
/usr/local/lib:\
$LD_LIBRARY_PATH"

export DISPLAY="${DISPLAY:-:0}"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# 检查关键动态库
echo ""
echo "检查动态库加载..."
LIBS_OK=1

if [ -f "$PUBLISH_DIR/libv4l2_mpp_camera.so" ]; then
    echo -e "  ${GREEN}✓${NC} libv4l2_mpp_camera.so"
else
    echo -e "  ${RED}✗${NC} libv4l2_mpp_camera.so"
    LIBS_OK=0
fi

if ldconfig -p | grep -q "librockchip_mpp.so"; then
    echo -e "  ${GREEN}✓${NC} librockchip_mpp.so"
else
    echo -e "  ${YELLOW}?${NC} librockchip_mpp.so (需运行时验证)"
fi

if ldconfig -p | grep -q "librga.so"; then
    echo -e "  ${GREEN}✓${NC} librga.so"
else
    echo -e "  ${YELLOW}?${NC} librga.so (需运行时验证)"
fi

if [ -f "/opt/face_offline_sdk/lib/armv8/libbaidu_face_api.so" ]; then
    echo -e "  ${GREEN}✓${NC} libbaidu_face_api.so"
else
    echo -e "  ${RED}✗${NC} libbaidu_face_api.so"
fi

echo ""

# 运行应用
chmod +x "$APP_NAME"
./"$APP_NAME"
