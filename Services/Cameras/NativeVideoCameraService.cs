using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceLocker.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 原生视频摄像头服务 - 零拷贝渲染版本
    /// 
    /// 核心优势：
    /// 1. 视频渲染完全绕过 Avalonia UI 线程
    /// 2. GStreamer + glimagesink 直接渲染到 X11 窗口
    /// 3. 人脸识别通过 appsink 分流，不影响显示
    /// 4. CPU 占用极低，适合 RK3568 (2G RAM)
    /// 
    /// 架构：
    /// Camera (V4L2) → GStreamer → mppjpegdec → tee
    ///                                         ├── glimagesink → X11 窗口
    ///                                         └── appsink → 人脸识别回调
    /// </summary>
    public class NativeVideoCameraService : INativeVideoCameraService, IDisposable
    {
        #region Native Interop
        private const string LibraryName = "gst_video_player";

        // 帧回调委托（人脸识别用）
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GstFrameCallback(IntPtr userData, IntPtr data, int width, int height, int stride);

        // 视频格式枚举
        private enum GstPlayerFormat
        {
            MJPEG = 0,
            YUY2 = 1,
            NV12 = 2
        }

        // 配置结构体
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct GstPlayerConfig
        {
            public string device;
            public int width;
            public int height;
            public int fps;
            public GstPlayerFormat format;
            [MarshalAs(UnmanagedType.I1)]
            public bool use_hardware_decode;
            [MarshalAs(UnmanagedType.I1)]
            public bool use_rga;
            public int face_detect_fps;
            public int face_detect_width;
            public int face_detect_height;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_global_init();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr gst_player_create(ref GstPlayerConfig config);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void gst_player_destroy(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_set_window(IntPtr handle, ulong x11_window_id);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_set_frame_callback(IntPtr handle, GstFrameCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_start(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_stop(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool gst_player_is_playing(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr gst_player_get_error_string(int error);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void gst_player_get_stats(IntPtr handle, out float fps, out int dropped_frames);

        // 人脸框结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct GstFaceBox
        {
            public float center_x;
            public float center_y;
            public float width;
            public float height;
            public float score;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int gst_player_set_face_boxes(IntPtr handle, 
            [MarshalAs(UnmanagedType.LPArray)] GstFaceBox[] boxes, 
            int count, int source_width, int source_height);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void gst_player_clear_face_boxes(IntPtr handle);
        #endregion

        #region 私有字段
        private readonly ILogger<NativeVideoCameraService> _logger;
        private readonly AppSettings _appSettings;
        private readonly object _lock = new();

        private IntPtr _playerHandle = IntPtr.Zero;
        private GstFrameCallback? _frameCallback;
        private bool _isDisposed = false;
        private ulong _currentWindowId = 0;

        // 人脸识别帧缓存
        private WriteableBitmap? _latestFaceFrame;
        private DateTime _lastFaceFrameTime = DateTime.MinValue;
        private readonly object _faceFrameLock = new();

        // 帧数据缓冲区
        private byte[]? _frameDataBuffer;
        private int _faceFrameWidth = 0;
        private int _faceFrameHeight = 0;
        #endregion

        #region 属性
        /// <summary>
        /// 摄像头是否可用
        /// </summary>
        public bool IsCameraAvailable { get; private set; }

        /// <summary>
        /// 帧捕获事件（用于人脸识别，非显示用途）
        /// 注意：显示已由 GStreamer 直接渲染，此事件仅提供人脸识别数据
        /// </summary>
        public event EventHandler<WriteableBitmap>? FrameDisplayCaptured;

        /// <summary>
        /// 原生帧回调（更高效的人脸识别接口，直接访问原始数据）
        /// </summary>
        public event EventHandler<NativeFrameEventArgs>? NativeFrameReceived;
        #endregion

        #region 构造函数
        public NativeVideoCameraService(
            ILogger<NativeVideoCameraService> logger,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _appSettings = appSettings.Value;

            _logger.LogInformation("NativeVideoCameraService 初始化");

            // 初始化 GStreamer
            if (gst_player_global_init() != 0)
            {
                _logger.LogError("GStreamer 全局初始化失败");
                IsCameraAvailable = false;
            }
            else
            {
                IsCameraAvailable = CheckCameraDevice();
            }
        }
        #endregion

        #region 检查设备
        private bool CheckCameraDevice()
        {
            try
            {
                var devicePath = _appSettings.Camera.DevicePath;
                if (System.IO.File.Exists(devicePath))
                {
                    _logger.LogInformation("摄像头设备存在: {Device}", devicePath);
                    return true;
                }
                _logger.LogWarning("摄像头设备不存在: {Device}", devicePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查摄像头设备时发生异常");
                return false;
            }
        }
        #endregion

        #region 设置窗口
        /// <summary>
        /// 设置视频渲染的 X11 窗口
        /// </summary>
        /// <param name="x11WindowId">X11 窗口 ID</param>
        public async Task<bool> SetWindowAsync(ulong x11WindowId)
        {
            if (_isDisposed)
            {
                _logger.LogError("服务已释放，无法设置窗口");
                return false;
            }

            if (x11WindowId == 0)
            {
                _logger.LogError("无效的窗口 ID");
                return false;
            }

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    _currentWindowId = x11WindowId;

                    if (_playerHandle != IntPtr.Zero)
                    {
                        int result = gst_player_set_window(_playerHandle, x11WindowId);
                        if (result != 0)
                        {
                            _logger.LogError("设置窗口失败: {Error}", GetErrorString(result));
                            return false;
                        }
                        _logger.LogInformation("已设置渲染窗口: 0x{WindowId:X}", x11WindowId);
                    }

                    return true;
                }
            });
        }
        #endregion

        #region 启动摄像头
        /// <summary>
        /// 启动摄像头
        /// </summary>
        public async Task<bool> StartCameraAsync()
        {
            if (_isDisposed)
            {
                _logger.LogError("服务已释放，无法启动");
                return false;
            }

            lock (_lock)
            {
                if (_playerHandle != IntPtr.Zero)
                {
                    _logger.LogInformation("摄像头已在运行中");
                    return true;
                }
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("开始启动原生视频摄像头");

                    var cfg = _appSettings.Camera;

                    // 构建配置
                    var config = new GstPlayerConfig
                    {
                        device = cfg.DevicePath,
                        width = cfg.Resolution.Width,
                        height = cfg.Resolution.Height,
                        fps = cfg.FrameRate,
                        format = GstPlayerFormat.MJPEG, // MJPEG 格式
                        use_hardware_decode = true,      // 使用 MPP 硬件解码
                        use_rga = true,                  // 使用 RGA 硬件加速
                        face_detect_fps = 10,           // 人脸检测 10 FPS（提高帧率减少延迟）
                        face_detect_width = 640,         // 人脸检测缩放宽度
                        face_detect_height = 360         // 人脸检测缩放高度
                    };

                    // 创建播放器
                    _playerHandle = gst_player_create(ref config);
                    if (_playerHandle == IntPtr.Zero)
                    {
                        _logger.LogError("创建播放器失败");
                        return false;
                    }

                    // 设置帧回调
                    _frameCallback = OnNativeFrameReceived;
                    int callbackResult = gst_player_set_frame_callback(_playerHandle, _frameCallback, IntPtr.Zero);
                    if (callbackResult != 0)
                    {
                        _logger.LogWarning("设置帧回调失败: {Error}", GetErrorString(callbackResult));
                    }

                    // 设置窗口（如果已有）
                    if (_currentWindowId != 0)
                    {
                        int windowResult = gst_player_set_window(_playerHandle, _currentWindowId);
                        if (windowResult != 0)
                        {
                            _logger.LogError("设置窗口失败: {Error}", GetErrorString(windowResult));
                            gst_player_destroy(_playerHandle);
                            _playerHandle = IntPtr.Zero;
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("窗口未设置，需要先调用 SetWindowAsync");
                    }

                    // 如果窗口已设置，启动播放
                    if (_currentWindowId != 0)
                    {
                        int startResult = gst_player_start(_playerHandle);
                        if (startResult != 0)
                        {
                            _logger.LogError("启动播放失败: {Error}", GetErrorString(startResult));
                            gst_player_destroy(_playerHandle);
                            _playerHandle = IntPtr.Zero;
                            return false;
                        }
                    }

                    _logger.LogInformation("原生视频摄像头启动成功");
                    IsCameraAvailable = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "启动摄像头时发生异常");
                    return false;
                }
            });
        }

        /// <summary>
        /// 启动播放（在窗口设置后调用）
        /// </summary>
        public async Task<bool> StartPlaybackAsync()
        {
            if (_isDisposed || _playerHandle == IntPtr.Zero)
            {
                return false;
            }

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (gst_player_is_playing(_playerHandle))
                    {
                        return true;
                    }

                    int result = gst_player_start(_playerHandle);
                    if (result != 0)
                    {
                        _logger.LogError("启动播放失败: {Error}", GetErrorString(result));
                        return false;
                    }

                    _logger.LogInformation("播放已启动");
                    return true;
                }
            });
        }
        #endregion

        #region 停止摄像头
        /// <summary>
        /// 停止摄像头
        /// </summary>
        public async Task StopCameraAsync()
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_playerHandle == IntPtr.Zero)
                    {
                        _logger.LogInformation("摄像头未运行，无需停止");
                        return;
                    }

                    try
                    {
                        _logger.LogInformation("开始停止原生视频摄像头");

                        // 停止播放
                        gst_player_stop(_playerHandle);

                        // 销毁播放器
                        gst_player_destroy(_playerHandle);
                        _playerHandle = IntPtr.Zero;
                        _frameCallback = null;

                        _logger.LogInformation("原生视频摄像头停止成功");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "停止摄像头时发生异常");
                    }
                }
            });
        }
        #endregion

        #region 帧回调
        /// <summary>
        /// 原生帧回调处理（人脸识别用）
        /// </summary>
        private void OnNativeFrameReceived(IntPtr userData, IntPtr data, int width, int height, int stride)
        {
            if (_isDisposed || data == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // 触发原生帧事件（高效接口）
                NativeFrameReceived?.Invoke(this, new NativeFrameEventArgs
                {
                    Data = data,
                    Width = width,
                    Height = height,
                    Stride = stride
                });

                // 如果有 FrameDisplayCaptured 订阅者，创建 WriteableBitmap
                if (FrameDisplayCaptured != null)
                {
                    CreateWriteableBitmapFromNative(data, width, height, stride);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理原生帧回调时发生异常");
            }
        }

        /// <summary>
        /// 从原生数据创建 WriteableBitmap
        /// </summary>
        private unsafe void CreateWriteableBitmapFromNative(IntPtr data, int width, int height, int stride)
        {
            int dataSize = stride * height;

            // 确保缓冲区大小
            if (_frameDataBuffer == null || _frameDataBuffer.Length < dataSize)
            {
                _frameDataBuffer = new byte[dataSize];
            }

            // 复制数据
            Marshal.Copy(data, _frameDataBuffer, 0, dataSize);

            // 在 UI 线程创建和更新 WriteableBitmap
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_isDisposed) return;

                    // 检查是否需要重新创建
                    if (_latestFaceFrame == null || _faceFrameWidth != width || _faceFrameHeight != height)
                    {
                        _latestFaceFrame?.Dispose();
                        var pixelSize = new Avalonia.PixelSize(width, height);
                        var dpi = new Avalonia.Vector(96, 96);
                        _latestFaceFrame = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                        _faceFrameWidth = width;
                        _faceFrameHeight = height;
                    }

                    // 复制数据到 WriteableBitmap
                    using (var lockedBitmap = _latestFaceFrame.Lock())
                    {
                        int copySize = Math.Min(dataSize, lockedBitmap.RowBytes * height);
                        fixed (byte* srcPtr = _frameDataBuffer)
                        {
                            Buffer.MemoryCopy(srcPtr, (void*)lockedBitmap.Address, copySize, copySize);
                        }
                    }

                    _lastFaceFrameTime = DateTime.Now;

                    // 触发事件
                    FrameDisplayCaptured?.Invoke(this, _latestFaceFrame);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "创建 WriteableBitmap 时发生异常");
                }
            }, DispatcherPriority.Normal);
        }
        #endregion

        #region 捕获单帧
        /// <summary>
        /// 捕获单帧（用于人脸识别）
        /// </summary>
        public async Task<WriteableBitmap> CaptureDisplayFrameAsync()
        {
            if (!gst_player_is_playing(_playerHandle))
            {
                throw new InvalidOperationException("摄像头未运行");
            }

            var startTime = DateTime.Now;
            const int timeoutMs = 3000;

            while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(timeoutMs))
            {
                lock (_faceFrameLock)
                {
                    if (_latestFaceFrame != null && DateTime.Now - _lastFaceFrameTime < TimeSpan.FromSeconds(5))
                    {
                        return _latestFaceFrame;
                    }
                }
                await Task.Delay(10);
            }

            throw new TimeoutException("捕获帧超时");
        }
        #endregion

        #region 健康检查
        /// <summary>
        /// 检查摄像头健康状态
        /// </summary>
        public async Task<bool> CheckCameraHealthAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string devicePath = _appSettings.Camera.DevicePath;
                    return System.IO.File.Exists(devicePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查摄像头健康状态时发生异常");
                    return false;
                }
            });
        }
        #endregion

        #region 统计信息
        /// <summary>
        /// 获取性能统计
        /// </summary>
        public (float Fps, int DroppedFrames) GetStats()
        {
            if (_playerHandle == IntPtr.Zero)
            {
                return (0, 0);
            }

            gst_player_get_stats(_playerHandle, out float fps, out int dropped);
            return (fps, dropped);
        }

        /// <summary>
        /// 设置人脸框（用于 GStreamer cairooverlay 绘制）
        /// </summary>
        public void SetFaceBoxes(GstFaceBox[]? boxes, int sourceWidth, int sourceHeight)
        {
            if (_playerHandle == IntPtr.Zero)
            {
                _logger.LogWarning("[SetFaceBoxes] _playerHandle 为空，跳过");
                return;
            }

            if (boxes == null || boxes.Length == 0)
            {
                gst_player_clear_face_boxes(_playerHandle);
            }
            else
            {
                _logger.LogDebug("[SetFaceBoxes] 调用 native: count={Count}, srcW={W}, srcH={H}", boxes.Length, sourceWidth, sourceHeight);
                gst_player_set_face_boxes(_playerHandle, boxes, boxes.Length, sourceWidth, sourceHeight);
            }
        }

        /// <summary>
        /// 清除人脸框
        /// </summary>
        public void ClearFaceBoxes()
        {
            if (_playerHandle != IntPtr.Zero)
            {
                gst_player_clear_face_boxes(_playerHandle);
            }
        }
        #endregion

        #region 辅助方法
        private static string GetErrorString(int error)
        {
            IntPtr ptr = gst_player_get_error_string(error);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "未知错误" : "未知错误";
        }
        #endregion

        #region 资源释放
        public void Dispose()
        {
            if (_isDisposed) return;

            _logger.LogInformation("开始释放 NativeVideoCameraService 资源");

            try
            {
                _isDisposed = true;
                StopCameraAsync().Wait(5000);

                _latestFaceFrame?.Dispose();
                _latestFaceFrame = null;

                _logger.LogInformation("NativeVideoCameraService 资源释放完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时发生异常");
            }
        }
        #endregion
    }

    /// <summary>
    /// 原生帧事件参数
    /// </summary>
    public class NativeFrameEventArgs : EventArgs
    {
        /// <summary>
        /// 帧数据指针（BGRA 格式）
        /// </summary>
        public IntPtr Data { get; set; }

        /// <summary>
        /// 宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 行字节数
        /// </summary>
        public int Stride { get; set; }
    }

    /// <summary>
    /// 原生视频摄像头服务接口
    /// </summary>
    public interface INativeVideoCameraService : ICameraService
    {
        /// <summary>
        /// 设置视频渲染的 X11 窗口
        /// </summary>
        Task<bool> SetWindowAsync(ulong x11WindowId);

        /// <summary>
        /// 启动播放（在窗口设置后）
        /// </summary>
        Task<bool> StartPlaybackAsync();

        /// <summary>
        /// 原生帧事件（高效人脸识别接口）
        /// </summary>
        event EventHandler<NativeFrameEventArgs>? NativeFrameReceived;

        /// <summary>
        /// 获取性能统计
        /// </summary>
        (float Fps, int DroppedFrames) GetStats();

        /// <summary>
        /// 设置人脸框（用于 GStreamer cairooverlay 绘制）
        /// </summary>
        void SetFaceBoxes(NativeVideoCameraService.GstFaceBox[]? boxes, int sourceWidth, int sourceHeight);

        /// <summary>
        /// 清除人脸框
        /// </summary>
        void ClearFaceBoxes();
    }
}
