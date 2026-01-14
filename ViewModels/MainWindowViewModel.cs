using FaceLocker.Services;
using FaceLocker.Views;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using ReactiveUI;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace FaceLocker.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// 使用 ReactiveUI 框架实现 MVVM 模式
    /// </summary>
    public class MainWindowViewModel : ViewModelBase, IScreen
    {
        #region 私有字段
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly IEnvironmentCheckService _envService;
        private readonly ISessionAuthService _sessionAuthService;
        private readonly IUserService _userService;
        private readonly BaiduFaceService _baiduFaceService;
        private readonly IAppStateService _appStateService;

        private bool _selfCheckPassed = false;
        private Avalonia.Controls.Window? _mainWindow;
        private string _title = "黄埔区看守所智能柜管理系统";
        private string? _selfCheckError;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化主窗口视图模型
        /// </summary>
        /// <param name="logger">日志服务</param>
        /// <param name="envService">环境检查服务</param>
        /// <param name="sessionAuthService">会话认证服务</param>
        /// <param name="userService">用户服务</param>
        /// <param name="baiduFaceService">百度人脸服务</param>
        /// <param name="appStateService">应用状态服务</param>
        public MainWindowViewModel(
            ILogger<MainWindowViewModel> logger,
            IEnvironmentCheckService envService,
            ISessionAuthService sessionAuthService,
            IUserService userService,
            BaiduFaceService baiduFaceService,
            IAppStateService appStateService)
        {
            _logger = logger;
            _envService = envService;
            _sessionAuthService = sessionAuthService;
            _userService = userService;
            _baiduFaceService = baiduFaceService;
            _appStateService = appStateService;

            _logger.LogInformation("MainWindowViewModel 开始初始化");

            // 初始化路由状态
            Router = new RoutingState();

            // 初始化命令
            InitializeCommands();

            // 属性变化通知
            InitializePropertySubscriptions();

            // 执行异步初始化
            Init();

            _logger.LogInformation("MainWindowViewModel 初始化完成");
        }
        #endregion

        #region IScreen 实现
        /// <summary>
        /// 路由状态
        /// </summary>
        public RoutingState Router { get; }
        #endregion

        #region 属性定义
        /// <summary>
        /// 窗口标题
        /// </summary>
        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        /// <summary>
        /// 自检错误信息
        /// </summary>
        public string? SelfCheckError
        {
            get => _selfCheckError;
            set => this.RaiseAndSetIfChanged(ref _selfCheckError, value);
        }

        /// <summary>
        /// 是否存在自检错误
        /// </summary>
        public bool HasSelfCheckError => !string.IsNullOrWhiteSpace(SelfCheckError);

        /// <summary>
        /// 窗口引用
        /// </summary>
        public Avalonia.Controls.Window? WindowReference
        {
            get => _mainWindow;
            set => this.RaiseAndSetIfChanged(ref _mainWindow, value);
        }
        #endregion

        #region 命令定义
        /// <summary>
        /// 全屏切换命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ToggleFullScreenCommand { get; private set; } = null!;

        /// <summary>
        /// 关闭命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;

        /// <summary>
        /// 存物品命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> StoreButtonCommand { get; private set; } = null!;

        /// <summary>
        /// 取物品命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> RetrieveButtonCommand { get; private set; } = null!;

        /// <summary>
        /// 新管理界面命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> AdminMainButtonCommand { get; private set; } = null!;
        #endregion

        #region 初始化命令
        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitializeCommands()
        {
            _logger.LogDebug("初始化 MainWindowViewModel 命令");

            ToggleFullScreenCommand = ReactiveCommand.Create(ExecuteToggleFullScreen);
            CloseCommand = ReactiveCommand.Create(ExecuteClose);
            StoreButtonCommand = ReactiveCommand.Create(ExecuteStoreButton);
            RetrieveButtonCommand = ReactiveCommand.Create(ExecuteRetrieveButton);
            AdminMainButtonCommand = ReactiveCommand.Create(ExecuteAdminMainButton);

            _logger.LogDebug("MainWindowViewModel 命令初始化完成");
        }
        #endregion

        #region 初始化属性订阅
        /// <summary>
        /// 初始化属性订阅
        /// </summary>
        private void InitializePropertySubscriptions()
        {
            _logger.LogDebug("初始化 MainWindowViewModel 属性订阅");

            // 监听自检错误变化
            this.WhenAnyValue(x => x.SelfCheckError)
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(HasSelfCheckError));
                    _logger.LogDebug("SelfCheckError 属性变化，HasSelfCheckError: {HasError}", HasSelfCheckError);
                });

            _logger.LogDebug("MainWindowViewModel 属性订阅初始化完成");
        }
        #endregion

        #region 异步初始化方法
        /// <summary>
        /// 异步初始化方法
        /// </summary>
        private void Init()
        {
            _logger.LogInformation("开始执行自检初始化");

            _ = Task.Run
                (async () =>
                {
                    await Task.Delay(1000);

                    _logger.LogInformation("开始执行设备自检");
                    var selfCheckResult = await _envService.RunFullCheckAsync();
                    if (selfCheckResult != null && selfCheckResult.OverallStatus)
                    {
                        _selfCheckPassed = true;
                        _logger.LogInformation("设备自检通过");
                    }
                    else
                    {
                        _selfCheckPassed = false;
                        SelfCheckError = "设备硬件自检未通过";
                        _logger.LogWarning("设备自检未通过");
                    }
                });

        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 获取主窗口实例
        /// </summary>
        /// <returns>主窗口实例</returns>
        private MainWindow? GetMainWindow()
        {
            // 优先使用直接注入的窗口引用
            if (WindowReference is MainWindow injected)
                return injected;

            // 回退到应用状态服务中的主窗口
            if (_appStateService.MainWindow is MainWindow mainWindow)
                return mainWindow;

            _logger.LogWarning("无法获取主窗口实例");
            return null;
        }
        #endregion

        #region 执行全屏切换
        /// <summary>
        /// 执行全屏切换
        /// </summary>
        private void ExecuteToggleFullScreen()
        {
            _logger.LogInformation("执行全屏切换命令");
            var win = GetMainWindow();
            win?.ToggleFullScreen();
        }
        #endregion

        #region 执行关闭命令
        /// <summary>
        /// 执行关闭命令
        /// </summary>
        private void ExecuteClose()
        {
            _logger.LogInformation("执行关闭命令");
            var win = GetMainWindow();
            win?.Close();
        }
        #endregion

        #region 执行存物品按钮命令
        /// <summary>
        /// 执行存物品按钮命令
        /// </summary>
        private void ExecuteStoreButton()
        {
            _logger.LogInformation("执行存物品按钮命令");

            // 防止重复点击
            if (_appStateService.IsWindowOpen<StoreWindow>())
            {
                _logger.LogWarning("存放物品窗口已打开，跳过重复点击");
                return;
            }

            // 显示存放物品窗口
            _appStateService.ShowStoreWindowAsync();
        }
        #endregion

        #region 执行取物品按钮命令
        /// <summary>
        /// 执行取物品按钮命令
        /// </summary>
        private void ExecuteRetrieveButton()
        {
            try
            {
                _logger.LogInformation("执行取物品按钮命令");

                // 防止重复点击
                if (_appStateService.IsWindowOpen<RetrieveWindow>())
                {
                    _logger.LogWarning("取物窗口已打开，跳过重复点击");
                    return;
                }

                _logger.LogInformation("开始打开取物窗口");

                // 显示取物窗口
                _appStateService.ShowRetrieveWindowAsync();

                _logger.LogInformation("取物窗口打开请求已发送");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开取物窗口时发生异常");
            }
        }
        #endregion

        #region 执行管理主窗口按钮命令
        /// <summary>
        /// 执行管理按钮命令
        /// </summary>
        private void ExecuteAdminMainButton()
        {
            _logger.LogInformation("执行管理按钮命令");

            _logger.LogInformation("会话已过期，打开管理员登录窗口");
            _appStateService.ShowAdminLoginWindow();
        }
        #endregion
    }
}
