using Avalonia.Threading;
using FaceLocker.Models;
using FaceLocker.Models.Settings;
using FaceLocker.ViewModels;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 会话认证服务实现
    /// </summary>
    public class SessionAuthService : ISessionAuthService
    {
        private readonly SecuritySettings _securitySettings;
        private readonly ILogger<SessionAuthService> _logger;
        private readonly IAdminLoginService _adminLoginService;
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;

        private bool _isAuthenticated = false;
        private DateTime? _authenticatedAt = null;
        private int _sessionTimeoutMinutes = 30;
        private readonly object _lockObject = new object();

        /// <summary>
        /// 当前认证的用户
        /// </summary>
        public User CurrentUser { get; private set; }

        /// <summary>
        /// 获取是否已认证
        /// </summary>
        public bool IsAuthenticated
        {
            get
            {
                lock (_lockObject)
                {
                    if (!_isAuthenticated || _authenticatedAt == null)
                        return false;

                    // 检查会话是否过期
                    if (IsSessionExpired())
                    {
                        ClearAuthentication();
                        return false;
                    }

                    return _isAuthenticated;
                }
            }
        }

        /// <summary>
        /// 获取认证时间
        /// </summary>
        public DateTime? AuthenticatedAt
        {
            get
            {
                lock (_lockObject)
                {
                    return _authenticatedAt;
                }
            }
        }

        /// <summary>
        /// 获取或设置会话超时时间（分钟）
        /// </summary>
        public int SessionTimeoutMinutes
        {
            get => _sessionTimeoutMinutes;
            set
            {
                if (value > 0)
                {
                    _sessionTimeoutMinutes = value;
                    _logger.LogInformation("会话超时时间已更新为: {Timeout}分钟", value);
                }
            }
        }

        /// <summary>
        /// 认证状态变更事件
        /// </summary>
        public event EventHandler<AuthenticationStatusChangedEventArgs> AuthenticationStatusChanged;

        #region 构造函数
        /// <summary>
        /// 初始化会话认证服务
        /// </summary>
        /// <param name="appSettings">应用设置</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="adminLoginService">管理员登录服务</param>
        /// <param name="userService">用户服务</param>
        /// <param name="roleService">角色服务</param>
        public SessionAuthService(
            IOptions<AppSettings> appSettings,
            ILogger<SessionAuthService> logger,
            IAdminLoginService adminLoginService,
            IUserService userService,
            IRoleService roleService)
        {
            _securitySettings = appSettings.Value.Security;
            _logger = logger;
            _adminLoginService = adminLoginService;
            _userService = userService;
            _roleService = roleService;

            // 从配置读取会话超时时间
            _sessionTimeoutMinutes = 30;

            _logger.LogInformation("会话认证服务初始化完成，会话超时时间: {Timeout}分钟", _sessionTimeoutMinutes);
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 认证用户
        /// </summary>
        /// <param name="mode">认证模式</param>
        /// <param name="operation">操作描述</param>
        public async Task<bool> AuthenticateAsync(AuthMode mode = AuthMode.Session, string operation = "")
        {
            try
            {
                // 如果是会话模式且已认证，直接返回成功
                if (mode == AuthMode.Session && IsAuthenticated)
                {
                    _logger.LogDebug("使用已有会话认证: {Operation}", operation);
                    RefreshSession(); // 刷新会话时间
                    return true;
                }

                // 显示密码验证对话框
                _logger.LogInformation("请求管理员密码验证: {Operation}", operation);

                var result = await ShowPasswordDialogAsync(mode, operation);

                if (result)
                {
                    if (mode == AuthMode.Session)
                    {
                        // 设置会话认证状态
                        SetAuthenticationStatus(true);
                        _logger.LogInformation("管理员认证成功，会话已建立: {Operation}", operation);

                        CurrentUser = new User
                        {
                            Id = 1,
                            Name = "admin",
                            UserNumber = "系统管理员",
                            Department = "系统部门",
                            RoleId = 1,
                            AssignedLockers = [],
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            Remarks = "系统默认管理员账户"
                        };
                    }
                    else
                    {
                        _logger.LogInformation("管理员密码验证成功: {Operation}", operation);
                    }
                }
                else
                {
                    _logger.LogWarning("管理员密码验证失败: {Operation}", operation);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "管理员认证失败: {Operation}", operation);
                return false;
            }
        }

        /// <summary>
        /// 验证密码
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        public async Task<bool> VerifyPasswordAsync(string username, string password)
        {
            try
            {
                _logger.LogDebug("验证用户密码，用户名: {Username}", username);
                return await _adminLoginService.VerifyAdminLoginAsync(username, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "密码验证失败，用户名: {Username}", username);
                return false;
            }
        }

        /// <summary>
        /// 设置认证状态
        /// </summary>
        /// <param name="authenticated">是否认证</param>
        public void SetAuthenticationStatus(bool authenticated)
        {
            lock (_lockObject)
            {
                var oldStatus = _isAuthenticated;
                _isAuthenticated = authenticated;

                if (authenticated)
                {
                    _authenticatedAt = DateTime.Now;
                    _logger.LogInformation("管理员会话已建立");
                }
                else
                {
                    _authenticatedAt = null;
                    _logger.LogInformation("管理员会话已清除");
                }

                // 触发状态变更事件
                if (oldStatus != authenticated)
                {
                    AuthenticationStatusChanged?.Invoke(this, new AuthenticationStatusChangedEventArgs
                    {
                        OldStatus = oldStatus,
                        NewStatus = authenticated,
                        ChangedAt = DateTime.Now,
                        Reason = authenticated ? "会话建立" : "会话清除"
                    });
                }
            }
        }

        /// <summary>
        /// 清除认证状态
        /// </summary>
        public void ClearAuthentication()
        {
            lock (_lockObject)
            {
                if (_isAuthenticated)
                {
                    SetAuthenticationStatus(false);
                    _logger.LogInformation("管理员会话已清除");
                }
            }
        }

        /// <summary>
        /// 检查会话是否过期
        /// </summary>
        public bool IsSessionExpired()
        {
            lock (_lockObject)
            {
                if (!_isAuthenticated || _authenticatedAt == null)
                {
                    return true;
                }

                var elapsed = DateTime.Now - _authenticatedAt.Value;
                return elapsed.TotalMinutes > _sessionTimeoutMinutes;
            }
        }

        /// <summary>
        /// 刷新会话
        /// </summary>
        public void RefreshSession()
        {
            lock (_lockObject)
            {
                if (_isAuthenticated)
                {
                    _authenticatedAt = DateTime.Now;
                    _logger.LogDebug("管理员会话时间已刷新");
                }
            }
        }

        /// <summary>
        /// 获取剩余会话时间（分钟）
        /// </summary>
        public int GetRemainingSessionMinutes()
        {
            lock (_lockObject)
            {
                if (!_isAuthenticated || _authenticatedAt == null)
                {
                    return 0;
                }

                var elapsed = DateTime.Now - _authenticatedAt.Value;
                var remaining = _sessionTimeoutMinutes - elapsed.TotalMinutes;
                return Math.Max(0, (int)Math.Ceiling(remaining));
            }
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 显示密码验证对话框
        /// </summary>
        private async Task<bool> ShowPasswordDialogAsync(AuthMode mode, string operation)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var dialog = new AdminLoginWindow();

                    // 从应用程序的服务提供者获取所需服务
                    var serviceProvider = App.GetServiceProvider();
                    var sessionAuthService = serviceProvider.GetService<ISessionAuthService>();
                    var logger = serviceProvider.GetService<ILogger<AdminLoginViewModel>>();
                    var appStateService = serviceProvider.GetService<IAppStateService>();
                    var numPadDialogViewModel = serviceProvider.GetService<NumPadDialogViewModel>();

                    var viewModel = new AdminLoginViewModel(
                        closeAction: () => dialog.Close(true),
                        sessionAuthService: sessionAuthService,
                        logger: logger,
                        appStateService: appStateService,
                        numPadDialogViewModel: numPadDialogViewModel
                        );

                    dialog.DataContext = viewModel;

                    // 获取主窗口作为父窗口
                    var mainWindow = App.MainWindow;
                    if (mainWindow != null)
                    {
                        var result = await dialog.ShowDialog<bool?>(mainWindow);
                        return result == true;
                    }

                    _logger.LogWarning("无法获取主窗口，密码验证对话框显示失败");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "显示密码验证对话框失败");
                    return false;
                }
            });
        }
        #endregion
    }
}