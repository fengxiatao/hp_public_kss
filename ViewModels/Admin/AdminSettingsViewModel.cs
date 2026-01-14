using FaceLocker.Models.Settings;
using FaceLocker.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace FaceLocker.ViewModels;

/// <summary>
/// 系统设置 ViewModel
/// </summary>
public class AdminSettingsViewModel : ViewModelBase, IDisposable
{
    #region 私有字段
    private readonly IAppConfigManager _appConfigManager;
    private readonly IAccessLogService _accessLogService;
    private readonly IAppStateService _appStateService;
    private readonly ILogger<AdminSettingsViewModel> _logger;
    private bool _disposed = false;
    #endregion

    #region 构造函数
    /// <summary>
    /// 构造函数
    /// </summary>
    public AdminSettingsViewModel(
        IAppConfigManager appConfigManager,
        IAccessLogService accessLogService,
        ILogger<AdminSettingsViewModel> logger,
        IAppStateService appStateService)
    {
        _appConfigManager = appConfigManager;
        _accessLogService = accessLogService;
        _logger = logger;
        _appStateService = appStateService;

        _logger.LogInformation("AdminSettingsViewModel 初始化开始");

        // 初始化命令
        InitializeCommands();

        // 加载设置数据
        _ = LoadSettingsAsync();

        _logger.LogInformation("AdminSettingsViewModel 初始化完成");
    }
    #endregion

    #region 属性定义 - 主机设置
    private string _deviceName = string.Empty;
    /// <summary>
    /// 设备名称
    /// </summary>
    public string DeviceName
    {
        get => _deviceName;
        set => this.RaiseAndSetIfChanged(ref _deviceName, value);
    }

    private string _serverAddress = string.Empty;
    /// <summary>
    /// 服务器地址
    /// </summary>
    public string ServerAddress
    {
        get => _serverAddress;
        set => this.RaiseAndSetIfChanged(ref _serverAddress, value);
    }

    private int _serverPort = 8080;
    /// <summary>
    /// 服务器端口
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set => this.RaiseAndSetIfChanged(ref _serverPort, value);
    }

    private int _checkInterval = 30;
    /// <summary>
    /// 心跳检测间隔（秒）
    /// </summary>
    public int CheckInterval
    {
        get => _checkInterval;
        set => this.RaiseAndSetIfChanged(ref _checkInterval, value);
    }
    #endregion

    #region 属性定义 - 摄像头设置
    private int _defaultCameraIndex = 0;
    /// <summary>
    /// 默认摄像头索引
    /// </summary>
    public int DefaultCameraIndex
    {
        get => _defaultCameraIndex;
        set => this.RaiseAndSetIfChanged(ref _defaultCameraIndex, value);
    }

    private string _cameraDevicePath = string.Empty;
    /// <summary>
    /// 摄像头设备路径
    /// </summary>
    public string CameraDevicePath
    {
        get => _cameraDevicePath;
        set => this.RaiseAndSetIfChanged(ref _cameraDevicePath, value);
    }

    private int _frameRate = 30;
    /// <summary>
    /// 帧率
    /// </summary>
    public int FrameRate
    {
        get => _frameRate;
        set => this.RaiseAndSetIfChanged(ref _frameRate, value);
    }

    private int _resolutionWidth = 640;
    /// <summary>
    /// 分辨率宽度
    /// </summary>
    public int ResolutionWidth
    {
        get => _resolutionWidth;
        set => this.RaiseAndSetIfChanged(ref _resolutionWidth, value);
    }

    private int _resolutionHeight = 480;
    /// <summary>
    /// 分辨率高度
    /// </summary>
    public int ResolutionHeight
    {
        get => _resolutionHeight;
        set => this.RaiseAndSetIfChanged(ref _resolutionHeight, value);
    }
    #endregion

    #region 属性定义 - 数据库设置
    private string _databaseConnectionString = string.Empty;
    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public string DatabaseConnectionString
    {
        get => _databaseConnectionString;
        set => this.RaiseAndSetIfChanged(ref _databaseConnectionString, value);
    }

    private string _dataDirectory = string.Empty;
    /// <summary>
    /// 数据目录地址
    /// </summary>
    public string DataDirectory
    {
        get => _dataDirectory;
        set => this.RaiseAndSetIfChanged(ref _dataDirectory, value);
    }
    #endregion

    #region 属性定义 - 锁控板设置
    private string _lockGroupName = "A";
    /// <summary>
    /// 柜组名称
    /// </summary>
    public string LockGroupName
    {
        get => _lockGroupName;
        set => this.RaiseAndSetIfChanged(ref _lockGroupName, value);
    }

    private string _localIpAddress = string.Empty;
    /// <summary>
    /// 本地IP地址
    /// </summary>
    public string LocalIpAddress
    {
        get => _localIpAddress;
        set => this.RaiseAndSetIfChanged(ref _localIpAddress, value);
    }

    private int _lockDirection = 0;
    /// <summary>
    /// 柜组方向
    /// </summary>
    public int LockDirection
    {
        get => _lockDirection;
        set => this.RaiseAndSetIfChanged(ref _lockDirection, value);
    }

    private int _lockBoardCount = 1;
    /// <summary>
    /// 锁控板数量
    /// </summary>
    public int LockBoardCount
    {
        get => _lockBoardCount;
        set => this.RaiseAndSetIfChanged(ref _lockBoardCount, value);
    }

    /// <summary>
    /// 串口地址
    /// </summary>
    private string _lockSerialPort = "/dev/ttyS9";
    public string LockSerialPort
    {
        get => _lockSerialPort;
        set => this.RaiseAndSetIfChanged(ref _lockSerialPort, value);
    }
    #endregion

    #region 命令定义
    /// <summary>
    /// 保存主机设置命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveServerSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// 保存摄像头设置命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveCameraSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// 保存数据库设置命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveDatabaseSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// 保存锁控板设置命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveLockControllerSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        _logger.LogDebug("初始化 AdminSettingsViewModel 命令");

        // 保存主机设置命令
        SaveServerSettingsCommand = ReactiveCommand.CreateFromTask(SaveServerSettingsAsync);

        // 保存摄像头设置命令
        SaveCameraSettingsCommand = ReactiveCommand.CreateFromTask(SaveCameraSettingsAsync);

        // 保存数据库设置命令
        SaveDatabaseSettingsCommand = ReactiveCommand.CreateFromTask(SaveDatabaseSettingsAsync);

        // 保存锁控板设置命令
        SaveLockControllerSettingsCommand = ReactiveCommand.CreateFromTask(SaveLockControllerSettingsAsync);

        _logger.LogDebug("AdminSettingsViewModel 命令初始化完成");
    }
    #endregion

    #region 私有方法
    /// <summary>
    /// 异步加载设置数据
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            _logger.LogInformation("开始加载系统设置数据");

            // 获取完整的应用程序设置
            var appSettings = await _appConfigManager.GetAppSettingsAsync();

            if (appSettings != null)
            {
                // 加载主机设置
                LoadServerSettings(appSettings.Server);

                // 加载摄像头设置
                LoadCameraSettings(appSettings.Camera);

                // 加载数据库设置
                LoadDatabaseSettings(appSettings.Database);

                // 加载锁控板设置
                LoadLockControllerSettings(appSettings.LockController);

                _logger.LogInformation("系统设置数据加载完成");
            }
            else
            {
                _logger.LogWarning("获取应用程序设置为空，使用默认设置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载系统设置数据时发生异常");
        }
    }

    /// <summary>
    /// 加载主机设置
    /// </summary>
    private void LoadServerSettings(ServerSettings settings)
    {
        try
        {
            DeviceName = settings.DeviceName;
            ServerAddress = settings.ServerAddress;
            ServerPort = settings.ServerPort;
            CheckInterval = settings.CheckInterval;

            _logger.LogDebug("主机设置加载完成: 设备名称={DeviceName}, 服务器地址={ServerAddress}, 端口={ServerPort}, 检测间隔={CheckInterval}",
                DeviceName, ServerAddress, ServerPort, CheckInterval);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载主机设置时发生异常");
        }
    }

    /// <summary>
    /// 加载摄像头设置
    /// </summary>
    private void LoadCameraSettings(CameraSettings settings)
    {
        try
        {
            DefaultCameraIndex = settings.DefaultCameraIndex;
            CameraDevicePath = settings.DevicePath;
            FrameRate = settings.FrameRate;
            ResolutionWidth = settings.Resolution?.Width ?? 640;
            ResolutionHeight = settings.Resolution?.Height ?? 480;

            _logger.LogDebug("摄像头设置加载完成: 摄像头索引={DefaultCameraIndex}, 设备路径={CameraDevicePath}, 帧率={FrameRate}, 分辨率={ResolutionWidth}x{ResolutionHeight}",
                DefaultCameraIndex, CameraDevicePath, FrameRate, ResolutionWidth, ResolutionHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载摄像头设置时发生异常");
        }
    }

    /// <summary>
    /// 加载数据库设置
    /// </summary>
    private void LoadDatabaseSettings(DatabaseSettings settings)
    {
        try
        {
            DatabaseConnectionString = settings.ConnectionString;
            DataDirectory = settings.DataDirectory;

            _logger.LogDebug("数据库设置加载完成: 连接字符串={DatabaseConnectionString}, 数据目录={DataDirectory}",
                DatabaseConnectionString, DataDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载数据库设置时发生异常");
        }
    }

    /// <summary>
    /// 加载锁控板设置
    /// </summary>
    private void LoadLockControllerSettings(LockControllerSettings settings)
    {
        try
        {
            LockGroupName = settings.GroupName;
            LocalIpAddress = settings.IPAddress;
            LockDirection = settings.Direction;
            LockBoardCount = settings.Boards?.Count ?? 0;
            var boards = settings.Boards;

            if (boards != null && boards.Any())
            {
                LockBoardCount = boards.Count;
                LockSerialPort = boards.First().SerialPort ?? "/dev/ttyS9";
            }
            else
            {
                LockBoardCount = 1;
                LockSerialPort = "/dev/ttyS9";
            }

            _logger.LogDebug("锁控板设置加载完成: 柜组名称={LockGroupName}, IP地址={LocalIpAddress}, 方向={LockDirection}, 板数量={LockBoardCount}",
                LockGroupName, LocalIpAddress, LockDirection, LockBoardCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载锁控板设置时发生异常");
        }
    }
    #endregion

    #region 命令处理方法
    /// <summary>
    /// 保存主机设置
    /// </summary>
    private async Task SaveServerSettingsAsync()
    {
        try
        {
            _logger.LogInformation("开始保存主机设置");

            var serverSettings = new ServerSettings
            {
                DeviceName = DeviceName,
                ServerAddress = ServerAddress,
                ServerPort = ServerPort,
                CheckInterval = CheckInterval
            };

            var result = await _appConfigManager.SaveServerAsync(serverSettings);

            if (result)
            {
                _logger.LogInformation("主机设置保存成功: 设备名称={DeviceName}, 服务器地址={ServerAddress}, 端口={ServerPort}, 检测间隔={CheckInterval}",
                    DeviceName, ServerAddress, ServerPort, CheckInterval);
            }
            else
            {
                _logger.LogError("主机设置保存失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存主机设置时发生异常");
        }
    }

    /// <summary>
    /// 保存摄像头设置
    /// </summary>
    private async Task SaveCameraSettingsAsync()
    {
        try
        {
            _logger.LogInformation("开始保存摄像头设置");

            var cameraSettings = new CameraSettings
            {
                DefaultCameraIndex = DefaultCameraIndex,
                DevicePath = CameraDevicePath,
                FrameRate = FrameRate,
                Resolution = new Resolution
                {
                    Width = ResolutionWidth,
                    Height = ResolutionHeight
                }
            };

            var result = await _appConfigManager.SaveCameraAsync(cameraSettings);

            if (result)
            {
                _logger.LogInformation("摄像头设置保存成功: 摄像头索引={DefaultCameraIndex}, 设备路径={CameraDevicePath}, 帧率={FrameRate}, 分辨率={ResolutionWidth}x{ResolutionHeight}",
                    DefaultCameraIndex, CameraDevicePath, FrameRate, ResolutionWidth, ResolutionHeight);
            }
            else
            {
                _logger.LogError("摄像头设置保存失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存摄像头设置时发生异常");
        }
    }

    /// <summary>
    /// 保存数据库设置
    /// </summary>
    private async Task SaveDatabaseSettingsAsync()
    {
        try
        {
            _logger.LogInformation("开始保存数据库设置");

            var databaseSettings = new DatabaseSettings
            {
                ConnectionString = DatabaseConnectionString,
                DataDirectory = DataDirectory
            };

            var result = await _appConfigManager.SaveDatabaseAsync(databaseSettings);

            if (result)
            {
                _logger.LogInformation("数据库设置保存成功: 连接字符串={DatabaseConnectionString}, 数据目录={DataDirectory}",
                    DatabaseConnectionString, DataDirectory);
            }
            else
            {
                _logger.LogError("数据库设置保存失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存数据库设置时发生异常");
        }
    }

    /// <summary>
    /// 保存锁控板设置
    /// </summary>
    private async Task SaveLockControllerSettingsAsync()
    {
        try
        {
            _logger.LogInformation("开始保存锁控板设置");

            var lockControllerSettings = new LockControllerSettings
            {
                GroupName = LockGroupName,
                IPAddress = LocalIpAddress,
                Direction = LockDirection
            };

            var boards = new List<LockBoardSettings>();
            for (int i = 1; i <= LockBoardCount; i++)
            {
                boards.Add(new LockBoardSettings
                {
                    Address = i,
                    SerialPort = LockSerialPort
                });
            }
            lockControllerSettings.Boards = boards;

            var result = await _appConfigManager.SaveLockControllerAsync(lockControllerSettings);

            if (result)
            {
                _logger.LogInformation("锁控板设置保存成功: 柜组名称={LockGroupName}, IP地址={LocalIpAddress}, 方向={LockDirection}",
                    LockGroupName, LocalIpAddress, LockDirection);
            }
            else
            {
                _logger.LogError("锁控板设置保存失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存锁控板设置时发生异常");
        }
    }
    #endregion

    #region 资源释放
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("释放 AdminSettingsViewModel 资源");

        try
        {
            // 清理命令
            SaveServerSettingsCommand?.Dispose();
            SaveCameraSettingsCommand?.Dispose();
            SaveDatabaseSettingsCommand?.Dispose();
            SaveLockControllerSettingsCommand?.Dispose();

            _disposed = true;
            _logger.LogInformation("AdminSettingsViewModel 资源释放完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放 AdminSettingsViewModel 资源时发生异常");
        }
    }
    #endregion
}