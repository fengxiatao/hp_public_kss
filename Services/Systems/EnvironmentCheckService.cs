using FaceLocker.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.IO.Ports;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 环境检查服务
    /// 提供系统运行所需环境的全面检测功能
    /// </summary>
    public class EnvironmentCheckService : IEnvironmentCheckService
    {
        #region 私有字段
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<EnvironmentCheckService> _logger;
        private readonly IOptions<AppSettings> _appSettings;
        private readonly ILockControlService _lockControlService;
        private readonly ICameraService _cameraService;
        private readonly string _databasePath;
        private DateTime _lastDatabaseCheck = DateTime.MinValue;
        private bool _lastDatabaseStatus = false;
        #endregion

        #region 公共属性
        /// <summary>
        /// 系统是否就绪
        /// </summary>
        public bool IsReady { get; private set; } = false;
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="databaseService">数据库服务</param>
        /// <param name="logger">日志服务</param>
        /// <param name="appSettings">应用设置</param>
        /// <param name="lockControlService">锁控服务</param>
        public EnvironmentCheckService(
            IDatabaseService databaseService,
            ILogger<EnvironmentCheckService> logger,
            IOptions<AppSettings> appSettings,
            ILockControlService lockControlService,
            ICameraService cameraService)
        {
            _databaseService = databaseService;
            _logger = logger;
            _appSettings = appSettings;
            _lockControlService = lockControlService;
            _cameraService = cameraService;

            var connectionString = _appSettings.Value.Database.ConnectionString ?? "Data Source=./Data/database.db";
            _databasePath = _appSettings.Value.Database.DataDirectory ?? "./Data/";
        }
        #endregion

        #region 数据库检查
        /// <summary>
        /// 检查数据库连接状态
        /// 包含缓存机制，每30秒检查一次
        /// </summary>
        /// <returns>数据库是否可用</returns>
        public async Task<bool> CheckDatabaseAsync()
        {
            try
            {
                // 添加缓存：每30秒检查一次数据库状态
                if ((DateTime.Now - _lastDatabaseCheck).TotalSeconds < 30)
                {
                    _logger.LogDebug("使用缓存的数据库状态: {Status}", _lastDatabaseStatus);
                    return _lastDatabaseStatus;
                }

                _logger.LogInformation("正在检查数据库...");

                // 检查Data目录是否存在
                var dataDirectory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
                {
                    _logger.LogInformation("创建数据库目录: {Directory}", dataDirectory);
                    Directory.CreateDirectory(dataDirectory);
                }

                // 使用 DatabaseService 检查数据库连接
                var canConnect = await _databaseService.CheckDatabaseConnectionAsync();

                if (!canConnect)
                {
                    _logger.LogError("数据库连接失败，尝试重新初始化...");
                    // 删除损坏的数据库文件并重新初始化
                    if (File.Exists(_databasePath))
                    {
                        _logger.LogWarning("删除损坏的数据库文件: {Path}", _databasePath);
                        File.Delete(_databasePath);
                    }
                    await _databaseService.InitializeDatabaseAsync();
                    _lastDatabaseStatus = true;
                    _lastDatabaseCheck = DateTime.Now;
                    _logger.LogInformation("数据库重新初始化成功");
                    return true;
                }

                _lastDatabaseStatus = true;
                _lastDatabaseCheck = DateTime.Now;
                _logger.LogInformation("数据库连接正常");
                return true;
            }
            catch (Exception ex)
            {
                _lastDatabaseStatus = false;
                _lastDatabaseCheck = DateTime.Now;
                _logger.LogError(ex, "数据库检查失败");
                return false;
            }
        }
        #endregion

        #region 摄像头检查
        /// <summary>
        /// 检查摄像头设备
        /// 支持自动检测和配置索引检测
        /// </summary>
        /// <returns>摄像头是否可用</returns>
        public async Task<bool> CheckCameraAsync()
        {
            try
            {
                _logger.LogInformation("正在检查摄像头...");

                // 使用已初始化的摄像头服务进行检查
                if (_cameraService == null)
                {
                    _logger.LogError("摄像头服务未初始化");
                    return false;
                }

                // 直接使用健康检查，不重复初始化
                var healthCheckResult = await _cameraService.CheckCameraHealthAsync();
                _logger.LogInformation("摄像头健康检查结果: {Result}", healthCheckResult ? "成功" : "失败");

                return healthCheckResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "摄像头检查失败");
                return false;
            }
        }
        #endregion

        #region 锁控板检查
        /// <summary>
        /// 检查锁控板连接状态
        /// 检查物理串口和初始化状态
        /// </summary>
        /// <returns>锁控板是否可用</returns>
        public async Task<bool> CheckLockControlBoardAsync()
        {
            try
            {
                _logger.LogInformation("正在检查锁控板连接...");

                // 1. 检查物理串口枚举
                var availablePorts = SerialPort.GetPortNames();
                if (availablePorts.Length == 0)
                {
                    _logger.LogWarning("未发现可用的串口设备");
                    return false;
                }
                _logger.LogInformation("系统检测到 {Count} 个串口: {Ports}", availablePorts.Length, string.Join(", ", availablePorts));

                // 2. 调用底层初始化检查所有板子
                var initResult = await _lockControlService.InitializeLockControlAsync();
                if (initResult)
                {
                    _logger.LogInformation("锁控板串口检查全部成功");
                    return true;
                }
                else
                {
                    _logger.LogError("部分锁控板串口初始化失败，请检查物理连接与配置");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "锁控板检查失败");
                return false;
            }
        }
        #endregion

        #region 百度人脸识别SDK检查
        /// <summary>
        /// 检查百度人脸识别SDK环境
        /// 验证配置文件、许可证文件和模型目录
        /// </summary>
        /// <returns>SDK环境是否正常</returns>
        public async Task<bool> CheckBaiduFaceSDKAsync()
        {
            _logger.LogInformation("正在检查百度人脸识别SDK...");

            // 百度AI SDK路径
            var _modelPath = _appSettings.Value.BaiduFace.ModelPath;

            // 如果没有指定配置路径，自动检测百度SDK根路径
            if (string.IsNullOrEmpty(_modelPath))
            {
                // 尝试多个可能的SDK路径
                string[] possiblePaths = [
                    "/home/orangepi/soft/face_offline_sdk",
                    "/opt/face_offline_sdk",
                    "/usr/local/face_offline_sdk"
                ];

                foreach (string path in possiblePaths)
                {
                    string testFaceIniPath = Path.Combine(path, "face.ini");
                    if (File.Exists(testFaceIniPath))
                    {
                        _modelPath = path;
                        break;
                    }
                }

                // 如果都没找到，使用默认路径
                if (string.IsNullOrEmpty(_modelPath))
                {
                    _modelPath = "/opt/face_offline_sdk";
                    _logger.LogError("未找到有效的SDK路径，使用默认路径: {Path}", _modelPath);
                }
            }
            _logger.LogInformation("自动检测到SDK路径: {Path}", _modelPath);

            // 检查face.ini配置文件是否存在
            string faceIniPath = Path.Combine(_modelPath, "face.ini");
            if (!File.Exists(faceIniPath))
            {
                _logger.LogError("face.ini配置文件不存在: {Path}", faceIniPath);
                _logger.LogError("请确保已正确部署face.ini配置文件到SDK根目录");
                return await Task.FromResult(false);
            }

            // 检查license文件是否存在
            string licenseIniPath = Path.Combine(_modelPath, "license", "license.ini");
            string licenseKeyPath = Path.Combine(_modelPath, "license", "license.key");

            if (!File.Exists(licenseIniPath))
            {
                _logger.LogError("license.ini文件不存在: {Path}", licenseIniPath);
                _logger.LogError("请确保已正确部署license.ini文件到SDK的license目录");
                return await Task.FromResult(false);
            }

            if (!File.Exists(licenseKeyPath))
            {
                _logger.LogError("license.key文件不存在: {Path}", licenseKeyPath);
                _logger.LogError("请确保已正确部署license.key文件到SDK的license目录");
                return await Task.FromResult(false);
            }

            var licenseIniInfo = new FileInfo(licenseIniPath);
            var licenseKeyInfo = new FileInfo(licenseKeyPath);
            _logger.LogInformation("license.ini文件存在 - 路径: {Path}, 大小: {Size} 字节", licenseIniPath, licenseIniInfo.Length);
            _logger.LogInformation("license.key文件存在 - 路径: {Path}, 大小: {Size} 字节", licenseKeyPath, licenseKeyInfo.Length);

            // 检查models目录是否存在
            string modelsPath = Path.Combine(_modelPath, "models");
            if (!Directory.Exists(modelsPath))
            {
                _logger.LogError("models目录不存在: {Path}", modelsPath);
                _logger.LogError("请确保已正确部署models目录到SDK根目录");
                return await Task.FromResult(false);
            }

            // 检查关键模型子目录是否存在
            string[] requiredModelDirs = { "align", "detect", "feature", "silent_live" };
            foreach (string modelDir in requiredModelDirs)
            {
                string modelDirPath = Path.Combine(modelsPath, modelDir);
                if (!Directory.Exists(modelDirPath))
                {
                    _logger.LogWarning("模型目录不存在: {Path}", modelDirPath);
                }
                else
                {
                    _logger.LogInformation("模型目录存在: {Path}", modelDirPath);
                }
            }

            // 记录models目录内容
            try
            {
                var modelsDirInfo = new DirectoryInfo(modelsPath);
                var subDirs = modelsDirInfo.GetDirectories();
                var files = modelsDirInfo.GetFiles();
                _logger.LogInformation("models目录内容 - 子目录数: {DirCount}, 文件数: {FileCount}", subDirs.Length, files.Length);

                foreach (var subDir in subDirs)
                {
                    _logger.LogDebug("发现模型子目录: {Name}", subDir.Name);
                }

                foreach (var file in files)
                {
                    _logger.LogDebug("发现模型文件: {Name} (大小: {Size} 字节)", file.Name, file.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("无法获取models目录信息: {Message}", ex.Message);
            }

            _logger.LogInformation("百度人脸识别SDK检查完成");
            return await Task.FromResult(true);
        }
        #endregion

        #region 全面系统检测
        /// <summary>
        /// 全面检测系统状态
        /// 执行所有环境检查并返回综合结果
        /// </summary>
        /// <returns>系统检查结果</returns>
        public async Task<SystemCheckResult> RunFullCheckAsync()
        {
            var result = new SystemCheckResult();

            _logger.LogInformation("开始全面系统环境检查...");
            // 数据库检查
            result.DatabaseStatus = await CheckDatabaseAsync();
            // 摄像头检查
            result.CameraStatus = await CheckCameraAsync();
            // 锁控板检查
            result.LockControlBoardStatus = await CheckLockControlBoardAsync();
            // 百度人脸SDK检查
            result.BaiduSDKStatus = await CheckBaiduFaceSDKAsync();

            result.OverallStatus = result.DatabaseStatus && result.CameraStatus && result.LockControlBoardStatus && result.BaiduSDKStatus;

            result.CheckTime = DateTime.Now;

            IsReady = result.OverallStatus;

            if (IsReady)
            {
                _logger.LogInformation("环境检测全部通过");
            }
            else
            {
                _logger.LogError("环境检测失败，请检查详细错误");
            }

            _logger.LogInformation("系统检查结果: {Summary}", result.GetStatusSummary());

            return result;
        }
        #endregion
    }

    #region 系统检查结果
    /// <summary>
    /// 系统检查结果
    /// 包含各项环境检查的状态和总体状态
    /// </summary>
    public class SystemCheckResult
    {
        /// <summary>
        /// 总体状态
        /// </summary>
        public bool OverallStatus { get; set; }

        /// <summary>
        /// 数据库状态
        /// </summary>
        public bool DatabaseStatus { get; set; }

        /// <summary>
        /// 摄像头状态
        /// </summary>
        public bool CameraStatus { get; set; }

        /// <summary>
        /// 锁控板状态
        /// </summary>
        public bool LockControlBoardStatus { get; set; }

        /// <summary>
        /// 百度SDK状态
        /// </summary>
        public bool BaiduSDKStatus { get; set; }

        /// <summary>
        /// 检查时间
        /// </summary>
        public DateTime CheckTime { get; set; }

        /// <summary>
        /// 获取状态摘要
        /// </summary>
        /// <returns>状态摘要字符串</returns>
        public string GetStatusSummary()
        {
            var passedCount = 0;
            var totalCount = 4; // Database, Camera, LockControlBoard, BaiduSDK

            if (DatabaseStatus) passedCount++;
            if (CameraStatus) passedCount++;
            if (LockControlBoardStatus) passedCount++;
            if (BaiduSDKStatus) passedCount++;

            return $"系统检查完成: {passedCount}/{totalCount} 项通过";
        }
    }
    #endregion
}