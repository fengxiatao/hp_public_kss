using Avalonia.Threading;
using FaceLocker.Services;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views.NumPad;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ursa.Controls;

namespace FaceLocker.ViewModels
{
    /// <summary>
    /// 管理员登录视图模型
    /// 使用 ReactiveUI 框架实现 MVVM 模式
    /// </summary>
    public class AdminLoginViewModel : ViewModelBase
    {
        #region 私有字段
        private readonly Action _closeAction;
        private readonly ISessionAuthService _sessionAuthService;
        private readonly ILogger<AdminLoginViewModel> _logger;
        private readonly IAppStateService _appStateService;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private readonly ObservableAsPropertyHelper<bool> _isLoading;
        private readonly NumPadDialogViewModel _numPadDialogViewModel;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化管理员登录视图模型
        /// </summary>
        /// <param name="closeAction">关闭窗口的回调函数</param>
        /// <param name="sessionAuthService">会话认证服务</param>
        /// <param name="logger">日志服务</param>
        /// <param name="appStateService">应用状态服务</param>
        public AdminLoginViewModel(
            Action closeAction,
            ISessionAuthService sessionAuthService,
            ILogger<AdminLoginViewModel> logger,
            IAppStateService appStateService,
            NumPadDialogViewModel numPadDialogViewModel)
        {
            _closeAction = closeAction;
            _sessionAuthService = sessionAuthService;
            _logger = logger;
            _appStateService = appStateService;
            _numPadDialogViewModel = numPadDialogViewModel;

            // 设置开发版本默认凭据
            Username = "admin";
#if DEBUG
            Password = "123456";

            _logger.LogInformation("AdminLoginViewModel 初始化完成，开发版默认凭据已设置: admin/123456");
#endif
            // 初始化命令
            InitializeCommands();

            // 设置加载状态属性
            _isLoading = LoginCommand.IsExecuting.ToProperty(this, x => x.IsLoading);

            // 初始化属性订阅
            InitializePropertySubscriptions();

            _logger.LogInformation("AdminLoginViewModel ReactiveUI 初始化完成");
           
        }
        #endregion

        #region 属性
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }


        /// <summary>
        /// 显示数字键盘命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ShowNumPadCommand { get; private set; } = null!;

        /// <summary>
        /// 是否正在加载（执行登录操作）
        /// </summary>
        public bool IsLoading => _isLoading.Value;
        #endregion

        #region 命令定义
        /// <summary>
        /// 登录命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> LoginCommand { get; private set; } = null!;

        /// <summary>
        /// 取消命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> CancelCommand { get; private set; } = null!;
        #endregion

        #region 初始化方法
        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitializeCommands()
        {
            _logger.LogDebug("初始化 AdminLoginViewModel 命令");

            // 创建命令
            LoginCommand = ReactiveCommand.CreateFromTask(ExecuteLoginAsync, CanExecuteLogin());
            CancelCommand = ReactiveCommand.Create(ExecuteCancel);
            // 显示数字键盘命令
            ShowNumPadCommand = ReactiveCommand.CreateFromTask(ExecuteShowNumPadAsync);

            _logger.LogDebug("AdminLoginViewModel 命令初始化完成");
        }

        /// <summary>
        /// 初始化属性订阅
        /// </summary>
        private void InitializePropertySubscriptions()
        {
            _logger.LogDebug("初始化 AdminLoginViewModel 属性订阅");

            // 当错误消息改变时记录日志
            this.WhenAnyValue(x => x.ErrorMessage)
                .Where(error => !string.IsNullOrEmpty(error))
                .Subscribe(error => _logger.LogWarning("登录错误信息: {ErrorMessage}", error));

            // 监听用户名和密码变化，清除错误信息
            this.WhenAnyValue(x => x.Username, x => x.Password)
                .Subscribe(_ =>
                {
                    if (!string.IsNullOrEmpty(ErrorMessage))
                    {
                        ErrorMessage = string.Empty;
                        _logger.LogDebug("清空错误信息");
                    }
                });

            _logger.LogDebug("AdminLoginViewModel 属性订阅初始化完成");
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 检查是否可以执行登录操作
        /// </summary>
        /// <returns>可观察的布尔值</returns>
        private IObservable<bool> CanExecuteLogin()
        {
            return this.WhenAnyValue(
                x => x.Username,
                x => x.Password,
                (user, pwd) => !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pwd));
        }

        /// <summary>
        /// 执行登录操作
        /// </summary>
        /// <returns>异步任务</returns>
        private async Task ExecuteLoginAsync()
        {
            try
            {
                _logger.LogInformation("开始执行管理员登录验证，用户名: {Username}", Username);

                // 使用 SessionAuthService 进行密码验证
                var isValid = await _sessionAuthService.VerifyPasswordAsync(Username, Password);

                if (isValid)
                {
                    ErrorMessage = "";
                    _logger.LogInformation("管理员登录成功，用户名: {Username}", Username);

                    // 建立会话认证
                    _sessionAuthService.SetAuthenticationStatus(true);
                    _logger.LogInformation("管理员会话已建立，用户名: {Username}", Username);

                    // 关闭登录窗口
                    _closeAction?.Invoke();

                    // 使用 AppStateService 打开管理员管理窗口
                    _appStateService.ShowAdminMainWindow();
                }
                else
                {
                    ErrorMessage = "用户名或密码错误";
                    _logger.LogWarning("管理员登录失败：用户名或密码错误，用户名: {Username}", Username);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "登录过程中发生错误";
                _logger.LogError(ex, "管理员登录过程中发生异常，用户名: {Username}", Username);
                Debug.WriteLine($"登录异常: {ex}");
            }
        }

        /// <summary>
        /// 执行取消操作
        /// </summary>
        private void ExecuteCancel()
        {
            _logger.LogInformation("管理员登录取消操作");
            _closeAction?.Invoke();
        }
        #endregion

        #region 数字键盘命令执行
        /// <summary>
        /// 执行显示数字键盘操作
        /// </summary>
        private async Task ExecuteShowNumPadAsync()
        {
            _logger.LogDebug("数字键盘对话框打开，已暂停视频处理和人脸检测");

            try
            {
                // 配置对话框选项
                var options = new OverlayDialogOptions()
                {
                    Title = "输入密码",
                    Mode = DialogMode.None,
                    Buttons = DialogButton.None,
                    IsCloseButtonVisible = true,
                    CanDragMove = true,
                    CanResize = false,
                    HorizontalAnchor = HorizontalPosition.Left,
                    VerticalAnchor = VerticalPosition.Center,
                    HorizontalOffset = 50,
                    VerticalOffset = 152,
                    CanLightDismiss = true,
                };

                _logger.LogDebug("已配置数字键盘对话框选项：标题={Title}，模式={Mode}，按钮={Buttons}", options.Title, options.Mode, options.Buttons);

                _numPadDialogViewModel.Initialize(Password ?? string.Empty);

                // 显示模态对话框
                _logger.LogDebug("开始显示数字键盘模态对话框。");
                await OverlayDialog.ShowModal<NumPadDialogView, NumPadDialogViewModel>(_numPadDialogViewModel, options: options);
                _logger.LogDebug("数字键盘模态对话框已关闭。");

                // 获取对话框结果
                var isEnter = _numPadDialogViewModel.IsEnter;
                var dialogInputValue = _numPadDialogViewModel.InputValue;

                _logger.LogDebug("数字键盘对话框结果：IsEnter={IsEnter}, InputValue={DialogInputValue}",
                    isEnter, dialogInputValue);

                // 更新密码
                if (isEnter && !string.IsNullOrWhiteSpace(dialogInputValue))
                {
                    Password = dialogInputValue;
                }
                else if (isEnter && string.IsNullOrWhiteSpace(dialogInputValue))
                {
                    Password = string.Empty;
                }
                else
                {
                    _logger.LogDebug("用户取消输入手机号，保持原值。");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行显示数字键盘命令时发生异常。");
                throw;
            }
        }

        #endregion
    }
}