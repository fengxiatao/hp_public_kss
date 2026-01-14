using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FaceLocker.Services;
using FaceLocker.ViewModels;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views.NumPad;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;

namespace FaceLocker.Views;

/// <summary>
/// 管理员登录窗口
/// 使用 ReactiveUI 框架实现 MVVM 模式
/// </summary>
public partial class AdminLoginWindow : ReactiveWindow<AdminLoginViewModel>
{
    #region 私有字段
    private readonly ILogger<AdminLoginWindow> _logger;
    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化管理员登录窗口
    /// </summary>
    public AdminLoginWindow()
    {
        try
        {
            _logger = App.GetService<ILogger<AdminLoginWindow>>();
            _logger.LogInformation("AdminLoginWindow 开始初始化");

            // 先获取服务，再初始化组件
            var sessionAuthService = App.GetService<ISessionAuthService>();
            var logger = App.GetService<ILogger<AdminLoginViewModel>>();
            var appStateService = App.GetService<IAppStateService>();
            var numPadDialogViewModel = App.GetService<NumPadDialogViewModel>();

            // 创建 ViewModel
            ViewModel = new AdminLoginViewModel(() => Close(), sessionAuthService, logger, appStateService, numPadDialogViewModel);

            // 窗口配置
            SystemDecorations = SystemDecorations.None;
            CanResize = false;

            // 现在初始化组件
            InitializeComponent();

            // 设置 ReactiveUI 绑定
            this.WhenActivated(disposables =>
            {
                _logger.LogInformation("AdminLoginWindow WhenActivated 开始设置绑定");

                // 注册命令绑定
                disposables(ViewModel.LoginCommand.Subscribe());
                disposables(ViewModel.CancelCommand.Subscribe(_ => Close()));

                _logger.LogInformation("AdminLoginWindow 绑定设置完成");
            });

            this.Focus();
            _logger.LogInformation("AdminLoginWindow 初始化完成");
        }
        catch (Exception ex)
        {
            // 如果获取服务失败，尝试基本的初始化
            try
            {
                InitializeComponent();
                _logger?.LogError(ex, "AdminLoginWindow 初始化过程中发生异常，但已完成基础初始化");
            }
            catch
            {
                // 如果连基础初始化都失败，重新抛出原始异常
                throw new InvalidOperationException("AdminLoginWindow 初始化失败", ex);
            }
        }
    }
    #endregion

    #region 方法
    /// <summary>
    /// 初始化组件
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _logger?.LogDebug("AdminLoginWindow XAML 加载完成");
    }

    /// <summary>
    /// 窗口打开时的事件处理
    /// </summary>
    /// <param name="e">事件参数</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _logger?.LogInformation("AdminLoginWindow 已打开");

        try
        {
            // 设置焦点到用户名输入框
            var usernameTextBox = this.FindControl<TextBox>("UsernameTextBox");
            usernameTextBox?.Focus();
            _logger?.LogDebug("焦点已设置到用户名输入框");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置用户名输入框焦点时发生异常");
        }
    }

    /// <summary>
    /// 窗口关闭时的事件处理
    /// </summary>
    /// <param name="e">事件参数</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _logger?.LogInformation("AdminLoginWindow 已关闭");
    }
    #endregion

    #region 事件处理

    /// <summary>
    /// 密码文本框获得焦点事件处理
    /// </summary>
    private void OnPasswordGotFocus(object? sender, GotFocusEventArgs e)
    {
        try
        {
            _logger.LogInformation("文本框获得焦点，触发数字键盘显示。发送者：{SenderType}，事件源：{SourceType}", sender?.GetType().Name, e.Source?.GetType().Name);

            // 验证焦点状态
            if (sender is TextBox textBox)
            {
                _logger.LogDebug("文本框焦点状态：IsFocused={IsFocused}, IsEnabled={IsEnabled}", textBox.IsFocused, textBox.IsEnabled);
            }

            if (ViewModel != null)
            {
                // 检查命令是否可执行
                ViewModel.ShowNumPadCommand.Execute().Subscribe(
                    onNext: _ =>
                    {
                        _logger.LogDebug("数字键盘命令执行完成");
                    },
                    onError: ex =>
                    {
                        _logger.LogError(ex, "执行数字键盘命令时发生异常");
                    }
                );
            }
            else
            {
                _logger.LogWarning("ViewModel 为空，无法显示数字键盘。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理手机号文本框获得焦点事件时发生异常。");
        }
    }

    #endregion
}