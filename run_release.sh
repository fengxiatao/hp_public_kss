#!/usr/bin/env bash
set -euo pipefail

# 一键按“部署运行”的方式启动（与你提供的命令一致）
# - 运行 Release 输出目录下的 ./FaceLocker
# - 将本目录加入 LD_LIBRARY_PATH，确保 libgst_video_player.so 等原生库能被找到
# - 可选：通过 DISPLAY 指定 X11 显示

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${ROOT_DIR}/bin/Release/net8.0"

export DISPLAY="${DISPLAY:-:0}"
export LD_LIBRARY_PATH="${APP_DIR}:${LD_LIBRARY_PATH:-}"

cd "${APP_DIR}"

LOG_FILE="${LOG_FILE:-/tmp/facelocker.app.log}"
PID_FILE="${PID_FILE:-/tmp/facelocker.app.pid}"

chmod +x ./FaceLocker 2>/dev/null || true

nohup ./FaceLocker > "${LOG_FILE}" 2>&1 &
echo $! > "${PID_FILE}"

echo "FaceLocker started."
echo "  PID: $(cat "${PID_FILE}")"
echo "  LOG: ${LOG_FILE}"

