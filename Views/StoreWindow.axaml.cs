using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaceLocker.Services;
using FaceLocker.ViewModels;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views.Controls;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Views
{
    /// <summary>
    /// 存放物品窗口
    /// </summary>
    public partial class StoreWindow : ReactiveWindow<StoreWindowViewModel>
    {
        #region 私有字段
        private readonly ILogger<StoreWindow> _logger;
        private readonly IAppStateService _appStateService;
        private bool _isInitialized = false;
        private bool _isForceClosingByAppState = false;
        private bool _isReturnButtonClicked = false;
        private readonly SemaphoreSlim _closingSemaphore = new SemaphoreSlim(1, 1);
        private bool _isDisposed = false;
        
        // 原生视频显示控件
        private NativeVideoHost? _nativeVideoHost;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化存放物品窗口
        /// </summary>
        public StoreWindow()
        {
            _logger = App.GetService<ILogger<StoreWindow>>();
            _appStateService = App.GetService<IAppStateService>();

            _logger.LogInformation("StoreWindow：开始创建存放物品窗口");

            try
            {
                // 创建ViewModel（通过DI容器）
                ViewModel = new StoreWindowViewModel(
                    App.GetService<ILogger<StoreWindowViewModel>>(),
                    App.GetService<NumPadDialogViewModel>(),
                    App.GetService<ILoggerFactory>(),
                    App.GetService<ICameraService>(),
                    App.GetService<ILockerService>(),
                    App.GetService<IUserService>(),
                    _appStateService,
                    App.GetService<ILockControlService>(),
                    App.GetService<IAccessLogService>(),
                    App.GetService<BaiduFaceService>(),
                    App.GetService<FaceLockerDbContext>()
                );

                // 初始化组件
                InitializeComponent();

                // 设置ReactiveUI绑定和激活逻辑
                this.WhenActivated(disposables =>
                {
                    _logger.LogInformation("StoreWindow WhenActivated 开始设置绑定");

                    // 注册窗口关闭时的清理
                    disposables.Add(Disposable.Create(() =>
                    {
                        _logger.LogDebug("StoreWindow 激活状态结束，执行清理");
                        CleanupResources();
                    }));

                    _logger.LogInformation("StoreWindow 绑定设置完成");
                });

                _isInitialized = true;
                _logger.LogInformation("StoreWindow：存放物品窗口初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreWindow 初始化过程中发生异常");
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
            try
            {
                AvaloniaXamlLoader.Load(this);
                _logger.LogDebug("StoreWindow XAML 加载完成");
                
                // 获取 NativeVideoHost 控件引用
                _nativeVideoHost = this.FindControl<NativeVideoHost>("NativeVideoHost");
                if (_nativeVideoHost != null)
                {
                    _logger.LogInformation("找到 NativeVideoHost 控件，注册窗口句柄就绪事件");
                    _nativeVideoHost.WindowHandleReady += OnNativeVideoWindowHandleReady;
                }
                else
                {
                    _logger.LogWarning("未找到 NativeVideoHost 控件，将使用软件渲染模式");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreWindow XAML 加载失败");
                throw;
            }
        }
        
        /// <summary>
        /// 原生视频窗口句柄就绪处理
        /// </summary>
        private void OnNativeVideoWindowHandleReady(object? sender, WindowHandleReadyEventArgs e)
        {
            _logger.LogInformation("NativeVideoHost 窗口句柄就绪: 0x{Handle:X}", e.X11WindowId);
            
            // 通知 ViewModel 窗口句柄已就绪
            if (ViewModel != null)
            {
                _ = ViewModel.SetNativeVideoWindowAsync(e.X11WindowId);
                
                // 初始化人脸框弹出窗口
                InitializeFaceBoxPopup();
            }
        }
        
        /// <summary>
        /// 初始化人脸框弹出窗口
        /// </summary>
        private void InitializeFaceBoxPopup()
        {
            _logger.LogInformation("InitializeFaceBoxPopup 被调用");
            
            if (_nativeVideoHost == null)
            {
                _logger.LogWarning("InitializeFaceBoxPopup: _nativeVideoHost 为 null");
                return;
            }
            if (ViewModel == null)
            {
                _logger.LogWarning("InitializeFaceBoxPopup: ViewModel 为 null");
                return;
            }
                
            // 获取视频控件在屏幕上的位置
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _logger.LogInformation("InitializeFaceBoxPopup: 开始计算位置");
                    
                    var visualRoot = _nativeVideoHost.GetVisualRoot();
                    _logger.LogInformation("InitializeFaceBoxPopup: visualRoot = {Root}", visualRoot?.GetType().Name ?? "null");
                    
                    if (visualRoot is Window window)
                    {
                        // 获取控件相对于窗口的位置
                        var transformMatrix = _nativeVideoHost.TransformToVisual(window);
                        _logger.LogInformation("InitializeFaceBoxPopup: transformMatrix HasValue = {HasValue}", transformMatrix.HasValue);
                        
                        if (transformMatrix.HasValue)
                        {
                            var topLeft = transformMatrix.Value.Transform(new Point(0, 0));
                            var windowPos = window.Position;
                            
                            // 计算屏幕绝对位置
                            double screenX = windowPos.X + topLeft.X;
                            double screenY = windowPos.Y + topLeft.Y;
                            double width = _nativeVideoHost.Bounds.Width;
                            double height = _nativeVideoHost.Bounds.Height;
                            
                            _logger.LogInformation("视频控件位置: 屏幕({X},{Y}), 尺寸({W}x{H})", screenX, screenY, width, height);
                            
                            ViewModel.InitializeFaceBoxPopup(screenX, screenY, width, height);
                        }
                        else
                        {
                            _logger.LogWarning("InitializeFaceBoxPopup: transformMatrix 为空");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("InitializeFaceBoxPopup: visualRoot 不是 Window");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化人脸框弹出窗口失败");
                }
            });
        }
        #endregion

        #region 窗口事件处理
        /// <summary>
        /// 窗口显示时的事件处理
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _logger.LogInformation("StoreWindow：存放物品窗口已打开");

            try
            {
                // 重置状态
                _isForceClosingByAppState = false;
                _isReturnButtonClicked = false;

                // 激活窗口
                this.Activate();
                this.Focus();

                // 开始初始化摄像头和识别流程
                _ = InitializeCameraAndRecognitionAsync();

                _logger.LogInformation("StoreWindow：窗口打开流程完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreWindow：打开窗口时发生异常");
            }
        }

        /// <summary>
        /// 异步初始化摄像头和识别流程
        /// </summary>
        private async Task InitializeCameraAndRecognitionAsync()
        {
            if (!_isInitialized || ViewModel == null)
            {
                _logger.LogWarning("StoreWindow：窗口未初始化或ViewModel为空，跳过摄像头初始化");
                return;
            }

            try
            {
                _logger.LogInformation("StoreWindow：开始初始化摄像头和识别流程");

                // 使用ViewModel初始化摄像头和人脸识别
                await ViewModel.InitializeWindowAsync();

                _logger.LogInformation("StoreWindow：摄像头和识别流程初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreWindow：初始化摄像头和识别流程时发生异常");
            }
        }

        /// <summary>
        /// 窗口关闭请求处理
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                _logger.LogInformation("StoreWindow：处理窗口关闭请求");

                // 如果是由AppStateService强制关闭的，直接允许关闭
                if (_isForceClosingByAppState)
                {
                    _logger.LogDebug("由AppStateService强制关闭，直接关闭窗口");
                    e.Cancel = false;
                    return;
                }

                // 如果是返回按钮触发的关闭，直接允许关闭
                if (_isReturnButtonClicked)
                {
                    _logger.LogDebug("返回按钮触发的关闭，直接关闭窗口");
                    e.Cancel = false;
                    return;
                }

                // 其他关闭方式（如用户点击X按钮），先执行返回主界面逻辑
                _logger.LogInformation("非返回主界面操作触发的关闭，执行返回主界面流程");
                e.Cancel = true; // 先阻止关闭

                // 使用信号量防止并发关闭操作
                if (!_closingSemaphore.Wait(0))
                {
                    _logger.LogWarning("关闭操作正在进行中，跳过重复调用");
                    e.Cancel = false;
                    return;
                }

                // 异步执行返回主界面（使用UI线程确保ViewModel访问正确）
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        if (ViewModel != null)
                        {
                            _logger.LogInformation("执行ViewModel返回主界面操作");
                            await ViewModel.ExecuteReturnToMainAsync();
                        }
                        else
                        {
                            _logger.LogWarning("ViewModel为空，直接关闭窗口");
                            this.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "执行返回主界面时发生异常");
                        // 发生异常时也关闭窗口
                        this.Close();
                    }
                    finally
                    {
                        try
                        {
                            if (_closingSemaphore.CurrentCount == 0)
                            {
                                _closingSemaphore.Release();
                            }
                        }
                        catch (SemaphoreFullException semaphoreEx)
                        {
                            _logger.LogWarning(semaphoreEx, "释放关闭信号量时发生异常");
                        }
                        catch (Exception semaphoreEx)
                        {
                            _logger.LogError(semaphoreEx, "释放关闭信号量时发生未预期的异常");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理窗口关闭时发生异常");
                e.Cancel = false; // 发生异常时直接关闭
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// 窗口关闭时的事件处理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _logger.LogInformation("StoreWindow：存放物品窗口已关闭");

            try
            {
                // 清理ViewModel资源
                if (ViewModel != null)
                {
                    try
                    {
                        // 调用Dispose释放资源
                        ViewModel.Dispose();
                        _logger.LogDebug("ViewModel资源已释放");
                    }
                    catch (Exception disposeEx)
                    {
                        _logger.LogError(disposeEx, "释放ViewModel资源时发生异常");
                    }
                }

                // 重置状态
                _isInitialized = false;
                _isForceClosingByAppState = false;
                _isReturnButtonClicked = false;

                _logger.LogInformation("StoreWindow：窗口状态重置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreWindow：关闭窗口时发生异常");
            }
            finally
            {
                try
                {
                    if (_closingSemaphore.CurrentCount == 0)
                    {
                        _closingSemaphore.Release();
                    }
                }
                catch (Exception semaphoreEx)
                {
                    _logger.LogWarning(semaphoreEx, "释放关闭信号量时发生异常");
                }
            }

            base.OnClosed(e);
        }
        #endregion

        #region 窗口焦点处理
        /// <summary>
        /// 窗口获得焦点时的事件处理
        /// </summary>
        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);
            _logger.LogDebug("StoreWindow：窗口获得焦点");

            try
            {
                // 确保窗口保持激活状态
                this.Activate();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理窗口获得焦点时发生异常");
            }
        }

        /// <summary>
        /// 窗口失去焦点时的事件处理
        /// </summary>
        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            _logger.LogDebug("StoreWindow：窗口失去焦点");

            try
            {
                // 如果窗口失去焦点，尝试重新获取焦点
                if (!_isForceClosingByAppState)
                {
                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            this.Activate();
                            this.Focus();
                            _logger.LogDebug("StoreWindow：已重新激活窗口焦点");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "重新激活窗口焦点时发生异常");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理窗口失去焦点时发生异常");
            }
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 由AppStateService强制关闭窗口
        /// </summary>
        public void ForceCloseByAppState()
        {
            try
            {
                _logger.LogInformation("AppStateService强制关闭存放物品窗口");
                _isForceClosingByAppState = true;

                // 在UI线程执行关闭操作
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (this.IsVisible && !_isDisposed)
                        {
                            this.Close();
                        }
                        else
                        {
                            _logger.LogDebug("窗口已不可见或已释放，跳过关闭");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "强制关闭窗口时发生异常");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "强制关闭窗口时发生异常");
            }
        }
        #endregion

        #region 资源清理
        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            try
            {
                _logger.LogInformation("开始清理StoreWindow资源");

                // 标记为已释放
                _isDisposed = true;

                // 关闭人脸框弹出窗口
                try
                {
                    ViewModel?.CloseFaceBoxPopup();
                    _logger.LogDebug("人脸框弹出窗口已关闭");
                }
                catch (Exception popupEx)
                {
                    _logger.LogWarning(popupEx, "关闭人脸框弹出窗口时发生警告");
                }

                // 清理信号量
                try
                {
                    _closingSemaphore?.Dispose();
                    _logger.LogDebug("关闭信号量已释放");
                }
                catch (Exception semaphoreEx)
                {
                    _logger.LogWarning(semaphoreEx, "释放关闭信号量时发生警告");
                }

                _logger.LogInformation("StoreWindow资源清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理StoreWindow资源时发生异常");
            }
        }
        #endregion
    }
}
