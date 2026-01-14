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
    /// V4L2 + MPP 摄像头服务实现
    /// 使用 V4L2 采集 MJPEG 数据，使用 Rockchip MPP 硬件解码
    /// </summary>
    public class V4L2MppCameraService : ICameraService, IDisposable
    {
        #region Native Interop
        private const string LibraryName = "v4l2_mpp_camera";

        // 帧回调委托
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FrameCallbackDelegate(IntPtr userData, IntPtr bgraData, int width, int height, int stride);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr camera_init(
            [MarshalAs(UnmanagedType.LPStr)] string device,
            int width,
            int height,
            int fps);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void camera_deinit(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int camera_start(IntPtr handle, FrameCallbackDelegate callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int camera_stop(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool camera_is_running(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr camera_get_error_string(int error);
        #endregion

        #region 私有字段
        private readonly ILogger<V4L2MppCameraService> _logger;
        private readonly AppSettings _appSettings;
        private readonly object _lock = new();
        
        private IntPtr _cameraHandle = IntPtr.Zero;
        private FrameCallbackDelegate? _frameCallback;
        private bool _isDisposed = false;
        
        // 帧缓存
        private WriteableBitmap? _latestFrame;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(5);
        
        // 双缓冲机制 - 避免每帧创建新的 WriteableBitmap
        private WriteableBitmap? _buffer1;
        private WriteableBitmap? _buffer2;
        private volatile int _currentBufferIndex = 0; // 0 = buffer1, 1 = buffer2
        private readonly object _bufferLock = new();
        private bool _buffersInitialized = false;
        private int _bufferWidth = 0;
        private int _bufferHeight = 0;
        
        // 帧丢弃机制 - 避免 UI 线程帧积压
        private volatile int _isProcessingFrame = 0; // 0 = 空闲, 1 = 处理中
        
        // 帧数据缓冲区 - 在原生线程复制数据，避免指针失效
        private byte[]? _frameDataBuffer;
        private int _frameDataBufferSize = 0;
        private readonly object _frameDataLock = new();
        #endregion

        #region 属性
        /// <summary>
        /// 摄像头是否可用
        /// </summary>
        public bool IsCameraAvailable { get; private set; }

        /// <summary>
        /// 帧捕获事件
        /// </summary>
        public event EventHandler<WriteableBitmap>? FrameDisplayCaptured;
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        public V4L2MppCameraService(
            ILogger<V4L2MppCameraService> logger,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            
            _logger.LogInformation("V4L2MppCameraService 初始化");
            IsCameraAvailable = CheckCameraDevice();
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

        #region 启动摄像头
        /// <summary>
        /// 启动摄像头
        /// </summary>
        public async Task<bool> StartCameraAsync()
        {
            if (_isDisposed)
            {
                _logger.LogError("摄像头服务已释放，无法启动");
                return false;
            }

            lock (_lock)
            {
                if (_cameraHandle != IntPtr.Zero)
                {
                    _logger.LogInformation("摄像头已经在运行中");
                    return true;
                }
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("开始启动 V4L2+MPP 摄像头");

                    var cfg = _appSettings.Camera;
                    
                    // 初始化相机
                    _cameraHandle = camera_init(
                        cfg.DevicePath,
                        cfg.Resolution.Width,
                        cfg.Resolution.Height,
                        cfg.FrameRate);

                    if (_cameraHandle == IntPtr.Zero)
                    {
                        _logger.LogError("camera_init 失败");
                        return false;
                    }

                    // 设置回调
                    _frameCallback = OnFrameReceived;

                    // 启动采集
                    int result = camera_start(_cameraHandle, _frameCallback, IntPtr.Zero);
                    if (result != 0)
                    {
                        string errorMsg = GetErrorString(result);
                        _logger.LogError("camera_start 失败: {Error}", errorMsg);
                        camera_deinit(_cameraHandle);
                        _cameraHandle = IntPtr.Zero;
                        return false;
                    }

                    _logger.LogInformation("V4L2+MPP 摄像头启动成功");
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
                    if (_cameraHandle == IntPtr.Zero)
                    {
                        _logger.LogInformation("摄像头未运行，无需停止");
                        return;
                    }

                    try
                    {
                        _logger.LogInformation("开始停止 V4L2+MPP 摄像头");
                        
                        camera_stop(_cameraHandle);
                        camera_deinit(_cameraHandle);
                        _cameraHandle = IntPtr.Zero;
                        _frameCallback = null;
                        
                        _logger.LogInformation("V4L2+MPP 摄像头停止成功");
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
        /// 初始化双缓冲（在 UI 线程调用）
        /// </summary>
        private void InitializeBuffers(int width, int height)
        {
            if (_buffersInitialized && _bufferWidth == width && _bufferHeight == height)
            {
                return;
            }

            lock (_bufferLock)
            {
                try
                {
                    // 释放旧缓冲区
                    _buffer1?.Dispose();
                    _buffer2?.Dispose();
                    _buffer1 = null;
                    _buffer2 = null;
                    _buffersInitialized = false;

                    var pixelSize = new Avalonia.PixelSize(width, height);
                    var dpi = new Avalonia.Vector(96, 96);

                    _buffer1 = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                    _buffer2 = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                    _bufferWidth = width;
                    _bufferHeight = height;
                    _currentBufferIndex = 0;
                    _buffersInitialized = true;

                    _logger.LogInformation("双缓冲初始化完成: {Width}x{Height}", width, height);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "双缓冲初始化失败");
                    _buffersInitialized = false;
                }
            }
        }

        /// <summary>
        /// 原生帧回调处理 - 双缓冲 + 帧丢弃优化版本
        /// 1. 避免每帧创建新的 WriteableBitmap，减少 GC 压力
        /// 2. 如果 UI 线程正在处理，跳过当前帧，避免积压
        /// 3. 在原生线程复制数据，避免指针失效问题
        /// </summary>
        private unsafe void OnFrameReceived(IntPtr userData, IntPtr bgraData, int width, int height, int stride)
        {
            if (_isDisposed || bgraData == IntPtr.Zero)
            {
                return;
            }

            // 帧丢弃机制：如果 UI 线程正在处理上一帧，跳过当前帧
            if (Interlocked.CompareExchange(ref _isProcessingFrame, 1, 0) != 0)
            {
                // UI 线程正忙，丢弃当前帧
                return;
            }

            try
            {
                int dataSize = stride * height;
                
                // 在原生线程安全复制数据（避免指针在 UI 线程使用时失效）
                lock (_frameDataLock)
                {
                    // 确保缓冲区足够大
                    if (_frameDataBuffer == null || _frameDataBufferSize < dataSize)
                    {
                        _frameDataBuffer = new byte[dataSize];
                        _frameDataBufferSize = dataSize;
                    }
                    
                    // 复制帧数据
                    Marshal.Copy(bgraData, _frameDataBuffer, 0, dataSize);
                }
                
                // 捕获局部变量供闭包使用
                int w = width;
                int h = height;
                int s = stride;
                
                // 在 UI 线程创建新的 WriteableBitmap（避免双缓冲状态问题）
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_isDisposed)
                        {
                            Interlocked.Exchange(ref _isProcessingFrame, 0);
                            return;
                        }

                        // 每帧创建新的 WriteableBitmap（简单稳定方案）
                        WriteableBitmap? newFrame = null;
                        try
                        {
                            var pixelSize = new Avalonia.PixelSize(w, h);
                            var dpi = new Avalonia.Vector(96, 96);
                            newFrame = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

                            lock (_frameDataLock)
                            {
                                using (var lockedBitmap = newFrame.Lock())
                                {
                                    int copySize = Math.Min(s * h, lockedBitmap.RowBytes * h);
                                    
                                    fixed (byte* srcPtr = _frameDataBuffer)
                                    {
                                        Buffer.MemoryCopy(
                                            srcPtr,
                                            (void*)lockedBitmap.Address,
                                            copySize,
                                            copySize);
                                    }
                                }
                            }
                        }
                        catch (Exception bitmapEx)
                        {
                            _logger.LogWarning(bitmapEx, "创建或写入位图时发生异常");
                            newFrame?.Dispose();
                            Interlocked.Exchange(ref _isProcessingFrame, 0);
                            return;
                        }

                        // 更新最新帧
                        var oldFrame = _latestFrame;
                        _latestFrame = newFrame;
                        _lastFrameTime = DateTime.Now;

                        // 触发事件（传递新帧）
                        FrameDisplayCaptured?.Invoke(this, newFrame);
                        
                        // 延迟释放旧帧（避免UI正在使用时释放）
                        if (oldFrame != null)
                        {
                            _ = Task.Delay(100).ContinueWith(_ => 
                            {
                                try { oldFrame.Dispose(); } catch { }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理帧数据时发生异常");
                    }
                    finally
                    {
                        // 标记处理完成，允许下一帧进入
                        Interlocked.Exchange(ref _isProcessingFrame, 0);
                    }
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                // 发生异常时也要重置标志位
                Interlocked.Exchange(ref _isProcessingFrame, 0);
                _logger.LogError(ex, "帧回调时发生异常");
            }
        }
        #endregion

        #region 捕获单帧
        /// <summary>
        /// 捕获单帧
        /// </summary>
        public async Task<WriteableBitmap> CaptureDisplayFrameAsync()
        {
            if (!camera_is_running(_cameraHandle))
            {
                throw new InvalidOperationException("摄像头未运行");
            }

            var startTime = DateTime.Now;
            const int timeoutMs = 3000;

            while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(timeoutMs))
            {
                if (_latestFrame != null && DateTime.Now - _lastFrameTime < _frameTimeout)
                {
                    return _latestFrame;
                }
                await Task.Delay(10);
            }

            throw new TimeoutException("捕获帧超时");
        }
        #endregion

        #region 检查摄像头健康状态
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

        #region 辅助方法
        private static string GetErrorString(int error)
        {
            IntPtr ptr = camera_get_error_string(error);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "Unknown error" : "Unknown error";
        }
        #endregion

        #region 资源释放
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _logger.LogInformation("开始释放 V4L2MppCameraService 资源");

            try
            {
                _isDisposed = true;
                StopCameraAsync().Wait(5000);

                // 释放双缓冲资源
                lock (_bufferLock)
                {
                    _buffer1?.Dispose();
                    _buffer2?.Dispose();
                    _buffer1 = null;
                    _buffer2 = null;
                    _buffersInitialized = false;
                }

                _logger.LogInformation("V4L2MppCameraService 资源释放完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时发生异常");
            }
        }
        #endregion
    }
}
