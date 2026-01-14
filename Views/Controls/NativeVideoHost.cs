using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace FaceLocker.Views.Controls
{
    /// <summary>
    /// 原生视频显示控件
    /// 
    /// 核心功能：
    /// 1. 封装 NativeControlHost 获取 X11 窗口 ID
    /// 2. 提供窗口句柄给 GStreamer 用于零拷贝渲染
    /// 3. 视频直接绘制到此窗口，不经过 Avalonia UI 线程
    /// 
    /// 使用方法：
    /// 1. 在 XAML 中添加此控件
    /// 2. 监听 WindowHandleReady 事件获取窗口句柄
    /// 3. 将句柄传递给 NativeVideoCameraService
    /// </summary>
    public class NativeVideoHost : NativeControlHost
    {
        #region 事件
        /// <summary>
        /// 窗口句柄就绪事件
        /// </summary>
        public event EventHandler<WindowHandleReadyEventArgs>? WindowHandleReady;
        #endregion

        #region 私有字段
        private readonly ILogger<NativeVideoHost>? _logger;
        private IntPtr _nativeHandle = IntPtr.Zero;
        private bool _handleReported = false;
        #endregion

        #region 属性
        /// <summary>
        /// 原生窗口句柄
        /// </summary>
        public IntPtr NativeHandle => _nativeHandle;

        /// <summary>
        /// X11 窗口 ID（仅在 X11 环境下有效）
        /// </summary>
        public ulong X11WindowId => (ulong)_nativeHandle.ToInt64();

        /// <summary>
        /// 是否已就绪
        /// </summary>
        public bool IsHandleReady => _nativeHandle != IntPtr.Zero;
        #endregion

        #region 构造函数
        public NativeVideoHost()
        {
            try
            {
                _logger = App.GetService<ILogger<NativeVideoHost>>();
            }
            catch
            {
                // 忽略，日志服务可能未初始化
            }

            _logger?.LogDebug("NativeVideoHost 创建");
        }
        #endregion

        #region 重写方法
        /// <summary>
        /// 创建原生控件核心
        /// </summary>
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            _logger?.LogInformation("NativeVideoHost: 创建原生控件核心");

            // 调用基类创建原生窗口
            var handle = base.CreateNativeControlCore(parent);

            if (handle != null && handle.Handle != IntPtr.Zero)
            {
                _nativeHandle = handle.Handle;
                _logger?.LogInformation("NativeVideoHost: 获取到原生窗口句柄 0x{Handle:X}", _nativeHandle.ToInt64());

                // 在 UI 线程报告句柄（延迟一下确保窗口完全创建）
                Dispatcher.UIThread.Post(() =>
                {
                    ReportWindowHandle();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                _logger?.LogWarning("NativeVideoHost: 创建原生控件失败");
            }

            return handle!;
        }

        /// <summary>
        /// 销毁原生控件核心
        /// </summary>
        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _logger?.LogInformation("NativeVideoHost: 销毁原生控件核心");
            
            _nativeHandle = IntPtr.Zero;
            _handleReported = false;

            base.DestroyNativeControlCore(control);
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 报告窗口句柄
        /// </summary>
        private void ReportWindowHandle()
        {
            if (_handleReported || _nativeHandle == IntPtr.Zero)
            {
                return;
            }

            _handleReported = true;

            _logger?.LogInformation("NativeVideoHost: 报告窗口句柄 0x{Handle:X}", _nativeHandle.ToInt64());

            WindowHandleReady?.Invoke(this, new WindowHandleReadyEventArgs
            {
                Handle = _nativeHandle,
                X11WindowId = (ulong)_nativeHandle.ToInt64()
            });
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 手动请求窗口句柄（如果错过了事件）
        /// </summary>
        public void RequestWindowHandle()
        {
            if (_nativeHandle != IntPtr.Zero)
            {
                _handleReported = false;
                Dispatcher.UIThread.Post(ReportWindowHandle, DispatcherPriority.Normal);
            }
        }
        #endregion
    }

    /// <summary>
    /// 窗口句柄就绪事件参数
    /// </summary>
    public class WindowHandleReadyEventArgs : EventArgs
    {
        /// <summary>
        /// 原生窗口句柄
        /// </summary>
        public IntPtr Handle { get; set; }

        /// <summary>
        /// X11 窗口 ID
        /// </summary>
        public ulong X11WindowId { get; set; }
    }
}
