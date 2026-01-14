using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceLocker.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// GStreamer 摄像头服务实现
    /// 使用 GStreamer 管道提供高性能摄像头视频流
    /// </summary>
    public class GStreamerCameraService : ICameraService, IDisposable
    {
        #region 私有字段
        private readonly ILogger<GStreamerCameraService> _logger;
        private readonly AppSettings _appSettings;
        private readonly object _pipelineLock = new();
        private readonly object _frameLock = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private IntPtr _pipeline;
        private IntPtr _bus;
        private IntPtr _appSink;
        private bool _isRunning = false;
        private bool _isDisposed = false;
        private Task? _messageLoopTask;

        // 最新帧缓存
        private WriteableBitmap? _latestFrame;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(5);

        // 帧对象池 - 重用WriteableBitmap减少GC压力
        private readonly ConcurrentQueue<WriteableBitmap> _framePool = new();
        private readonly int _poolSize = 3;
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
        public GStreamerCameraService(
            ILogger<GStreamerCameraService> logger,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _appSettings = appSettings.Value;

            _logger.LogInformation("GStreamerCameraService 开始初始化");
            InitializeGStreamer();
            _logger.LogInformation("GStreamerCameraService 初始化完成");
        }
        #endregion

        #region 初始化方法
        /// <summary>
        /// 初始化 GStreamer
        /// </summary>
        private void InitializeGStreamer()
        {
            try
            {
                _logger.LogInformation("开始初始化 GStreamer");

                // 初始化 GStreamer
                if (!GStreamerInterop.gst_init_check(IntPtr.Zero, IntPtr.Zero, out bool initSuccess))
                {
                    _logger.LogError("GStreamer 初始化失败");
                    IsCameraAvailable = false;
                    return;
                }

                if (!initSuccess)
                {
                    _logger.LogWarning("GStreamer 初始化检查失败");
                    IsCameraAvailable = false;
                    return;
                }

                _logger.LogInformation("GStreamer 初始化成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 GStreamer 时发生异常");
                IsCameraAvailable = false;
            }
        }
        #endregion

        #region 启动摄像头
        /// <summary>
        /// 启动摄像头
        /// </summary>
        public async Task<bool> StartCameraAsync()
        {
            lock (_pipelineLock)
            {
                if (_isRunning)
                {
                    _logger.LogInformation("摄像头已经在运行中");
                    return true;
                }

                if (_isDisposed)
                {
                    _logger.LogError("摄像头服务已释放，无法启动");
                    return false;
                }
            }

            try
            {
                _logger.LogInformation("开始启动 GStreamer 摄像头");

                // 先停止可能存在的旧管道
                await StopCameraAsync();
                await Task.Delay(10);

                // 构建 GStreamer 管道
                var pipelineCreated = await CreatePipeline();
                if (!pipelineCreated)
                {
                    _logger.LogError("创建 GStreamer 管道失败");
                    return false;
                }

                // 设置管道状态为播放
                var setStateResult = GStreamerInterop.gst_element_set_state(_pipeline, GStreamerInterop.GstState.GST_STATE_PLAYING);
                if (setStateResult == GStreamerInterop.GstStateChangeReturn.GST_STATE_CHANGE_FAILURE)
                {
                    _logger.LogError("设置管道状态为播放失败");
                    DestroyPipeline();
                    return false;
                }

                lock (_pipelineLock)
                {
                    _isRunning = true;
                }

                // 启动消息循环
                _messageLoopTask = Task.Run(MessageLoop, _cancellationTokenSource.Token);

                _logger.LogInformation("GStreamer 摄像头启动成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 GStreamer 摄像头时发生异常");
                lock (_pipelineLock)
                {
                    _isRunning = false;
                }
                DestroyPipeline();
                return false;
            }
        }
        #endregion

        #region 停止摄像头
        /// <summary>
        /// 停止摄像头
        /// </summary>
        public async Task StopCameraAsync()
        {
            lock (_pipelineLock)
            {
                if (!_isRunning || _isDisposed)
                {
                    _logger.LogInformation("摄像头未运行或已释放，无需停止");
                    return;
                }

                _isRunning = false;
            }

            try
            {
                _logger.LogInformation("开始停止 GStreamer 摄像头");

                // 取消消息循环
                _cancellationTokenSource.Cancel();

                // 设置管道状态为空
                if (_pipeline != IntPtr.Zero)
                {
                    GStreamerInterop.gst_element_set_state(_pipeline, GStreamerInterop.GstState.GST_STATE_NULL);
                }

                // 等待消息循环结束
                if (_messageLoopTask != null && !_messageLoopTask.IsCompleted)
                {
                    await _messageLoopTask.ConfigureAwait(false);
                }

                // 销毁管道
                DestroyPipeline();

                // 清除最新帧缓存
                lock (_frameLock)
                {
                    _latestFrame = null;
                    _lastFrameTime = DateTime.MinValue;
                }

                _logger.LogInformation("GStreamer 摄像头停止成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 GStreamer 摄像头时发生异常");
            }
        }
        #endregion

        #region 捕获单帧
        /// <summary>
        /// 捕获单帧
        /// </summary>
        public async Task<WriteableBitmap> CaptureDisplayFrameAsync()
        {
            try
            {
                _logger.LogDebug("开始捕获单帧");

                // 检查摄像头是否在运行
                if (!_isRunning)
                {
                    _logger.LogWarning("摄像头未运行，无法捕获帧");
                    throw new InvalidOperationException("摄像头未运行");
                }

                // 等待最新帧可用
                var startTime = DateTime.Now;
                const int timeoutMs = 3000; // 3秒超时

                while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(timeoutMs))
                {
                    lock (_frameLock)
                    {
                        if (_latestFrame != null && DateTime.Now - _lastFrameTime < _frameTimeout)
                        {
                            _logger.LogDebug("成功获取最新帧");
                            return _latestFrame;
                        }
                    }
                }

                _logger.LogWarning("捕获帧超时，未收到有效帧数据");
                throw new TimeoutException("捕获帧超时");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "捕获单帧时发生异常");
                throw;
            }
        }
        #endregion

        #region 检查摄像头健康状态
        /// <summary>
        /// 检查摄像头健康状态
        /// </summary>
        public async Task<bool> CheckCameraHealthAsync()
        {
            try
            {
                _logger.LogInformation("开始检查摄像头健康状态");

                // 检查设备是否存在
                var devices = await App.ExecuteShellCommand("v4l2-ctl --list-devices");
                string devicePath = _appSettings.Camera.DevicePath;
                if (string.IsNullOrEmpty(devices) || !devices.Contains(devicePath))
                {
                    _logger.LogWarning("摄像头设备不存在: {DevicePath}", devicePath);
                    return false;
                }

                _logger.LogInformation("摄像头健康状态检查通过");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查摄像头健康状态时发生异常");
                return false;
            }
        }
        #endregion

        #region 创建 GStreamer 管道
        /// <summary>
        /// 创建 GStreamer 管道
        /// </summary>
        private async Task<bool> CreatePipeline()
        {
            try
            {
                _logger.LogInformation("开始创建 GStreamer 管道");

                // 构建管道字符串
                string pipelineString = await BuildPipelineString();
                _logger.LogInformation("GStreamer 管道字符串: {PipelineString}", pipelineString);

                // 创建管道
                _pipeline = GStreamerInterop.gst_parse_launch(pipelineString, out IntPtr error);
                if (_pipeline == IntPtr.Zero || error != IntPtr.Zero)
                {
                    string errorMsg = error != IntPtr.Zero ? Marshal.PtrToStringAnsi(error) : "未知错误";
                    _logger.LogError("创建 GStreamer 管道失败: {Error}", errorMsg);
                    if (error != IntPtr.Zero)
                    {
                        GStreamerInterop.gst_object_unref(error);
                    }
                    return false;
                }

                // 获取 appsink 元素
                _appSink = GStreamerInterop.gst_bin_get_by_name(_pipeline, "appsink");
                if (_appSink == IntPtr.Zero)
                {
                    _logger.LogError("获取 appsink 元素失败");
                    DestroyPipeline();
                    return false;
                }

                // 设置 appsink 属性
                bool propertiesSet = SetAppSinkProperties();
                if (!propertiesSet)
                {
                    _logger.LogError("设置 appsink 属性失败");
                    DestroyPipeline();
                    return false;
                }

                // 获取总线
                _bus = GStreamerInterop.gst_element_get_bus(_pipeline);
                if (_bus == IntPtr.Zero)
                {
                    _logger.LogError("获取总线失败");
                    DestroyPipeline();
                    return false;
                }

                _logger.LogInformation("GStreamer 管道创建成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 GStreamer 管道时发生异常");
                DestroyPipeline();
                return false;
            }
        }
        #endregion

        #region 构建管道字符串
        /// <summary>
        /// 构建超低延迟YUYV管道字符串
        /// </summary>
        private async Task<string> BuildPipelineString()
        {
            try
            {
                _logger.LogInformation("开始构建超低延迟 YUYV GStreamer 管道字符串");

                var cfg = _appSettings.Camera;
                string device = cfg.DevicePath;
                int w = cfg.Resolution.Width;
                int h = cfg.Resolution.Height;
                int fps = cfg.FrameRate;

                // 超低延迟YUYV管道配置
                string pipeline =
                    $"v4l2src device={device} ! " +
                    $"video/x-raw,format=YUY2,width={w},height={h},framerate={fps}/1 ! " +
                    $"videoconvert ! " +
                    $"video/x-raw,format=BGRA ! " +
                    $"appsink name=appsink " +
                    $"emit-signals=true " +
                    $"max-buffers=1 " +
                    $"drop=true " +
                    $"sync=false";

                _logger.LogInformation("YUYV管道已生成：{Pipeline}", pipeline);
                return pipeline;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "构建管道字符串时发生异常，使用默认管道");

                // 默认超低延迟管道:YUYV格式，320x240，30fps
                string defaultPipeline =
                    $"v4l2src device=/dev/video12 ! " +
                    $"video/x-raw,format=YUY2,width=320,height=240,framerate=30/1 ! " +
                    $"videoconvert ! " +
                    $"video/x-raw,format=BGRA ! " +
                    $"appsink name=appsink " +
                    $"emit-signals=true " +
                    $"max-buffers=1 " +
                    $"drop=true " +
                    $"sync=false";

                _logger.LogInformation("使用默认超低延迟YUYV管道：{DefaultPipeline}", defaultPipeline);
                return defaultPipeline;
            }
        }
        #endregion

        #region GStreamer 回调管理
        /// <summary>
        /// GStreamer 回调包装器，确保回调不会被垃圾回收
        /// </summary>
        private class GStreamerCallbackWrapper
        {
            private readonly GStreamerCameraService _service;
            private readonly ILogger<GStreamerCameraService> _logger;

            public GStreamerCallbackWrapper(GStreamerCameraService service, ILogger<GStreamerCameraService> logger)
            {
                _service = service;
                _logger = logger;
            }

            public void OnEos(IntPtr appsink, IntPtr user_data)
            {
                _logger.LogInformation("接收到 EOS (End of Stream) 信号");
                // 在后台尝试重新启动管道
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000); // 等待1秒
                    try
                    {
                        await _service.RestartPipelineAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "重启管道失败");
                    }
                });
            }

            public GStreamerInterop.GstFlowReturn OnNewPreroll(IntPtr appsink, IntPtr user_data)
            {
                _logger.LogDebug("接收到新预卷帧");
                return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
            }

            public GStreamerInterop.GstFlowReturn OnNewSample(IntPtr appsink, IntPtr user_data)
            {
                return _service.OnNewSampleInternal(appsink, user_data);
            }
        }

        // 保持回调包装器的引用，防止被垃圾回收
        private GStreamerCallbackWrapper? _callbackWrapper;
        private GStreamerInterop.GstAppSinkCallbacks _callbacks;
        #endregion

        #region 管道重启方法
        /// <summary>
        /// 重启管道
        /// </summary>
        private async Task<bool> RestartPipelineAsync()
        {
            lock (_pipelineLock)
            {
                if (_isDisposed)
                {
                    _logger.LogWarning("服务已释放，无法重启管道");
                    return false;
                }
            }

            try
            {
                _logger.LogInformation("开始重启GStreamer管道");

                // 先停止当前管道
                await StopCameraAsync();
                await Task.Delay(100); // 等待100ms确保完全停止

                // 重新启动管道
                bool success = await StartCameraAsync();

                if (success)
                {
                    _logger.LogInformation("GStreamer管道重启成功");
                }
                else
                {
                    _logger.LogError("GStreamer管道重启失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重启GStreamer管道时发生异常");
                return false;
            }
        }
        #endregion

        #region 内部的新样本处理方法
        /// <summary>
        /// 内部的新样本处理方法
        /// </summary>
        private GStreamerInterop.GstFlowReturn OnNewSampleInternal(IntPtr appsink, IntPtr user_data)
        {
            if (!_isRunning || _isDisposed)
            {
                return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
            }

            IntPtr sample = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                // 拉取样本
                sample = GStreamerInterop.gst_app_sink_pull_sample(appsink);
                if (sample == IntPtr.Zero)
                {
                    return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
                }

                // 从样本中获取缓冲区
                buffer = GStreamerInterop.gst_sample_get_buffer(sample);
                if (buffer == IntPtr.Zero)
                {
                    return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
                }

                // 映射缓冲区
                GStreamerInterop.GstMapInfo mapInfo = new GStreamerInterop.GstMapInfo();
                if (!GStreamerInterop.gst_buffer_map(buffer, ref mapInfo, GStreamerInterop.GstMapFlags.GST_MAP_READ))
                {
                    return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
                }

                try
                {
                    // 处理帧数据
                    var cameraSettings = _appSettings.Camera;
                    ProcessFrameDataDirect(mapInfo.data, (int)mapInfo.size, cameraSettings.Resolution.Width, cameraSettings.Resolution.Height);
                }
                finally
                {
                    // 确保取消映射
                    GStreamerInterop.gst_buffer_unmap(buffer, ref mapInfo);
                }

                return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理新样本时发生异常");
                return GStreamerInterop.GstFlowReturn.GST_FLOW_OK;
            }
            finally
            {
                // 确保释放样本
                if (sample != IntPtr.Zero)
                {
                    GStreamerInterop.gst_sample_unref(sample);
                }
            }
        }
        #endregion

        #region 设置 appsink 属性
        /// <summary>
        /// 设置 appsink 属性
        /// </summary>
        private bool SetAppSinkProperties()
        {
            try
            {
                _logger.LogInformation("开始设置 appsink 属性");

                _callbackWrapper = new GStreamerCallbackWrapper(this, _logger);

                GStreamerInterop.gst_app_sink_set_emit_signals(_appSink, true);
                GStreamerInterop.gst_app_sink_set_max_buffers(_appSink, 1); // 只保留最新帧
                GStreamerInterop.gst_app_sink_set_drop(_appSink, true);     // 丢弃旧帧

                _callbacks = new GStreamerInterop.GstAppSinkCallbacks
                {
                    eos = _callbackWrapper.OnEos,
                    new_preroll = _callbackWrapper.OnNewPreroll,
                    new_sample = _callbackWrapper.OnNewSample
                };

                GStreamerInterop.gst_app_sink_set_callbacks(_appSink, ref _callbacks, IntPtr.Zero, IntPtr.Zero);

                _logger.LogInformation("appsink 属性设置完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置 appsink 属性异常");
                return false;
            }
        }
        #endregion

        #region GStreamer 管道管理优化
        /// <summary>
        /// 销毁管道
        /// </summary>
        private void DestroyPipeline()
        {
            try
            {
                _logger.LogInformation("开始销毁 GStreamer 管道");

                // 先停止管道状态
                if (_pipeline != IntPtr.Zero)
                {
                    _logger.LogDebug("设置管道状态为NULL");
                    GStreamerInterop.gst_element_set_state(_pipeline, GStreamerInterop.GstState.GST_STATE_NULL);
                }

                // 清除回调防止野指针
                if (_appSink != IntPtr.Zero)
                {
                    _logger.LogDebug("清除appsink回调");
                    var emptyCallbacks = new GStreamerInterop.GstAppSinkCallbacks();
                    GStreamerInterop.gst_app_sink_set_callbacks(_appSink, ref emptyCallbacks, IntPtr.Zero, IntPtr.Zero);

                    _logger.LogDebug("释放appsink");
                    GStreamerInterop.gst_object_unref(_appSink);
                    _appSink = IntPtr.Zero;
                }

                if (_bus != IntPtr.Zero)
                {
                    _logger.LogDebug("释放bus");
                    GStreamerInterop.gst_object_unref(_bus);
                    _bus = IntPtr.Zero;
                }

                if (_pipeline != IntPtr.Zero)
                {
                    _logger.LogDebug("释放pipeline");
                    GStreamerInterop.gst_object_unref(_pipeline);
                    _pipeline = IntPtr.Zero;
                }

                // 清理回调包装器引用
                _callbackWrapper = null;

                _logger.LogInformation("GStreamer 管道销毁完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "销毁 GStreamer 管道时发生异常");
            }
        }
        #endregion

        #region 直接帧数据处理
        /// <summary>
        /// 直接帧数据处理
        /// 直接创建WriteableBitmap并立即触发事件，不进行任何缓存
        /// </summary>
        private unsafe void ProcessFrameDataDirect(IntPtr data, int size, int width, int height)
        {
            // 极速检查
            if (_isDisposed || !_isRunning || data == IntPtr.Zero || size <= 0)
            {
                return;
            }

            WriteableBitmap? newBitmap = null;
            try
            {
                // 检查数据大小是否匹配预期
                int expectedSize = width * height * 4; // BGRA格式，每个像素4字节
                if (size < expectedSize)
                {
                    _logger.LogWarning("帧数据大小不匹配，预期: {ExpectedSize}, 实际: {ActualSize}", expectedSize, size);
                    return;
                }

                // 立即创建WriteableBitmap并复制数据
                newBitmap = CreateWriteableBitmapFromData(data, size, width, height);
                if (newBitmap == null)
                {
                    return;
                }

                // 立即触发帧事件
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_isRunning && !_isDisposed && FrameDisplayCaptured != null)
                        {
                            FrameDisplayCaptured?.Invoke(this, newBitmap);
                        }
                        else
                        {
                            // 如果服务已停止，释放帧资源
                            newBitmap?.Dispose();
                        }
                    }
                    catch (Exception eventEx)
                    {
                        _logger.LogError(eventEx, "触发帧捕获事件时发生异常");
                        // 立即释放失败的帧
                        newBitmap?.Dispose();
                    }
                }, DispatcherPriority.MaxValue); // 使用最高优先级

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理帧数据时发生异常");
                newBitmap?.Dispose();
            }
        }

        /// <summary>
        /// 从数据创建WriteableBitmap 
        /// </summary>
        private unsafe WriteableBitmap CreateWriteableBitmapFromData(IntPtr data, int size, int width, int height)
        {
            try
            {
                var pixelSize = new Avalonia.PixelSize(width, height);
                var dpi = new Avalonia.Vector(96, 96);
                var newBitmap = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

                using (var lockedBitmap = newBitmap.Lock())
                {
                    if (lockedBitmap.Address == IntPtr.Zero)
                    {
                        newBitmap.Dispose();
                        return null;
                    }

                    int copySize = Math.Min(size, lockedBitmap.RowBytes * height);

                    unsafe
                    {
                        Buffer.MemoryCopy(
                            (void*)data,
                            (void*)lockedBitmap.Address,
                            copySize,
                            copySize);
                    }
                }

                return newBitmap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建WriteableBitmap时发生异常");
                return null;
            }
        }
        #endregion

        #region 处理总线消息
        /// <summary>
        /// 消息循环处理总线消息
        /// </summary>
        private void MessageLoop()
        {
            _logger.LogInformation("开始 GStreamer 消息循环");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // 等待总线消息，超时 500ms
                    IntPtr message = GStreamerInterop.gst_bus_timed_pop_filtered(
                        _bus,
                        500000000, // 500ms in nanoseconds
                        GStreamerInterop.GstMessageType.GST_MESSAGE_EOS |
                        GStreamerInterop.GstMessageType.GST_MESSAGE_ERROR |
                        GStreamerInterop.GstMessageType.GST_MESSAGE_WARNING);

                    if (message == IntPtr.Zero)
                    {
                        continue; // 超时，继续循环
                    }

                    // 处理消息
                    HandleMessage(message);

                    // 释放消息
                    GStreamerInterop.gst_message_unref(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理 GStreamer 消息时发生异常");
                }
            }

            _logger.LogInformation("GStreamer 消息循环结束");
        }
        #endregion

        #region 处理总线消息
        /// <summary>
        /// 处理总线消息
        /// </summary>
        private void HandleMessage(IntPtr message)
        {
            var messageType = GStreamerInterop.gst_message_get_type(message);

            switch (messageType)
            {
                case GStreamerInterop.GstMessageType.GST_MESSAGE_EOS:
                    _logger.LogInformation("接收到 EOS 消息，忽略EOS信号");
                    break;

                case GStreamerInterop.GstMessageType.GST_MESSAGE_ERROR:
                    HandleErrorMessage(message);
                    break;

                case GStreamerInterop.GstMessageType.GST_MESSAGE_WARNING:
                    HandleWarningMessage(message);
                    break;

                default:
                    _logger.LogDebug("接收到未处理的消息类型: {MessageType}", messageType);
                    break;
            }
        }
        #endregion

        #region 处理错误消息
        /// <summary>
        /// 处理错误消息
        /// </summary>
        private void HandleErrorMessage(IntPtr message)
        {
            IntPtr errorPtr = IntPtr.Zero;
            IntPtr debugPtr = IntPtr.Zero;
            GStreamerInterop.gst_message_parse_error(message, out errorPtr, out debugPtr);

            string error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "未知错误";
            string debug = debugPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(debugPtr) : "无调试信息";

            _logger.LogError("GStreamer 错误: {Error}, 调试信息: {Debug}", error, debug);
        }
        #endregion

        #region 处理警告消息
        /// <summary>
        /// 处理警告消息
        /// </summary>
        private void HandleWarningMessage(IntPtr message)
        {
            IntPtr warningPtr = IntPtr.Zero;
            IntPtr debugPtr = IntPtr.Zero;
            GStreamerInterop.gst_message_parse_warning(message, out warningPtr, out debugPtr);

            string warning = warningPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(warningPtr) : "未知警告";
            string debug = debugPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(debugPtr) : "无调试信息";

            _logger.LogWarning("GStreamer 警告: {Warning}, 调试信息: {Debug}", warning, debug);
        }
        #endregion

        #region 资源释放
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _logger.LogInformation("开始释放 GStreamerCameraService 资源");

            try
            {
                _isDisposed = true;
                _cancellationTokenSource.Cancel();

                // 停止摄像头
                StopCameraAsync().Wait(5000); // 等待最多 5 秒

                _cancellationTokenSource.Dispose();

                // 释放帧对象池
                while (_framePool.TryDequeue(out var bitmap))
                {
                    bitmap?.Dispose();
                }
                _logger.LogInformation("帧对象池已清空");

                _logger.LogInformation("GStreamerCameraService 资源释放完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放 GStreamerCameraService 资源时发生异常");
            }
        }
        #endregion
    }
}