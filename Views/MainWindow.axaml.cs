using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceLocker.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace FaceLocker.Views;

/// <summary>
/// 主窗口
/// 负责全屏管理和窗口布局
/// </summary>
public partial class MainWindow : Window
{
    #region 私有字段
    private readonly ILogger<MainWindow> _logger;
    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化主窗口
    /// </summary>
    public MainWindow()
    {
        _logger = App.GetService<ILogger<MainWindow>>();
        _logger.LogInformation("MainWindow 开始初始化");

        // 窗口配置
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;

        InitializeComponent();

        // 设置窗口引用
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.WindowReference = this;
        }

        _logger.LogInformation("MainWindow 初始化完成");
    }
    #endregion

    #region 初始化方法
    /// <summary>
    /// 初始化组件
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _logger.LogDebug("MainWindow XAML 加载完成");
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
        _logger.LogInformation("MainWindow 已打开");

        // 重要：不要在 OnOpened 再次强制设置 WindowState/Position/Width/Height
        // X11/WM 下反复重设会造成“窗口抖动/闪动”观感。
        this.Activate();
        this.Focus();
    }

    /// <summary>
    /// 窗口关闭时的事件处理
    /// </summary>
    /// <param name="e">事件参数</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _logger.LogInformation("MainWindow 已关闭");
    }
    #endregion

    #region 全屏管理
    /// <summary>
    /// 切换全屏/普通窗口
    /// </summary>
    public void ToggleFullScreen()
    {
        _logger.LogInformation("切换全屏状态，当前状态: {WindowState}", WindowState);

        if (WindowState == WindowState.FullScreen)
        {
            // 退出全屏：先切回 Normal，再恢复到安全可见的位置与大小
            ExitFullScreenAndRestore();
        }
        else
        {
            EnterFullScreenCoveringPrimary();
        }
    }

    /// <summary>
    /// 进入全屏覆盖主显示器
    /// </summary>
    private void EnterFullScreenCoveringPrimary()
    {
        _logger.LogInformation("进入全屏模式");

        WindowState = WindowState.FullScreen;

        // 通过窗口的 Screens 属性获取屏幕信息
        var screens = this.Screens;
        if (screens != null)
        {
            var screen = screens.Primary ?? screens.All.FirstOrDefault();
            if (screen is not null)
            {
                Position = screen.Bounds.Position;
                Width = screen.Bounds.Width;
                Height = screen.Bounds.Height;
                _logger.LogInformation("进入全屏模式，覆盖屏幕: {ScreenBounds}, 窗口位置: {Position}", screen.Bounds, Position);
            }
            else
            {
                _logger.LogWarning("未找到可用屏幕");
            }
        }
        else
        {
            _logger.LogWarning("无法获取屏幕集合");
        }
    }

    /// <summary>
    /// 退出全屏并恢复窗口
    /// </summary>
    private void ExitFullScreenAndRestore()
    {
        _logger.LogInformation("退出全屏模式");

        // 某些 WM 下需要允许 Resize 才会尊重宽高设置
        var oldResizable = CanResize;
        CanResize = true;

        WindowState = WindowState.Normal;

        // 默认给一个合理初值：工作区 80% 且居中
        RestoreToReasonableDefaultSize();

        // 关键：无论恢复自保存值还是默认值，都做一遍"钳制到屏内"
        EnsureWindowFullyInsideScreen();

        // 还原你的可调整大小配置
        CanResize = oldResizable;

        _logger.LogInformation("退出全屏完成，窗口大小: {Width}x{Height}, 位置: {Position}", Width, Height, Position);
    }

    /// <summary>
    /// 若没有保存过窗口边界，提供一个默认的可见尺寸与位置（工作区 80%，居中）
    /// </summary>
    private void RestoreToReasonableDefaultSize()
    {
        var screen = GetBestScreen();
        var wa = screen?.WorkingArea ?? screen?.Bounds;

        if (wa is null)
        {
            _logger.LogWarning("无法获取屏幕信息，使用默认窗口大小");
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // 默认 80% 工作区
        int targetW = (int)(wa.Value.Width * 0.8);
        int targetH = (int)(wa.Value.Height * 0.8);

        // 最小下限，避免太小
        targetW = Math.Max(targetW, 900);
        targetH = Math.Max(targetH, 600);

        Width = targetW;
        Height = targetH;

        // 居中
        var x = wa.Value.X + (wa.Value.Width - targetW) / 2;
        var y = wa.Value.Y + (wa.Value.Height - targetH) / 2;
        Position = new PixelPoint(x, y);

        _logger.LogDebug("设置默认窗口大小: {Width}x{Height}, 位置: {Position}", targetW, targetH, Position);
    }

    /// <summary>
    /// 保证窗口完全处于某个屏幕的工作区之内；若越界则钳制或居中
    /// </summary>
    private void EnsureWindowFullyInsideScreen()
    {
        var screen = GetBestScreen();
        var wa = screen?.WorkingArea ?? screen?.Bounds;
        if (wa is null)
        {
            _logger.LogWarning("无法获取屏幕信息，跳过窗口边界检查");
            return;
        }

        // 将当前 Width/Height（DIP）转换为像素尺寸进行比较（或直接用像素：简单处理）
        int winW = (int)Math.Round(Width);
        int winH = (int)Math.Round(Height);

        // 如果窗口比工作区还大，先缩小到工作区内（留一点边距）
        const int margin = 16;
        winW = Math.Min(winW, Math.Max(200, wa.Value.Width - margin * 2));
        winH = Math.Min(winH, Math.Max(150, wa.Value.Height - margin * 2));

        Width = winW;
        Height = winH;

        // 钳制位置：确保右上角不会跑到屏外
        int x = Position.X;
        int y = Position.Y;

        if (x < wa.Value.X + margin) x = wa.Value.X + margin;
        if (y < wa.Value.Y + margin) y = wa.Value.Y + margin;

        if (x + winW > wa.Value.Right - margin) x = wa.Value.Right - margin - winW;
        if (y + winH > wa.Value.Bottom - margin) y = wa.Value.Bottom - margin - winH;

        Position = new PixelPoint(x, y);

        _logger.LogDebug("窗口边界钳制完成: 位置 {Position}, 大小 {Width}x{Height}", Position, winW, winH);
    }

    /// <summary>
    /// 选择一个合适的屏幕（优先当前窗口所在屏；退化到主屏）
    /// </summary>
    private Screen? GetBestScreen()
    {
        if (Screens is null)
        {
            _logger.LogWarning("Screens 为 null");
            return null;
        }

        // 使用 ScreenFromWindow 方法获取当前窗口所在的屏幕
        try
        {
            return Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取当前窗口屏幕失败，使用主屏");
            return Screens.Primary ?? Screens.All.FirstOrDefault();
        }
    }

    /// <summary>
    /// 强制窗口居中显示
    /// </summary>
    public void ForceCenterWindow()
    {
        _logger.LogInformation("强制窗口居中显示");

        try
        {
            var screen = GetBestScreen();
            if (screen != null)
            {
                var screenBounds = screen.Bounds;
                var centerX = screenBounds.X + (screenBounds.Width - Width) / 2;
                var centerY = screenBounds.Y + (screenBounds.Height - Height) / 2;

                Position = new PixelPoint((int)centerX, (int)centerY);

                _logger.LogInformation("窗口已强制居中，位置: {Position}", Position);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "强制窗口居中时发生异常");
        }
    }
    #endregion
}