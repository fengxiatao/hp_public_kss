using System;
using System.Reactive;
using Irihi.Avalonia.Shared.Contracts;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace FaceLocker.ViewModels.NumPad;

/// <summary>
/// 数字键盘对话框视图模型
/// </summary>
public partial class NumPadDialogViewModel : ReactiveObject, IDialogContext
{
    private readonly ILogger<NumPadDialogViewModel> _logger;
    private string _inputValue = string.Empty;
    private bool _isEnter;

    #region 构造函数

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <exception cref="ArgumentNullException">当 logger 为 null 时抛出</exception>
    public NumPadDialogViewModel(ILogger<NumPadDialogViewModel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeCommands();
        _logger.LogInformation("数字键盘对话框视图模型已初始化。");
    }

    #endregion

    #region 属性

    /// <summary>
    /// 输入值
    /// </summary>
    public string InputValue
    {
        get => _inputValue;
        set => this.RaiseAndSetIfChanged(ref _inputValue, value);
    }

    /// <summary>
    /// 是否按回车确认
    /// </summary>
    public bool IsEnter
    {
        get => _isEnter;
        private set => this.RaiseAndSetIfChanged(ref _isEnter, value);
    }

    #endregion

    #region 命令

    /// <summary>
    /// 回车确认命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> EnterPressed { get; private set; }

    #endregion

    #region 公共方法

    /// <summary>
    /// 初始化对话框
    /// </summary>
    /// <param name="inputValue">初始输入值</param>
    public void Initialize(string inputValue)
    {
        try
        {
            _logger.LogDebug("开始初始化数字键盘对话框，输入值：{InputValue}", inputValue);

            IsEnter = false;
            InputValue = inputValue;

            _logger.LogDebug("数字键盘对话框初始化完成。IsEnter 已重置为 false，InputValue 已设置为：{InputValue}", inputValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化数字键盘对话框时发生异常。输入值：{InputValue}", inputValue);
            throw;
        }
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    public void Close()
    {
        try
        {
            _logger.LogDebug("正在关闭数字键盘对话框。当前输入值：{InputValue}，IsEnter：{IsEnter}", InputValue, IsEnter);

            RequestClose?.Invoke(this, null);

            _logger.LogDebug("数字键盘对话框关闭请求已发送。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭数字键盘对话框时发生异常。");
            throw;
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 初始化所有命令
    /// </summary>
    private void InitializeCommands()
    {
        try
        {
            _logger.LogDebug("开始初始化命令。");

            EnterPressed = ReactiveCommand.Create(ExecuteEnterPressed);

            _logger.LogDebug("命令初始化完成。已创建 EnterPressed 命令。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化命令时发生异常。");
            throw;
        }
    }

    /// <summary>
    /// 执行回车确认操作
    /// </summary>
    private void ExecuteEnterPressed()
    {
        try
        {
            _logger.LogInformation("用户按下回车键确认输入。当前输入值：{InputValue}", InputValue);

            IsEnter = true;
            _logger.LogDebug("已设置 IsEnter 为 true。");

            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行回车确认操作时发生异常。");
            throw;
        }
    }

    #endregion

    #region 事件

    /// <summary>
    /// 请求关闭对话框事件
    /// </summary>
    public event EventHandler<object?>? RequestClose;

    #endregion
}
