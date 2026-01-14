using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FaceLocker.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FaceLocker.Services;

/// <summary>
/// 应用状态服务实现
/// 统一管理应用窗口状态和导航
/// </summary>
public class AppStateService : IAppStateService
{
    private readonly ILogger<AppStateService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dispatcher _dispatcher;

    #region 私有字段
    private readonly SemaphoreSlim _returnToMainWindowSemaphore = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _closeWindowSemaphore = new SemaphoreSlim(1, 1);
    private bool _isReturningToMainWindow = false;
    #endregion

    #region 属性实现
    /// <inheritdoc/>
    public IClassicDesktopStyleApplicationLifetime DesktopLifetime { get; set; }

    /// <inheritdoc/>
    public Window MainWindow => DesktopLifetime?.MainWindow;
    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化应用状态服务
    /// </summary>
    /// <param name="logger">日志服务</param>
    /// <param name="serviceProvider">服务提供者（可选）</param>
    public AppStateService(
        ILogger<AppStateService> logger,
        IServiceProvider serviceProvider = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dispatcher = Dispatcher.UIThread;

        _logger.LogInformation("AppStateService 初始化完成");
        _logger.LogInformation("服务解析方式: {ServiceProviderType}",
            _serviceProvider != null ? "注入IServiceProvider" : "静态App.GetService");
    }
    #endregion

    #region 服务解析辅助方法
    /// <summary>
    /// 获取服务实例 - 智能选择最优方式
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例</returns>
    private T GetService<T>() where T : class
    {
        // 优先使用注入的 IServiceProvider（性能最优）
        if (_serviceProvider != null)
        {
            var service = _serviceProvider.GetService<T>();
            if (service != null)
            {
                _logger.LogTrace("使用注入的IServiceProvider解析服务: {ServiceType}", typeof(T).Name);
                return service;
            }
        }

        // 备用方案：使用静态方法
        _logger.LogTrace("使用静态App.GetService解析服务: {ServiceType}", typeof(T).Name);
        return App.GetService<T>();
    }
    #endregion

    #region 返回主窗口
    /// <summary>
    /// 返回主窗口
    /// </summary>
    public void ReturnToMainWindow()
    {
        _logger.LogInformation("开始返回主窗口");

        // 防止重复调用
        if (_isReturningToMainWindow)
        {
            _logger.LogWarning("返回主窗口操作正在进行中，跳过重复调用");
            return;
        }

        try
        {
            // 使用信号量防止并发调用
            if (!_returnToMainWindowSemaphore.Wait(0))
            {
                _logger.LogWarning("返回主窗口操作正在进行中，信号量已被占用");
                return;
            }

            _isReturningToMainWindow = true;

            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ReturnToMainWindowInternal();
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ReturnToMainWindowInternal();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "返回主窗口时发生异常");
            ResetReturnToMainWindowState();
        }
    }

    /// <summary>
    /// 返回主窗口内部实现
    /// </summary>
    private void ReturnToMainWindowInternal()
    {
        try
        {
            _logger.LogInformation("开始返回主窗口内部流程");

            // 强制关闭管理员主窗口
            var adminMainWindow = DesktopLifetime?.Windows
                .OfType<AdminMainWindow>()
                .FirstOrDefault();

            if (adminMainWindow != null && adminMainWindow.IsVisible)
            {
                _logger.LogInformation("强制关闭管理员主窗口");
                adminMainWindow.Close();
            }

            // 强制关闭存放物品窗口
            var storeWindow = DesktopLifetime?.Windows
                .OfType<StoreWindow>()
                .FirstOrDefault();

            if (storeWindow != null && storeWindow.IsVisible)
            {
                _logger.LogInformation("强制关闭存放物品窗口");
                storeWindow.ForceCloseByAppState();
            }

            // 强制关闭取物窗口
            var retrieveWindow = DesktopLifetime?.Windows
                .OfType<RetrieveWindow>()
                .FirstOrDefault();

            if (retrieveWindow != null && retrieveWindow.IsVisible)
            {
                _logger.LogInformation("强制关闭取物窗口");
                retrieveWindow.ForceCloseByAppState();
            }

            // 确保主窗口显示和激活
            if (MainWindow != null)
            {
                // 重要：不要在这里反复重设 WindowState/Position/Width/Height
                // X11/WM 下重复重配窗口会造成“叠影/抖动/像多了一层”的观感。
                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.Focus();

                _logger.LogInformation("主窗口已显示并激活");
            }
            else
            {
                _logger.LogWarning("主窗口为null，无法显示和激活");
            }

            _logger.LogInformation("返回主窗口操作完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "返回主窗口内部流程发生异常");
        }
        finally
        {
            ResetReturnToMainWindowState();
        }
    }

    /// <summary>
    /// 重置返回主窗口状态
    /// </summary>
    private void ResetReturnToMainWindowState()
    {
        try
        {
            _isReturningToMainWindow = false;
            if (_returnToMainWindowSemaphore.CurrentCount == 0)
            {
                _returnToMainWindowSemaphore.Release();
            }
            _logger.LogDebug("返回主窗口状态已重置");
        }
        catch (SemaphoreFullException ex)
        {
            _logger.LogWarning(ex, "信号量已满，跳过释放操作");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重置返回主窗口状态时发生异常");
        }
    }
    #endregion

    #region 强制主窗口居中显示
    /// <summary>
    /// 强制主窗口居中显示
    /// </summary>
    private void CenterMainWindow()
    {
        try
        {
            if (MainWindow == null)
            {
                _logger.LogWarning("主窗口为null，无法居中");
                return;
            }

            _logger.LogInformation("开始强制主窗口居中");

            // 通过主窗口的 Screens 属性获取屏幕信息
            var screens = MainWindow.Screens;
            if (screens != null)
            {
                var screen = screens.Primary ?? screens.All.FirstOrDefault();
                if (screen != null)
                {
                    var screenBounds = screen.Bounds;

                    // 计算居中位置
                    var centerX = screenBounds.X + (screenBounds.Width - MainWindow.Width) / 2;
                    var centerY = screenBounds.Y + (screenBounds.Height - MainWindow.Height) / 2;

                    // 设置窗口位置
                    MainWindow.Position = new PixelPoint((int)centerX, (int)centerY);

                    _logger.LogInformation("主窗口已强制居中，屏幕尺寸: {Width}x{Height}, 窗口位置: {Position}",
                        screenBounds.Width, screenBounds.Height, MainWindow.Position);
                }
                else
                {
                    _logger.LogWarning("无法获取屏幕信息，使用默认居中");
                    MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            else
            {
                _logger.LogWarning("无法获取屏幕集合，使用默认居中");
                MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "强制主窗口居中时发生异常");
            // 备用方案
            if (MainWindow != null)
            {
                MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _logger.LogInformation("使用备用居中方案完成");
            }
        }
    }
    #endregion

    #region 显示管理主窗口
    /// <summary>
    /// 显示管理主窗口
    /// </summary>
    public void ShowAdminMainWindow()
    {
        _logger.LogInformation("显示管理主窗口");
        ShowWindow<AdminMainWindow>("管理主窗口");
    }
    #endregion

    #region 显示管理员登录窗口
    /// <summary>
    /// 显示管理员登录窗口
    /// </summary>
    public void ShowAdminLoginWindow()
    {
        _logger.LogInformation("显示管理员登录窗口");
        ShowWindow<AdminLoginWindow>("管理员登录窗口");
    }
    #endregion

    #region 显示存放物品窗口
    /// <summary>
    /// 显示存放物品窗口
    /// </summary>
    public void ShowStoreWindowAsync()
    {
        _logger.LogInformation("显示存放物品窗口");

        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ShowWindowInternal<StoreWindow>("存放物品窗口");
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ShowWindowInternal<StoreWindow>("存放物品窗口");
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示存放物品窗口时发生异常");
        }
    }
    #endregion

    #region 显示取物窗口
    /// <summary>
    /// 显示取物窗口
    /// </summary>
    public void ShowRetrieveWindowAsync()
    {
        _logger.LogInformation("显示取物窗口");

        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ShowWindowInternal<RetrieveWindow>("取物窗口");
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ShowWindowInternal<RetrieveWindow>("取物窗口");
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示取物窗口时发生异常");
        }
    }
    #endregion

    #region 关闭指定窗口
    /// <summary>
    /// 关闭指定窗口
    /// </summary>
    /// <typeparam name="TWindow">窗口类型</typeparam>
    public void CloseWindow<TWindow>() where TWindow : Window
    {
        _logger.LogInformation("关闭 {WindowType} 窗口", typeof(TWindow).Name);

        try
        {
            // 使用信号量防止并发调用
            if (!_closeWindowSemaphore.Wait(0))
            {
                _logger.LogWarning("关闭窗口操作正在进行中，信号量已被占用");
                return;
            }

            try
            {
                // 使用 UI 线程调度器确保在主线程执行
                if (_dispatcher.CheckAccess())
                {
                    CloseWindowInternal<TWindow>();
                }
                else
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        CloseWindowInternal<TWindow>();
                    });
                }
            }
            finally
            {
                try
                {
                    if (_closeWindowSemaphore.CurrentCount == 0)
                    {
                        _closeWindowSemaphore.Release();
                    }
                }
                catch (SemaphoreFullException ex)
                {
                    _logger.LogWarning(ex, "关闭窗口信号量已满，跳过释放操作");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭 {WindowType} 窗口时发生异常", typeof(TWindow).Name);
        }
    }

    /// <summary>
    /// 关闭指定窗口内部实现
    /// </summary>
    private void CloseWindowInternal<TWindow>() where TWindow : Window
    {
        try
        {
            if (DesktopLifetime == null)
            {
                _logger.LogWarning("DesktopLifetime 为 null，无法关闭窗口");
                return;
            }

            var targetWindow = DesktopLifetime.Windows
                .OfType<TWindow>()
                .FirstOrDefault();

            if (targetWindow != null)
            {
                if (targetWindow is StoreWindow storeWindow)
                {
                    storeWindow.ForceCloseByAppState();
                }
                else if (targetWindow is RetrieveWindow retrieveWindow)
                {
                    retrieveWindow.ForceCloseByAppState();
                }
                else
                {
                    targetWindow.Close();
                }
                _logger.LogDebug("已关闭窗口: {WindowType}", typeof(TWindow).Name);
            }
            else
            {
                _logger.LogDebug("未找到要关闭的窗口: {WindowType}", typeof(TWindow).Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭 {WindowType} 窗口时发生异常", typeof(TWindow).Name);
        }
    }
    #endregion

    #region 关闭所有非主窗口
    /// <summary>
    /// 关闭所有非主窗口
    /// </summary>
    public void CloseAllNonMainWindows()
    {
        _logger.LogInformation("开始关闭所有非主窗口");

        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                CloseAllNonMainWindowsInternal();
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    CloseAllNonMainWindowsInternal();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭所有非主窗口时发生异常");
        }
    }

    /// <summary>
    /// 关闭所有非主窗口内部实现
    /// </summary>
    private void CloseAllNonMainWindowsInternal()
    {
        try
        {
            if (DesktopLifetime == null)
            {
                _logger.LogWarning("DesktopLifetime 为 null，无法关闭窗口");
                return;
            }

            var windowsToClose = DesktopLifetime.Windows
                .Where(window => window != DesktopLifetime.MainWindow &&
                               window.IsVisible &&
                               window != null)
                .ToList();

            _logger.LogDebug("准备关闭 {Count} 个非主窗口", windowsToClose.Count);

            foreach (var window in windowsToClose)
            {
                try
                {
                    _logger.LogTrace("关闭窗口: {WindowType}", window.GetType().Name);

                    // 对特定窗口类型使用安全关闭方法
                    if (window is StoreWindow storeWindow)
                    {
                        storeWindow.ForceCloseByAppState();
                    }
                    else if (window is RetrieveWindow retrieveWindow)
                    {
                        retrieveWindow.ForceCloseByAppState();
                    }
                    else
                    {
                        window.Close();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭窗口时发生异常: {WindowType}", window.GetType().Name);
                }
            }

            _logger.LogInformation("所有非主窗口关闭完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭所有非主窗口时发生异常");
        }
    }
    #endregion

    #region 显示主窗口
    /// <summary>
    /// 显示主窗口
    /// </summary>
    public void ShowAndActivateMainWindow()
    {
        _logger.LogInformation("显示并激活主窗口");

        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ShowAndActivateMainWindowInternal();
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ShowAndActivateMainWindowInternal();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示并激活主窗口时发生异常");
        }
    }

    /// <summary>
    /// 显示主窗口内部实现
    /// </summary>
    private void ShowAndActivateMainWindowInternal()
    {
        try
        {
            if (DesktopLifetime?.MainWindow != null)
            {
                DesktopLifetime.MainWindow.Show();
                DesktopLifetime.MainWindow.Activate();
                DesktopLifetime.MainWindow.Focus();

                _logger.LogInformation("主窗口已显示并激活");
            }
            else
            {
                _logger.LogWarning("主窗口为 null，无法显示");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示并激活主窗口时发生异常");
        }
    }
    #endregion

    #region 窗口状态检查实现
    /// <inheritdoc/>
    public bool IsWindowOpen<TWindow>() where TWindow : Window
    {
        try
        {
            if (DesktopLifetime == null)
            {
                _logger.LogDebug("DesktopLifetime 为 null，窗口未打开");
                return false;
            }

            var isOpen = DesktopLifetime.Windows
                .OfType<TWindow>()
                .Any(window => window.IsVisible);

            _logger.LogTrace("检查窗口状态: {WindowType} = {IsOpen}", typeof(TWindow).Name, isOpen);
            return isOpen;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查窗口状态时发生异常: {WindowType}", typeof(TWindow).Name);
            return false;
        }
    }

    /// <inheritdoc/>
    public TWindow GetWindow<TWindow>() where TWindow : Window
    {
        try
        {
            if (DesktopLifetime == null)
            {
                _logger.LogDebug("DesktopLifetime 为 null，无法获取窗口");
                return null;
            }

            var window = DesktopLifetime.Windows
                .OfType<TWindow>()
                .FirstOrDefault();

            _logger.LogTrace("获取窗口实例: {WindowType} = {Found}",
                typeof(TWindow).Name, window != null ? "找到" : "未找到");
            return window;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取窗口实例时发生异常: {WindowType}", typeof(TWindow).Name);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsMainWindowFullScreen()
    {
        try
        {
            var isFullScreen = DesktopLifetime?.MainWindow?.WindowState == WindowState.FullScreen;
            _logger.LogTrace("检查主窗口全屏状态: {IsFullScreen}", isFullScreen);
            return isFullScreen;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查主窗口全屏状态时发生异常");
            return false;
        }
    }

    /// <inheritdoc/>
    public int GetOpenWindowCount()
    {
        try
        {
            if (DesktopLifetime == null)
            {
                _logger.LogDebug("DesktopLifetime 为 null，打开窗口数为 0");
                return 0;
            }

            var count = DesktopLifetime.Windows
                .Count(window => window != DesktopLifetime.MainWindow && window.IsVisible);

            _logger.LogTrace("获取打开窗口数量: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取打开窗口数量时发生异常");
            return 0;
        }
    }
    #endregion

    #region 应用状态管理实现
    /// <inheritdoc/>
    public bool IsApplicationReady()
    {
        try
        {
            var lockerService = GetService<ILockerService>();
            var lockControlService = GetService<ILockControlService>();
            var cameraService = GetService<ICameraService>();

            bool allServicesReady = lockerService != null &&
                                  lockControlService != null &&
                                  cameraService != null;

            _logger.LogDebug("应用就绪状态检查: {IsReady}", allServicesReady);

            if (!allServicesReady)
            {
                _logger.LogWarning("必要服务未就绪 - LockerService: {Locker}, LockControl: {LockControl}, Camera: {Camera}",
                    lockerService != null ? "就绪" : "缺失",
                    lockControlService != null ? "就绪" : "缺失",
                    cameraService != null ? "就绪" : "缺失");
            }

            return allServicesReady;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查应用就绪状态时发生异常");
            return false;
        }
    }

    /// <inheritdoc/>
    public string GetApplicationStatus()
    {
        try
        {
            var status = new List<string>
            {
                $"主窗口: {(MainWindow != null ? "运行中" : "未启动")}",
                $"全屏状态: {(IsMainWindowFullScreen() ? "是" : "否")}",
                $"打开窗口数: {GetOpenWindowCount()}",
                $"应用就绪: {(IsApplicationReady() ? "是" : "否")}",
                $"返回主窗口状态: {(_isReturningToMainWindow ? "进行中" : "空闲")}"
            };

            var statusString = string.Join(" | ", status);
            _logger.LogDebug("获取应用状态: {Status}", statusString);
            return statusString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取应用状态时发生异常");
            return "状态获取失败";
        }
    }

    /// <inheritdoc/>
    public void ShutdownApplication()
    {
        _logger.LogInformation("开始安全关闭应用");

        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ShutdownApplicationInternal();
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ShutdownApplicationInternal();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安全关闭应用时发生异常");
        }
    }

    /// <summary>
    /// 安全关闭应用内部实现
    /// </summary>
    private void ShutdownApplicationInternal()
    {
        try
        {
            // 关闭所有窗口
            CloseAllNonMainWindowsInternal();

            // 关闭主窗口
            if (DesktopLifetime?.MainWindow != null)
            {
                DesktopLifetime.MainWindow.Close();
                _logger.LogInformation("主窗口已关闭");
            }

            _logger.LogInformation("应用安全关闭完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安全关闭应用时发生异常");
        }
    }
    #endregion

    #region 私有辅助方法
    /// <summary>
    /// 通用窗口显示方法
    /// </summary>
    /// <typeparam name="TWindow">窗口类型</typeparam>
    /// <param name="windowName">窗口名称（用于日志）</param>
    private void ShowWindow<TWindow>(string windowName) where TWindow : Window
    {
        try
        {
            // 使用 UI 线程调度器确保在主线程执行
            if (_dispatcher.CheckAccess())
            {
                ShowWindowInternal<TWindow>(windowName);
            }
            else
            {
                _dispatcher.InvokeAsync(() =>
                {
                    ShowWindowInternal<TWindow>(windowName);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示{WindowName}时发生异常", windowName);
        }
    }

    /// <summary>
    /// 通用窗口显示内部实现
    /// </summary>
    private void ShowWindowInternal<TWindow>(string windowName) where TWindow : Window
    {
        try
        {
            var window = GetService<TWindow>();
            if (window != null)
            {
                window.Show();
                window.Activate();
                _logger.LogDebug("{WindowName}已显示并激活", windowName);
            }
            else
            {
                _logger.LogError("无法获取{WindowName}实例", windowName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示{WindowName}时发生异常", windowName);
        }
    }

    /// <summary>
    /// 人脸识别窗口关闭事件处理
    /// </summary>
    private void OnFaceRecognitionWindowClosed(object sender, EventArgs e)
    {
        _logger.LogInformation("人脸识别窗口已关闭，重新显示主窗口");

        try
        {
            if (sender is Window window)
            {
                window.Closed -= OnFaceRecognitionWindowClosed;
            }

            ShowAndActivateMainWindow();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理人脸识别窗口关闭事件时发生异常");
        }
    }
    #endregion

    #region 析构函数
    /// <summary>
    /// 析构函数，确保资源正确释放
    /// </summary>
    ~AppStateService()
    {
        try
        {
            _returnToMainWindowSemaphore?.Dispose();
            _closeWindowSemaphore?.Dispose();
            _logger.LogDebug("AppStateService 资源已释放");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppStateService 析构时发生异常");
        }
    }
    #endregion
}
