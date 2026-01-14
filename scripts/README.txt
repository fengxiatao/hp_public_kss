============================================================
  FaceLocker 人脸识别储物柜系统
  RK3568 / Ubuntu 20.04 ARM64 部署指南
============================================================

【系统要求】
- 硬件：RK3568 开发板
- 系统：Ubuntu 20.04 ARM64
- 已安装百度人脸离线 SDK（/opt/face_offline_sdk/）

【快速开始】

1. 解压项目文件：
   tar -xzvf facelocker_project.tar.gz
   cd kss

2. 首次部署 - 安装系统依赖（需要 sudo）：
   sudo ./scripts/install-dependencies.sh

3. 安装 .NET 8.0 SDK（如未安装）：
   ./scripts/install-dotnet.sh
   source ~/.bashrc

4. 一键编译并运行：
   ./scripts/build-and-run.sh

【脚本说明】

scripts/
├── install-dependencies.sh  # 安装系统依赖（首次运行）
├── install-dotnet.sh        # 安装 .NET 8.0 SDK
├── build-and-run.sh         # 一键编译并运行
├── run.sh                   # 仅运行（不重新编译）
└── README.txt               # 本说明文件

【常用操作】

- 仅运行（不重新编译）：
  ./scripts/run.sh

- 重新编译并运行：
  ./scripts/build-and-run.sh

- 后台运行：
  nohup ./scripts/run.sh > /dev/null 2>&1 &

【故障排除】

问题：提示 "libv4l2_mpp_camera.so: cannot open shared object file"
解决：运行 sudo ldconfig

问题：提示摄像头权限不足
解决：运行 sudo usermod -aG video $USER 然后重新登录

问题：提示百度人脸 SDK 初始化失败
解决：检查 /opt/face_offline_sdk/ 是否存在

问题：显示黑屏或无法启动 GUI
解决：确保 DISPLAY 环境变量正确，可尝试 export DISPLAY=:0

【联系支持】
如有问题，请联系技术支持。

============================================================
