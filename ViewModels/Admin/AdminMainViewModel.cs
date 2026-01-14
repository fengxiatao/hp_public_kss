using FaceLocker.Services;
using FaceLocker.Views;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Reactive;
using System.Threading;

namespace FaceLocker.ViewModels;

/// <summary>
/// 管理员主窗口 ViewModel
/// </summary>
public class AdminMainViewModel : ViewModelBase, IDisposable
{
    #region 私有字段
    private readonly ILogger<AdminMainViewModel> _logger;
    private readonly IAppStateService _appStateService;
    private readonly AdminLockViewModel _lockViewModel;
    private readonly AdminUserViewModel _userViewModel;
    private readonly AdminSettingsViewModel _settingsViewModel;
    private readonly AdminAccessLogViewModel _accessLogViewModel;
    private readonly Timer _statusTimer;
    private bool _disposed = false;
    #endregion

    #region 构造函数
    /// <summary>
    /// 构造函数
    /// </summary>
    public AdminMainViewModel(
        ILockerService lockerService,
        ILockControlService lockControlService,
        IAccessLogService accessLogService,
        IUserService userService,
        IRoleService roleService,
        IAppConfigManager appConfigManager,
        IAppStateService appStateService,
        ILogger<AdminMainViewModel> logger,
        ILogger<AdminLockViewModel> lockViewModelLogger,
        ILogger<AdminUserViewModel> userViewModelLogger,
        ILogger<AdminSettingsViewModel> settingsViewModelLogger,
        ILogger<AdminAccessLogViewModel> accessLogViewModelLogger)
    {
        _logger = logger;
        _appStateService = appStateService;

        _logger.LogInformation("AdminMainViewModel 初始化开始");

        // 初始化子 ViewModel
        _lockViewModel = new AdminLockViewModel(
            lockerService,
            lockControlService,
            accessLogService,
            lockViewModelLogger,
            appStateService
        );

        _userViewModel = new AdminUserViewModel(            
            userViewModelLogger
        );

        _settingsViewModel = new AdminSettingsViewModel(
            appConfigManager,
            accessLogService,
            settingsViewModelLogger,
            appStateService
        );

        _accessLogViewModel = new AdminAccessLogViewModel(
            accessLogService,
            accessLogViewModelLogger
        );

        // 初始化命令
        InitializeCommands();

        // 设置默认选项卡和内容
        CurrentTab = "Lockers";
        UpdateCurrentContent();
        UpdateTabSelection();

        // 初始化状态栏计时器
        _statusTimer = new Timer(UpdateStatusTime, null, 0, 1000);

        _logger.LogInformation("AdminMainViewModel 初始化完成，当前选项卡: {CurrentTab}", CurrentTab);
    }
    #endregion

    #region 属性定义
    private string _currentTab = "Lockers";
    /// <summary>
    /// 当前选中的选项卡
    /// </summary>
    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentTab, value);
            UpdateCurrentContent(); // 选项卡变化时更新内容
            UpdateTabSelection(); // 更新选项卡选中状态
        }
    }

    private bool _isLockersSelected = true;
    /// <summary>
    /// 锁柜管理选项卡是否选中
    /// </summary>
    public bool IsLockersSelected
    {
        get => _isLockersSelected;
        set => this.RaiseAndSetIfChanged(ref _isLockersSelected, value);
    }

    private bool _isAccessLogsSelected = false;
    /// <summary>
    /// 开锁日志选项卡是否选中
    /// </summary>
    public bool IsAccessLogsSelected
    {
        get => _isAccessLogsSelected;
        set => this.RaiseAndSetIfChanged(ref _isAccessLogsSelected, value);
    }

    private bool _isUsersSelected = false;
    /// <summary>
    /// 用户管理选项卡是否选中
    /// </summary>
    public bool IsUsersSelected
    {
        get => _isUsersSelected;
        set => this.RaiseAndSetIfChanged(ref _isUsersSelected, value);
    }

    private bool _isSettingsSelected = false;
    /// <summary>
    /// 系统设置选项卡是否选中
    /// </summary>
    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        set => this.RaiseAndSetIfChanged(ref _isSettingsSelected, value);
    }

    private object _currentContent;
    /// <summary>
    /// 当前显示的内容
    /// </summary>
    public object CurrentContent
    {
        get => _currentContent;
        set => this.RaiseAndSetIfChanged(ref _currentContent, value);
    }

    private string _currentTime = string.Empty;
    /// <summary>
    /// 当前时间
    /// </summary>
    public string CurrentTime
    {
        get => _currentTime;
        set => this.RaiseAndSetIfChanged(ref _currentTime, value);
    }

    private string _currentDate = string.Empty;
    /// <summary>
    /// 当前日期
    /// </summary>
    public string CurrentDate
    {
        get => _currentDate;
        set => this.RaiseAndSetIfChanged(ref _currentDate, value);
    }

    /// <summary>
    /// 锁柜管理 ViewModel
    /// </summary>
    public AdminLockViewModel LockViewModel => _lockViewModel;

    /// <summary>
    /// 开锁日志 ViewModel
    /// </summary>
    public AdminAccessLogViewModel AccessLogViewModel => _accessLogViewModel;

    /// <summary>
    /// 用户管理 ViewModel
    /// </summary>
    public AdminUserViewModel UserViewModel => _userViewModel;

    /// <summary>
    /// 系统设置 ViewModel
    /// </summary>
    public AdminSettingsViewModel SettingsViewModel => _settingsViewModel;
    #endregion

    #region 命令定义
    /// <summary>
    /// 切换到锁柜管理命令
    /// </summary>
    public ReactiveCommand<string, Unit> SwitchToLockersCommand { get; private set; } = null!;

    /// <summary>
    /// 切换到开锁日志命令
    /// </summary>
    public ReactiveCommand<string, Unit> SwitchToAccessLogsCommand { get; private set; } = null!;

    /// <summary>
    /// 切换到用户管理命令
    /// </summary>
    public ReactiveCommand<string, Unit> SwitchToUsersCommand { get; private set; } = null!;

    /// <summary>
    /// 切换到系统设置命令
    /// </summary>
    public ReactiveCommand<string, Unit> SwitchToSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// 返回命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ReturnCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        _logger.LogDebug("初始化 AdminMainViewModel 命令");

        // 选项卡切换命令
        SwitchToLockersCommand = ReactiveCommand.Create<string>(SwitchToTab);
        SwitchToAccessLogsCommand = ReactiveCommand.Create<string>(SwitchToTab);
        SwitchToUsersCommand = ReactiveCommand.Create<string>(SwitchToTab);
        SwitchToSettingsCommand = ReactiveCommand.Create<string>(SwitchToTab);

        // 返回命令
        ReturnCommand = ReactiveCommand.Create(ReturnToMain);

        // 关闭命令
        CloseCommand = ReactiveCommand.Create(CloseWindow);

        _logger.LogDebug("AdminMainViewModel 命令初始化完成");
    }
    #endregion

    #region 私有方法
    /// <summary>
    /// 根据当前选项卡更新显示内容
    /// </summary>
    private void UpdateCurrentContent()
    {
        _logger.LogInformation("更新显示内容为: {CurrentTab}", CurrentTab);

        switch (CurrentTab)
        {
            case "Lockers":
                CurrentContent = _lockViewModel;
                break;
            case "AccessLogs":
                CurrentContent = _accessLogViewModel;
                break;
            case "Users":
                CurrentContent = _userViewModel;
                break;
            case "Settings":
                CurrentContent = _settingsViewModel;
                break;
            default:
                CurrentContent = _lockViewModel;
                break;
        }
    }

    /// <summary>
    /// 更新选项卡选中状态
    /// </summary>
    private void UpdateTabSelection()
    {
        _logger.LogDebug("更新选项卡选中状态，当前选项卡: {CurrentTab}", CurrentTab);

        IsLockersSelected = CurrentTab == "Lockers";
        IsAccessLogsSelected = CurrentTab == "AccessLogs";
        IsUsersSelected = CurrentTab == "Users";
        IsSettingsSelected = CurrentTab == "Settings";

        _logger.LogDebug("选项卡选中状态 - 锁柜管理: {IsLockersSelected}, 用户管理: {IsUsersSelected}, 系统设置: {IsSettingsSelected}",
            IsLockersSelected, IsUsersSelected, IsSettingsSelected);
    }

    /// <summary>
    /// 更新状态栏时间显示
    /// </summary>
    private void UpdateStatusTime(object? state = null)
    {
        try
        {
            var now = DateTime.Now;
            CurrentTime = now.ToString("HH:mm:ss");
            CurrentDate = now.ToString("yyyy-MM-dd");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新状态栏时间时发生异常");
        }
    }
    #endregion

    #region 命令处理方法
    /// <summary>
    /// 切换选项卡
    /// </summary>
    /// <param name="tabName">选项卡名称</param>
    private void SwitchToTab(string tabName)
    {
        _logger.LogInformation("切换选项卡: {TabName}", tabName);
        CurrentTab = tabName;
    }

    /// <summary>
    /// 返回主界面
    /// </summary>
    private void ReturnToMain()
    {
        _logger.LogInformation("开始返回主界面操作");

        try
        {
            // 通过应用状态服务返回主界面
            if (_appStateService != null)
            {
                _logger.LogInformation("通过AppStateService返回主界面");

                // 先返回主窗口，这会关闭所有非主窗口
                _appStateService.ReturnToMainWindow();

                // 然后显式关闭当前管理窗口，确保窗口被正确关闭
                _logger.LogInformation("显式关闭管理员主窗口");
                _appStateService.CloseWindow<AdminMainWindow>();
            }
            else
            {
                _logger.LogWarning("AppStateService为空，尝试直接操作窗口");
                // 备用方案可以在需要时实现
            }

            _logger.LogInformation("返回主界面操作完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "返回主界面时发生异常");
        }
    }

    /// <summary>
    /// 关闭窗口
    /// </summary>
    private void CloseWindow()
    {
        _logger.LogInformation("开始关闭窗口操作");

        try
        {
            // 通过应用状态服务关闭窗口
            if (_appStateService != null)
            {
                _logger.LogInformation("通过AppStateService关闭管理员主窗口");
                _appStateService.CloseWindow<AdminMainWindow>();
            }
            else
            {
                _logger.LogWarning("AppStateService为空，无法正常关闭窗口");
            }

            _logger.LogInformation("关闭窗口操作完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭窗口时发生异常");
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

        _logger.LogInformation("释放 AdminMainViewModel 资源");

        try
        {
            // 停止计时器
            _statusTimer?.Dispose();

            // 清理子 ViewModel 资源
            _lockViewModel?.Dispose();
            _accessLogViewModel?.Dispose();
            _userViewModel?.Dispose();
            _settingsViewModel?.Dispose();
            

            // 清理命令
            SwitchToLockersCommand?.Dispose();
            SwitchToAccessLogsCommand?.Dispose();
            SwitchToUsersCommand?.Dispose();
            SwitchToSettingsCommand?.Dispose();
            ReturnCommand?.Dispose();
            CloseCommand?.Dispose();

            _disposed = true;
            _logger.LogInformation("AdminMainViewModel 资源释放完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放 AdminMainViewModel 资源时发生异常");
        }
    }
    #endregion
}