using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using FaceLocker.Services;
using FaceLocker.ViewModels;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Views;

public partial class AdminMainWindow : ReactiveWindow<AdminMainViewModel>
{
    #region 私有字段
    private readonly ILogger<AdminMainWindow> _logger;
    private bool _isClosing = false;
    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化管理员主窗口
    /// </summary>
    public AdminMainWindow()
    {
        _logger = App.GetService<ILogger<AdminMainWindow>>();
        _logger.LogInformation("AdminMainWindow 开始初始化");

        try
        {
            // 先获取服务
            var lockerService = App.GetService<ILockerService>();
            var lockControlService = App.GetService<ILockControlService>();
            var accessLogService = App.GetService<IAccessLogService>();
            var userService = App.GetService<IUserService>();
            var roleService = App.GetService<IRoleService>();
            var appConfigManager = App.GetService<IAppConfigManager>();
            var appStateService = App.GetService<IAppStateService>();

            // 获取 ViewModel 的 Logger
            var viewModelLogger = App.GetService<ILogger<AdminMainViewModel>>();
            var lockViewModelLogger = App.GetService<ILogger<AdminLockViewModel>>();
            var userViewModelLogger = App.GetService<ILogger<AdminUserViewModel>>();
            var settingsViewModelLogger = App.GetService<ILogger<AdminSettingsViewModel>>();
            var accessLogViewModelLogger = App.GetService<ILogger<AdminAccessLogViewModel>>();

            // 创建 ViewModel
            ViewModel = new AdminMainViewModel(
                lockerService,
                lockControlService,
                accessLogService,
                userService,
                roleService,
                appConfigManager,
                appStateService,
                viewModelLogger,
                lockViewModelLogger,
                userViewModelLogger,
                settingsViewModelLogger,
                accessLogViewModelLogger
            );

            // 现在初始化组件
            InitializeComponent();

            // 设置 ReactiveUI 绑定
            this.WhenActivated(disposables =>
            {
                _logger.LogInformation("AdminMainWindow WhenActivated 开始设置绑定");

                // 可以在这里添加额外的绑定或订阅
                // 命令绑定已经在 XAML 中完成

                _logger.LogInformation("AdminMainWindow 绑定设置完成");
            });

            // 设置全屏
            SetupFullscreen();

            _logger.LogInformation("AdminMainWindow 初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminMainWindow 初始化过程中发生异常");
            throw;
        }
    }
    #endregion

    #region 初始化方法
    /// <summary>
    /// 初始化组件
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _logger.LogDebug("AdminMainWindow XAML 加载完成");
    }
    #endregion

    #region 全屏设置
    /// <summary>
    /// 设置全屏模式
    /// </summary>
    private void SetupFullscreen()
    {
        _logger.LogInformation("设置管理员主窗口全屏模式");

        WindowState = WindowState.FullScreen;

        if (!OperatingSystem.IsLinux())
        {
            _logger.LogInformation("非 Linux 系统，直接设置全屏");
            return;
        }

        // Linux 系统需要特殊处理
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                _logger.LogInformation("开始 Linux 系统全屏设置");

                // 方法1: 直接设置全屏
                WindowState = WindowState.FullScreen;
                _logger.LogDebug("方法1: 直接设置全屏");

                // 方法2: 短暂延迟后再次设置
                await Task.Delay(50);
                WindowState = WindowState.FullScreen;
                _logger.LogDebug("方法2: 延迟后再次设置全屏");

                // 方法3: 先正常再全屏（触发状态变化）
                await Task.Delay(50);
                WindowState = WindowState.Normal;
                await Task.Delay(10);
                WindowState = WindowState.FullScreen;
                _logger.LogDebug("方法3: 正常->全屏切换");

                // 方法4: 设置窗口尺寸匹配屏幕
                var screen = Screens.Primary ?? Screens.All[0];
                Width = screen.Bounds.Width;
                Height = screen.Bounds.Height;
                Position = new PixelPoint(0, 0);
                _logger.LogDebug("方法4: 设置窗口尺寸匹配屏幕");

                // 方法5: 最终确认
                await Task.Delay(100);
                WindowState = WindowState.FullScreen;
                _logger.LogDebug("方法5: 最终确认全屏");

                _logger.LogInformation("Linux 系统强制全屏设置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置全屏时出错");
            }
        }, DispatcherPriority.Background);
    }
    #endregion

    #region 窗口事件处理
    /// <summary>
    /// 窗口打开时的事件处理
    /// </summary>
    /// <param name="e">事件参数</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _logger.LogInformation("AdminMainWindow 已打开");
    }

    /// <summary>
    /// 窗口关闭时的事件处理
    /// </summary>
    /// <param name="e">事件参数</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _logger.LogInformation("AdminMainWindow 已关闭");

        // 确保ViewModel资源释放
        if (ViewModel is IDisposable disposable)
        {
            disposable.Dispose();
            _logger.LogDebug("AdminMainWindow ViewModel 资源已释放");
        }
    }

    /// <summary>
    /// 窗口关闭请求处理
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isClosing)
        {
            _logger.LogWarning("窗口正在关闭中，允许关闭操作继续");
            e.Cancel = false;
            return;
        }

        _isClosing = true;
        _logger.LogInformation("AdminMainWindow：开始关闭窗口");

        try
        {
            // 不直接调用ViewModel的方法，通过命令触发
            if (ViewModel != null)
            {
                // 触发返回命令，让ViewModel处理关闭逻辑
                ViewModel.ReturnCommand.Execute().Subscribe();
                e.Cancel = true; // 让 ViewModel 处理关闭逻辑
            }
            else
            {
                _logger.LogWarning("ViewModel为空，直接关闭窗口");
                e.Cancel = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理窗口关闭时发生异常");
            e.Cancel = false; // 发生异常时直接关闭
        }

        base.OnClosing(e);
    }
    #endregion
}