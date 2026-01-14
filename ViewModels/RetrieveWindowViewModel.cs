using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceLocker.Models;
using FaceLocker.Services;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using ReactiveUI;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using static FaceLocker.Services.BaiduFaceSDKInterop; // 添加这个以使用JsonResultParser
// using FaceLocker.Services.FaceRecognitions;

namespace FaceLocker.ViewModels
{
    /// <summary>
    /// 取出物品界面视图模型
    /// </summary>
    public class RetrieveWindowViewModel : ViewModelBase, IDisposable
    {
        #region 私有字段
        private readonly ILogger<RetrieveWindowViewModel> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ICameraService _cameraService;
        private readonly IUserService _userService;
        private readonly ILockerService _lockerService;
        private readonly IAppStateService _appStateService;
        private readonly ILockControlService _lockControlService;
        private readonly IAccessLogService _accessLogService;
        private readonly BaiduFaceService _baiduFaceService;
        private readonly FaceLockerDbContext _dbContext;

        private CancellationTokenSource? _faceDetectionCts;
        private DispatcherTimer? _noFaceTimer;
        private DispatcherTimer? _returnCountdownTimer;
        private bool _isDisposed = false;

        // 超时配置（单位：秒）
        private const int NO_FACE_TIMEOUT_SECONDS = 15;      // 15秒未识别到人脸
        private const int RETURN_COUNTDOWN_SECONDS = 10;    // 返回倒计时（用于提示）
        private const int SUBMISSION_TIMEOUT_SECONDS = 180; // 3分钟未完成取出操作

        private DispatcherTimer? _submissionTimeoutTimer;
        private DateTime _windowOpenedTime;
        private bool _isSubmissionTimeoutActive = false;
        private int _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS;

        // 人脸识别相关字段
        private bool _isRecognitionRunning = false;    // 是否在人脸识别中
        private bool _isRecognitionCompleted = false;  // 是否完成人脸识别
        private DateTime _lastFaceDetectionTime = DateTime.Now;
        private const int FACE_DETECTION_INTERVAL_MS = 100; // 优化：100ms间隔，每秒10次检测，减少CPU负载，配合卡尔曼预测保持流畅
        private User? _recognizedUser = null;  // 已识别用户
        private byte[]? _recognizedCapturedFaceImage = null;
        private bool _hasDetectedFaceInCycle = false;
        private bool _isCameraReady = false;
        private bool _isEventSubscribed = false;

        private readonly SemaphoreSlim _faceDetectionLock = new(1, 1);
        private WrapperFaceBox[] _currentFaces = [];
        private readonly object _faceLock = new();
        // FaceTracker（预测/平滑）已移除：主路径由 GStreamer cairooverlay 同步绘制

        // 软件渲染占位图已移除（主路径不再把帧渲染到 UI）

        // 人脸检测预分配缓冲区 - 避免每帧分配内存
        private byte[]? _faceDetectionBuffer;
        private int _faceDetectionBufferSize = 0;

        // 性能监控
        private int _frameCount = 0;
        private double _totalFrameProcessTimeMs = 0;
        private DateTime _lastFpsTime = DateTime.Now;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化取出物品界面视图模型
        /// </summary>
        public RetrieveWindowViewModel(
            ILogger<RetrieveWindowViewModel> logger,
            ILoggerFactory loggerFactory,
            ICameraService cameraService,
            ILockerService lockerService,
            IUserService userService,
            IAppStateService appStateService,
            ILockControlService lockControlService,
            IAccessLogService accessLogService,
            BaiduFaceService baiduFaceService,
            FaceLockerDbContext dbContext)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _lockerService = lockerService ?? throw new ArgumentNullException(nameof(lockerService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _appStateService = appStateService ?? throw new ArgumentNullException(nameof(appStateService));
            _lockControlService = lockControlService ?? throw new ArgumentNullException(nameof(lockControlService));
            _accessLogService = accessLogService ?? throw new ArgumentNullException(nameof(accessLogService));
            _baiduFaceService = baiduFaceService ?? throw new ArgumentNullException(nameof(baiduFaceService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            _logger.LogInformation("RetrieveWindowViewModel 开始初始化");

            // 记录摄像头服务状态
            _logger.LogInformation("摄像头服务类型: {CameraServiceType}, 可用状态: {IsCameraAvailable}",
                _cameraService.GetType().Name, _cameraService.IsCameraAvailable);

            // 记录百度人脸服务状态
            _logger.LogInformation("百度人脸服务初始化状态: {IsInitialized}", _baiduFaceService.IsInitialized);

            // 初始化属性
            InitializeProperties();

            // 初始化命令
            InitializeCommands();

            // 不再维护 CameraVideoElement（软件渲染预览已移除）

            _logger.LogInformation("RetrieveWindowViewModel 初始化完成");
        }
        #endregion

        #region 界面属性
        /// <summary>
        /// 当前识别的用户
        /// </summary>
        public User? RecognizedUser
        {
            get => _recognizedUser;
            private set => this.RaiseAndSetIfChanged(ref _recognizedUser, value);
        }

        /// <summary>
        /// 当前用户分配的柜格
        /// </summary>
        private UserLocker? _userLocker;
        public UserLocker? UserLocker
        {
            get => _userLocker;
            private set => this.RaiseAndSetIfChanged(ref _userLocker, value);
        }

        /// <summary>
        /// 当前用户头像
        /// </summary>
        private Bitmap? _userAvatar;
        public Bitmap? UserAvatar
        {
            get => _userAvatar;
            private set => this.RaiseAndSetIfChanged(ref _userAvatar, value);
        }

        /// <summary>
        /// 手机号显示
        /// </summary>
        private string? _phoneNumber;
        public string? PhoneNumber
        {
            get => _phoneNumber;
            private set => this.RaiseAndSetIfChanged(ref _phoneNumber, value);
        }

        /// <summary>
        /// 存放时间显示
        /// </summary>
        private string? _storageTime;
        public string? StorageTime
        {
            get => _storageTime;
            private set => this.RaiseAndSetIfChanged(ref _storageTime, value);
        }

        /// <summary>
        /// 存放柜格名称显示
        /// </summary>
        private string? _lockerName;
        public string? LockerName
        {
            get => _lockerName;
            private set => this.RaiseAndSetIfChanged(ref _lockerName, value);
        }

        /// <summary>
        /// 系统提示信息
        /// </summary>
        private string _systemPrompt = "请正对摄像头";
        public string SystemPrompt
        {
            get => _systemPrompt;
            set => this.RaiseAndSetIfChanged(ref _systemPrompt, value);
        }

        /// <summary>
        /// 倒计时文本
        /// </summary>
        private string _countdownText = "倒计时 10 秒后返回主界面";
        public string CountdownText
        {
            get => _countdownText;
            set => this.RaiseAndSetIfChanged(ref _countdownText, value);
        }

        /// <summary>
        /// 用户是否已识别
        /// </summary>
        private bool _isUserRecognized = false;
        public bool IsUserRecognized
        {
            get => _isUserRecognized;
            private set => this.RaiseAndSetIfChanged(ref _isUserRecognized, value);
        }

        /// <summary>
        /// 确认开柜按钮是否可用
        /// </summary>
        private bool _isConfirmOpenLockerEnabled = false;
        public bool IsConfirmOpenLockerEnabled
        {
            get => _isConfirmOpenLockerEnabled;
            set => this.RaiseAndSetIfChanged(ref _isConfirmOpenLockerEnabled, value);
        }

        // CameraVideoElement（软件渲染预览）已移除：主路径使用 NativeVideoHost 显示视频

        /// <summary>
        /// 是否显示摄像头未连接提示
        /// </summary>
        private bool _showCameraNotConnected = false;
        public bool ShowCameraNotConnected
        {
            get => _showCameraNotConnected;
            private set => this.RaiseAndSetIfChanged(ref _showCameraNotConnected, value);
        }
        
        // 主路径：NativeVideoHost + GStreamer cairooverlay 绘制人脸框（同步）
        // 不再保留 UseNativeVideoMode/FaceBoxes/软件渲染备用分支，避免代码混淆

        /// <summary>
        /// 摄像头宽度（用于坐标转换，与人脸检测帧尺寸一致）
        /// </summary>
        private int _cameraWidth = 640;
        public int CameraWidth
        {
            get => _cameraWidth;
            set => this.RaiseAndSetIfChanged(ref _cameraWidth, value);
        }

        /// <summary>
        /// 摄像头高度（用于坐标转换，与人脸检测帧尺寸一致）
        /// </summary>
        private int _cameraHeight = 360;
        public int CameraHeight
        {
            get => _cameraHeight;
            set => this.RaiseAndSetIfChanged(ref _cameraHeight, value);
        }
        
        // #endregion  // 备用 UI 覆盖层相关属性已移除

        /// <summary>
        /// 是否显示人脸检测超时提示
        /// </summary>
        private bool _isFaceDetectionTimeout = false;
        public bool IsFaceDetectionTimeout
        {
            get => _isFaceDetectionTimeout;
            private set => this.RaiseAndSetIfChanged(ref _isFaceDetectionTimeout, value);
        }

        /// <summary>
        /// 人脸检测剩余秒数
        /// </summary>
        private int _faceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
        public int FaceDetectionRemainingSeconds
        {
            get => _faceDetectionRemainingSeconds;
            set => this.RaiseAndSetIfChanged(ref _faceDetectionRemainingSeconds, value);
        }
        #endregion

        #region 命令定义
        /// <summary>
        /// 确认开柜命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ConfirmOpenLockerCommand { get; private set; } = null!;

        /// <summary>
        /// 返回主界面命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ReturnToMainCommand { get; private set; } = null!;
        #endregion

        #region 初始化方法
        /// <summary>
        /// 初始化属性
        /// </summary>
        private void InitializeProperties()
        {
            try
            {
                _logger.LogDebug("初始化属性");

                // 重置所有属性
                RecognizedUser = null;
                UserLocker = null;
                UserAvatar = null;
                PhoneNumber = null;
                StorageTime = null;
                LockerName = null;
                SystemPrompt = "请正对摄像头";
                CountdownText = "倒计时 10 秒后返回主界面";
                IsUserRecognized = false;
                IsConfirmOpenLockerEnabled = false;
                ShowCameraNotConnected = false;
                IsFaceDetectionTimeout = false;
                FaceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;

                _logger.LogDebug("属性初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化属性时发生异常");
                throw;
            }
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitializeCommands()
        {
            try
            {
                _logger.LogDebug("初始化命令");

                // 确认开柜命令 - 仅在用户识别成功后可用
                var canConfirmOpenLocker = this.WhenAnyValue(x => x.IsConfirmOpenLockerEnabled);
                ConfirmOpenLockerCommand = ReactiveCommand.CreateFromTask(ExecuteConfirmOpenLockerAsync, canConfirmOpenLocker);

                // 返回主界面命令
                ReturnToMainCommand = ReactiveCommand.CreateFromTask(ExecuteReturnToMainAsync);

                _logger.LogInformation("命令初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化命令时发生异常");
                throw;
            }
        }
        #endregion

        #region 事件订阅管理
        private void SafeSubscribeCameraEvents()
        {
            if (_isEventSubscribed)
            {
                _logger.LogDebug("摄像头事件已经订阅，跳过重复订阅");
                return;
            }

            try
            {
                _cameraService.FrameDisplayCaptured += OnCameraFrameCaptured;
                _isEventSubscribed = true;
                _logger.LogInformation("已订阅摄像头帧捕获事件");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅摄像头事件时发生异常");
            }
        }

        private void SafeUnsubscribeCameraEvents()
        {
            if (!_isEventSubscribed)
            {
                _logger.LogDebug("摄像头事件未订阅，无需退订");
                return;
            }

            try
            {
                _cameraService.FrameDisplayCaptured -= OnCameraFrameCaptured;
                _isEventSubscribed = false;
                _logger.LogInformation("已退订摄像头帧捕获事件");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退订摄像头事件时发生异常");
            }
        }
        #endregion

        #region 窗口激活时调用的初始化方法
        /// <summary>
        /// 窗口激活时调用的初始化方法
        /// </summary>
        public async Task InitializeWindowAsync()
        {
            try
            {
                _logger.LogInformation("开始初始化取出物品窗口");

                // 重置所有状态
                InitializeProperties();

                // 记录窗口打开时间
                _windowOpenedTime = DateTime.Now;

                // 启动摄像头显示 - 关键修复：使用与StoreWindowViewModel相同的逻辑
                await StartCameraDisplay();

                // 启动人脸识别
                await StartFaceRecognitionAsync();

                // 启动人脸检测超时定时器（在摄像头就绪后启动）
                if (_isCameraReady)
                {
                    StartNoFaceDetectionTimer();
                }

                // 启动提交超时定时器（3分钟未完成取出操作）
                StartSubmissionTimeoutTimer();

                _logger.LogInformation("取出物品窗口初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化取出物品窗口时发生异常");
                throw;
            }
        }
        
        /// <summary>
        /// 设置原生视频窗口句柄（由 NativeVideoHost 回调）
        /// 此方法在 X11 窗口创建后被调用，用于零拷贝视频渲染
        /// </summary>
        /// <param name="x11WindowId">X11 窗口 ID</param>
        public async Task SetNativeVideoWindowAsync(ulong x11WindowId)
        {
            try
            {
                _logger.LogInformation("收到原生视频窗口句柄: 0x{Handle:X}", x11WindowId);
                
                // 检查摄像头服务是否支持原生视频模式
                if (_cameraService is INativeVideoCameraService nativeService)
                {
                    _logger.LogInformation("摄像头服务支持原生视频模式，正在设置窗口...");
                    
                    // 原生视频模式 + GStreamer cairooverlay 绘制人脸框（推荐）
                    // 人脸框直接在 GStreamer 管道中绘制，无需透明窗口
                    bool windowSet = await nativeService.SetWindowAsync(x11WindowId);
                    if (windowSet)
                    {
                        _logger.LogInformation("原生视频模式已启用（人脸框由 GStreamer cairooverlay 同步绘制）");
                        
                        // 如果摄像头已启动但未播放，启动播放
                        if (_isCameraReady)
                        {
                            await nativeService.StartPlaybackAsync();
                        }
                    }
                    else
                    {
                        _logger.LogError("设置原生视频窗口失败（当前版本不再提供软件渲染备用路径）");
                        ShowCameraNotConnected = true;
                        SystemPrompt = "原生视频窗口初始化失败";
                    }
                }
                else
                {
                    _logger.LogError("摄像头服务不支持原生视频模式（当前版本不再提供软件渲染备用路径）");
                    ShowCameraNotConnected = true;
                    SystemPrompt = "摄像头服务不支持原生视频模式";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置原生视频窗口时发生异常");
                ShowCameraNotConnected = true;
                SystemPrompt = "原生视频窗口初始化异常";
            }
        }
        #endregion

        #region 摄像头相关方法
        /// <summary>
        /// 启动摄像头显示
        /// </summary>
        private async Task StartCameraDisplay()
        {
            if (_isCameraReady)
            {
                _logger.LogDebug("摄像头已就绪，跳过重复初始化");
                return;
            }

            _logger.LogInformation("开始启动摄像头显示");

            try
            {
                // 显示摄像头连接中提示
                ShowCameraNotConnected = true;
                SystemPrompt = "正在启动摄像头...";

                // 先停止可能正在运行的摄像头
                await _cameraService.StopCameraAsync();
                await Task.Delay(200);

                // 检查摄像头健康状态
                _logger.LogInformation("检查摄像头健康状态");
                var healthCheckResult = await _cameraService.CheckCameraHealthAsync();
                _logger.LogInformation("摄像头健康检查结果: {HealthCheckResult}", healthCheckResult);

                if (!healthCheckResult)
                {
                    _logger.LogWarning("摄像头健康检查失败");
                    ShowCameraNotConnected = true;
                    SystemPrompt = "摄像头设备异常";
                    return;
                }

                // 启动摄像头
                _logger.LogInformation("摄像头健康检查通过，开始启动摄像头");
                var startResult = await _cameraService.StartCameraAsync();
                _logger.LogInformation("摄像头启动结果: {StartResult}", startResult);

                if (!startResult)
                {
                    _logger.LogWarning("启动摄像头失败");
                    ShowCameraNotConnected = true;
                    SystemPrompt = "启动摄像头失败";
                    return;
                }

                // 订阅摄像头事件
                SafeSubscribeCameraEvents();

                // 更新状态
                _isCameraReady = true;
                ShowCameraNotConnected = false;
                SystemPrompt = "请正对摄像头";

                _logger.LogInformation("摄像头显示启动成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动摄像头显示时发生异常");
                ShowCameraNotConnected = true;
                SystemPrompt = "摄像头初始化异常";
                SafeUnsubscribeCameraEvents();
            }
        }

        /// <summary>
        /// 摄像头帧捕获事件处理 - 优化版：安全复制帧数据，绘制人脸框
        /// </summary>
        private void OnCameraFrameCaptured(object? sender, WriteableBitmap frame)
        {
            if (frame == null || _isDisposed) return;

            var frameStartTime = DateTime.Now;

            try
            {
                // 获取帧尺寸
                int frameWidth = frame.PixelSize.Width;
                int frameHeight = frame.PixelSize.Height;
                // 主路径：视频由 NativeVideoHost 显示，人脸框由 GStreamer cairooverlay 绘制

                // 如果是第一次收到帧，更新连接状态
                if (ShowCameraNotConnected)
                {
                    ShowCameraNotConnected = false;
                    _logger.LogInformation("摄像头连接成功");
                }

                // 性能统计
                _frameCount++;
                _totalFrameProcessTimeMs += (DateTime.Now - frameStartTime).TotalMilliseconds;
                var elapsed = (DateTime.Now - _lastFpsTime).TotalSeconds;
                if (elapsed >= 5.0)
                {
                    var currentFps = _frameCount / elapsed;
                    var avgFrameTime = _totalFrameProcessTimeMs / (double)_frameCount;
                    var msg = $"[取物性能] 显示FPS: {currentFps:F1}, 平均帧处理: {avgFrameTime:F2}ms, 总帧数: {_frameCount}";
                    _logger.LogInformation(msg);
                    _frameCount = 0;
                    _totalFrameProcessTimeMs = 0;
                    _lastFpsTime = DateTime.Now;
                }

                // 如果识别已完成，不再进行人脸检测
                if (_isRecognitionCompleted)
                {
                    return;
                }

                // 异步进行人脸检测（不阻塞显示）
                ScheduleFaceDetectionAsync(frame, frameWidth, frameHeight);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理摄像头帧捕获事件时发生异常");
            }
        }

        /// <summary>
        /// 异步调度人脸检测 - 使用预分配缓冲区，安全复制帧数据后在后台线程处理
        /// </summary>
        private void ScheduleFaceDetectionAsync(WriteableBitmap frame, int frameWidth, int frameHeight)
        {
            // 限制人脸检测频率
            var now = DateTime.Now;
            var elapsedMs = (now - _lastFaceDetectionTime).TotalMilliseconds;
            if (elapsedMs < FACE_DETECTION_INTERVAL_MS)
            {
                return;
            }

            _lastFaceDetectionTime = now;

            // 快速复制帧数据（使用预分配缓冲区，减少GC压力）
            byte[]? frameData = null;
            int rowBytes = 0;
            try
            {
                using (var fb = frame.Lock())
                {
                    if (fb.Address != IntPtr.Zero)
                    {
                        rowBytes = fb.RowBytes;
                        int totalSize = rowBytes * frameHeight;
                        
                        // 重用或扩展预分配缓冲区
                        if (_faceDetectionBuffer == null || _faceDetectionBufferSize < totalSize)
                        {
                            _faceDetectionBuffer = new byte[totalSize];
                            _faceDetectionBufferSize = totalSize;
                        }
                        
                        System.Runtime.InteropServices.Marshal.Copy(fb.Address, _faceDetectionBuffer, 0, totalSize);
                        
                        // 创建数据副本用于后台线程（避免竞争）
                        frameData = new byte[totalSize];
                        Buffer.BlockCopy(_faceDetectionBuffer, 0, frameData, 0, totalSize);
                    }
                }
            }
            catch (Exception)
            {
                return; // 锁定失败，跳过本帧
            }

            if (frameData == null) return;

            // 在后台线程进行人脸检测（使用复制的数据）
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFaceDetectionFromBytesAsync(frameData, frameWidth, frameHeight, rowBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "后台人脸检测时发生异常");
                }
            });
        }
        #endregion

        #region 人脸识别核心逻辑
        /// <summary>
        /// 启动人脸识别
        /// </summary>
        private async Task StartFaceRecognitionAsync()
        {
            try
            {
                _logger.LogInformation("开始启动人脸识别");

                if (_isRecognitionRunning)
                {
                    _logger.LogDebug("人脸识别已在运行中，跳过重复启动");
                    return;
                }

                _isRecognitionRunning = true;
                _isRecognitionCompleted = false;

                // 重置识别状态
                _recognizedUser = null;
                _recognizedCapturedFaceImage = null;
                _hasDetectedFaceInCycle = false;


                SystemPrompt = "人脸识别中...";

                // 清空人脸框
                lock (_faceLock)
                {
                    _currentFaces = [];
                }

                // 创建取消令牌源
                _faceDetectionCts = new CancellationTokenSource();

                _logger.LogInformation("人脸识别已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动人脸识别时发生异常");
                _isRecognitionRunning = false;
            }
        }

        /// <summary>
        /// 从字节数组处理人脸检测和识别（安全线程方式）
        /// </summary>
        private async Task ProcessFaceDetectionFromBytesAsync(byte[] frameData, int width, int height, int rowBytes)
        {
            if (_isDisposed || !_isCameraReady) return;
            if (!_isRecognitionRunning || _faceDetectionCts?.IsCancellationRequested == true)
            {
                return;
            }

            // 使用信号量防止并发处理
            if (!await _faceDetectionLock.WaitAsync(0, _faceDetectionCts?.Token ?? CancellationToken.None))
            {
                _logger.LogDebug("人脸检测正在处理中，跳过当前帧");
                return;
            }

            Mat? mat = null;
            try
            {
                // 重置人脸检测标志
                _hasDetectedFaceInCycle = false;

                // 从字节数组创建Mat（BGRA格式）
                mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, frameData);
                if (mat == null || mat.Empty())
                {
                    _logger.LogDebug("转换Mat失败或Mat为空");
                    return;
                }

                // 检测人脸
                var detectionResult = await _baiduFaceService.DetectFacesOnlyAsync(mat);
                
                // 更新卡尔曼滤波追踪器（平滑人脸框跟踪）
                // 由 native cairooverlay 绘制人脸框，此处不再做 UI 侧追踪绘制
                
                lock (_faceLock)
                {
                    _currentFaces = detectionResult.FaceBoxes ?? [];
                }

                // 更新人脸框覆盖层（用于原生视频模式）
                UpdateFaceBoxOverlay(detectionResult.FaceBoxes);

                if (detectionResult.FaceBoxes == null || detectionResult.FaceBoxes.Length == 0)
                {
                    _logger.LogDebug("未检测到人脸");
                    return;
                }

                _logger.LogDebug("检测到 {FaceCount} 个人脸", detectionResult.FaceBoxes.Length);
                _hasDetectedFaceInCycle = true;

                // 重置无人脸检测超时定时器
                await Dispatcher.UIThread.InvokeAsync(() => ResetNoFaceDetectionTimer());

                // 提取置信度最高的人脸进行识别（按分数排序）
                var highestScoreFace = detectionResult.FaceBoxes.OrderByDescending(f => f.score).FirstOrDefault();
                _logger.LogDebug("选择置信度最高的人脸进行识别，分数: {Score}", highestScoreFace.score);

                // 保存当前帧作为用户头像（使用 OpenCV 编码为 PNG）
                try
                {
                    _recognizedCapturedFaceImage = mat.ToBytes(".png");
                    _logger.LogDebug("已保存当前帧作为用户头像，大小: {Size} bytes", _recognizedCapturedFaceImage.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "保存用户头像时发生异常");
                }

                // 进行人脸识别
                await ProcessFaceRecognitionAsync(mat, detectionResult.FaceBoxes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理人脸检测时发生异常");
            }
            finally
            {
                mat?.Dispose();
                _faceDetectionLock.Release();
            }
        }

        /// <summary>
        /// 更新人脸框覆盖层数据（用于原生视频模式）
        /// </summary>
        private void UpdateFaceBoxOverlay(WrapperFaceBox[]? faces)
        {
            // UI 叠加绘制方案已移除：人脸框由 GStreamer cairooverlay 同步绘制到视频帧
            if (_cameraService is INativeVideoCameraService nativeService)
            {
                if (faces == null || faces.Length == 0)
                {
                    nativeService.ClearFaceBoxes();
                }
                else
                {
                    var gstBoxes = faces.Select(f => new NativeVideoCameraService.GstFaceBox
                    {
                        center_x = f.center_x,
                        center_y = f.center_y,
                        width = f.width,
                        height = f.height,
                        score = f.score
                    }).ToArray();
                    nativeService.SetFaceBoxes(gstBoxes, CameraWidth, CameraHeight);
                }
            }
        }

        /// <summary>
        /// 处理人脸识别
        /// </summary>
        private async Task ProcessFaceRecognitionAsync(Mat mat, WrapperFaceBox[] detectedFaces)
        {
            try
            {
                _logger.LogInformation("开始进行人脸识别");

                // 使用百度人脸服务进行识别（1:N识别，不指定角色，使用用户角色Id 4）
                var recognitionResult = await _baiduFaceService.RecognizeFaceWithRoleAsync(mat, 4);

                // 检查是否是错误
                if (!recognitionResult.Success && !string.IsNullOrEmpty(recognitionResult.JsonResult))
                {
                    if (recognitionResult.JsonResult.Contains("\"errno\" : -1005") || recognitionResult.JsonResult.Contains("\"msg\" : \"record not exist\""))
                    {
                        _logger.LogWarning("人脸识别失败：人脸库中没有注册记录");
                        SystemPrompt = "人脸匹配失败\n请先进行存入物品操作";

                        // 停止人脸识别但继续绘制人脸框
                        StopFaceRecognition();
                        return;
                    }
                }

                // 解析JSON结果
                if (recognitionResult.Success && !string.IsNullOrEmpty(recognitionResult.JsonResult))
                {
                    var result = JsonResultParser.ParseFaceRecognitionResponse(recognitionResult.JsonResult);
                    if (result != null && result.UserList != null && result.UserList.Any())
                    {
                        // 获取分数最高的用户
                        var bestUser = result.UserList.OrderByDescending(u => u.Score).FirstOrDefault();
                        if (bestUser != null && bestUser.Score >= 80.0f)
                        {
                            // 根据用户ID查询用户信息
                            var user = await _userService.GetUserByIdAsync(bestUser.UserId);
                            if (user == null)
                            {
                                _logger.LogWarning("用户ID {UserId} 在数据库中不存在", bestUser.UserId);
                                SystemPrompt = "用户信息不存在\n请先进行存入物品操作";

                                // 停止人脸识别但继续绘制人脸框
                                StopFaceRecognition();
                                return;
                            }

                            // 保存识别结果
                            _recognizedUser = user;

                            // 加载用户头像（从Avatar Base64字符串）
                            if (!string.IsNullOrEmpty(user.Avatar))
                            {
                                try
                                {
                                    var avatarBytes = Convert.FromBase64String(user.Avatar);
                                    using (var memoryStream = new MemoryStream(avatarBytes))
                                    {
                                        UserAvatar = new Bitmap(memoryStream);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "加载用户头像时发生异常");
                                    UserAvatar = null;
                                }
                            }
                            else
                            {
                                UserAvatar = null;
                            }

                            // 查询用户的柜格信息
                            var userLockers = await _lockerService.GetUserLockersAsync(bestUser.UserId);
                            if (userLockers == null || !userLockers.Any())
                            {
                                _logger.LogWarning("用户 {UserId} 没有分配柜格", bestUser.UserId);
                                SystemPrompt = "用户未分配柜格\n请先进行存入物品操作";

                                // 停止人脸识别但继续绘制人脸框
                                StopFaceRecognition();
                                return;
                            }

                            // 查找状态为已存储的柜格
                            var storedLocker = userLockers.FirstOrDefault(ul => ul.StorageStatus == StorageStatus.Stored);
                            if (storedLocker == null)
                            {
                                _logger.LogWarning("用户 {UserId} 没有未取出的物品", bestUser.UserId);
                                SystemPrompt = "人脸识别成功\n您没有未取出的物品";

                                // 停止人脸识别但继续绘制人脸框
                                StopFaceRecognition();
                                return;
                            }

                            var locker = await _lockerService.GetLockerAsync(storedLocker.LockerId);
                            if (locker != null)
                            {
                                storedLocker.Locker = locker;
                            }

                            // 更新界面显示
                            await UpdateRecognitionResultAsync(user, storedLocker);

                            // 停止人脸识别但继续绘制人脸框
                            StopFaceRecognition();

                            _logger.LogInformation("人脸识别流程完成，用户 {UserName} 已识别", user.Name);
                            return;
                        }
                    }
                }

                _logger.LogDebug("人脸识别失败，未找到匹配用户");
                SystemPrompt = "未找到匹配用户\n请先进行存入物品操作";

                // 停止人脸识别但继续绘制人脸框
                StopFaceRecognition();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理人脸识别时发生异常");
                SystemPrompt = "人脸识别过程中发生错误，请重试";
            }
        }

        /// <summary>
        /// 更新识别结果到界面
        /// </summary>
        private async Task UpdateRecognitionResultAsync(User user, UserLocker userLocker)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _logger.LogInformation("更新识别结果到界面，用户: {UserName}, 柜格: {LockerName}", user.Name, userLocker.Locker?.LockerName);

                    // 更新属性
                    RecognizedUser = user;
                    UserLocker = userLocker;
                    PhoneNumber = user.PhoneNumber;
                    StorageTime = userLocker.StoredTime.ToString("yyyy-MM-dd HH:mm:ss");
                    LockerName = userLocker.Locker?.LockerName ?? "未知柜格";
                    IsUserRecognized = true;

                    _isRecognitionRunning = false;
                    _isRecognitionCompleted = true;

                    // 更新系统提示
                    SystemPrompt = "";

                    // 启用确认开柜按钮
                    IsConfirmOpenLockerEnabled = true;

                    // 停止人脸检测超时定时器
                    StopNoFaceDetectionTimer();



                    _logger.LogInformation("界面更新完成");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新识别结果到界面时发生异常");
                }
            });
        }

        /// <summary>
        /// 停止人脸识别 - 只停止识别，不停止绘制
        /// </summary>
        private void StopFaceRecognition()
        {
            try
            {
                _logger.LogInformation("停止人脸识别");

                _isRecognitionRunning = false;
                _isRecognitionCompleted = true;

                // 取消人脸检测
                _faceDetectionCts?.Cancel();
                _faceDetectionCts?.Dispose();
                _faceDetectionCts = null;

                _logger.LogInformation("人脸识别已停止，但继续显示视频画面和人脸框");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止人脸识别时发生异常");
            }
        }
        #endregion

        #region 超时定时器管理
        /// <summary>
        /// 启动无人脸检测超时定时器
        /// </summary>
        private void StartNoFaceDetectionTimer()
        {
            try
            {
                _logger.LogInformation("启动无人脸检测超时定时器（{TimeoutSeconds}秒）", NO_FACE_TIMEOUT_SECONDS);

                StopNoFaceDetectionTimer();

                _noFaceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };

                _faceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                IsFaceDetectionTimeout = false;

                _noFaceTimer.Tick += (sender, e) =>
                {
                    try
                    {
                        if (_hasDetectedFaceInCycle)
                        {
                            // 如果本轮检测到了人脸，重置计时器
                            _faceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                            _hasDetectedFaceInCycle = false;
                            IsFaceDetectionTimeout = false;
                            return;
                        }

                        _faceDetectionRemainingSeconds--;

                        if (_faceDetectionRemainingSeconds <= 10)
                        {
                            IsFaceDetectionTimeout = true;
                        }

                        if (_faceDetectionRemainingSeconds <= 0)
                        {
                            _logger.LogInformation("无人脸检测超时，返回主界面");
                            StopNoFaceDetectionTimer();
                            _ = ExecuteReturnToMainAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理无人脸检测超时定时器Tick事件时发生异常");
                    }
                };

                _noFaceTimer.Start();
                _logger.LogDebug("无人脸检测超时定时器已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动无人脸检测超时定时器时发生异常");
            }
        }

        /// <summary>
        /// 重置无人脸检测超时定时器
        /// </summary>
        private void ResetNoFaceDetectionTimer()
        {
            try
            {
                if (_noFaceTimer != null && _noFaceTimer.IsEnabled)
                {
                    _faceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                    IsFaceDetectionTimeout = false;
                    _logger.LogDebug("无人脸检测超时定时器已重置");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置无人脸检测超时定时器时发生异常");
            }
        }

        /// <summary>
        /// 停止无人脸检测超时定时器
        /// </summary>
        private void StopNoFaceDetectionTimer()
        {
            try
            {
                if (_noFaceTimer != null)
                {
                    _noFaceTimer.Stop();
                    _noFaceTimer = null;
                    IsFaceDetectionTimeout = false;
                    _logger.LogDebug("无人脸检测超时定时器已停止");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止无人脸检测超时定时器时发生异常");
            }
        }

        /// <summary>
        /// 启动提交超时定时器（3分钟未完成取出操作）
        /// </summary>
        private void StartSubmissionTimeoutTimer()
        {
            try
            {
                _logger.LogInformation("启动提交超时定时器（{TimeoutSeconds}秒）", SUBMISSION_TIMEOUT_SECONDS);

                StopSubmissionTimeoutTimer();

                _submissionTimeoutTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };

                _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS;
                _isSubmissionTimeoutActive = true;

                _submissionTimeoutTimer.Tick += (sender, e) =>
                {
                    try
                    {
                        _submissionRemainingSeconds--;

                        // 更新倒计时文本
                        CountdownText = $"倒计时 {_submissionRemainingSeconds} 秒后返回主界面";

                        if (_submissionRemainingSeconds <= 0)
                        {
                            _logger.LogInformation("提交超时，返回主界面");
                            StopSubmissionTimeoutTimer();
                            _ = ExecuteReturnToMainAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理提交超时定时器Tick事件时发生异常");
                    }
                };

                _submissionTimeoutTimer.Start();
                _logger.LogDebug("提交超时定时器已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动提交超时定时器时发生异常");
            }
        }

        /// <summary>
        /// 停止提交超时定时器
        /// </summary>
        private void StopSubmissionTimeoutTimer()
        {
            try
            {
                if (_submissionTimeoutTimer != null)
                {
                    _submissionTimeoutTimer.Stop();
                    _submissionTimeoutTimer = null;
                    _isSubmissionTimeoutActive = false;
                    _logger.LogDebug("提交超时定时器已停止");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止提交超时定时器时发生异常");
            }
        }
        #endregion

        #region 开启柜门
        /// <summary>
        /// 开启柜门
        /// </summary>
        private async Task ExecuteConfirmOpenLockerAsync()
        {
            try
            {
                _logger.LogInformation("开始执行确认开柜操作");

                if (UserLocker == null || RecognizedUser == null)
                {
                    _logger.LogWarning("用户或柜格信息为空，无法开柜");
                    SystemPrompt = $"开柜失败\n用户或柜格信息为空";
                    return;
                }

                // 禁用按钮防止重复点击
                IsConfirmOpenLockerEnabled = false;

                // 停止所有定时器
                StopNoFaceDetectionTimer();
                StopSubmissionTimeoutTimer();

                // 获取柜格详细信息
                var locker = await _lockerService.GetLockerAsync(UserLocker.LockerId);
                if (locker == null)
                {
                    _logger.LogError("获取柜格信息失败，柜格ID: {LockerId}", UserLocker.LockerId);
                    SystemPrompt = $"开柜失败\n柜格信息不存在";

                    // 重新启用按钮
                    IsConfirmOpenLockerEnabled = true;
                    return;
                }

                var user = await _userService.GetUserByIdAsync(UserLocker.UserId);
                if (user == null)
                {
                    SystemPrompt = "开柜失败\n用户信息不存在";
                    // 重新启用按钮
                    IsConfirmOpenLockerEnabled = true;
                    return;
                }

                // 执行开柜操作
                bool openSuccess = await _lockControlService.OpenLockAsync(locker.BoardAddress, locker.ChannelNumber);
                if (!openSuccess)
                {
                    _logger.LogError("开柜操作失败，柜格ID: {LockerId}, 板地址: {BoardAddress}, 通道: {ChannelNumber}", locker.LockerId, locker.BoardAddress, locker.ChannelNumber);
                    SystemPrompt = $"{locker.LockerName} 开柜失败\n柜门开启失败，请检查硬件连接";

                    // 重新启用按钮
                    IsConfirmOpenLockerEnabled = true;
                    return;
                }

                _logger.LogInformation("开柜成功，柜格ID: {LockerId}, 用户ID: {UserId}", UserLocker.LockerId, RecognizedUser.Id);

                await _accessLogService.LogAccessAsync(user.Id, user.UserName, locker.LockerId, locker.LockerName, AccessAction.Rerieve, AccessResult.Success, $"取出物品");

                // 更新柜格状态为已取出
                bool updateSuccess = await _lockerService.UpdateUserLockerStatusAsync(_userLocker.UserLockerId, StorageStatus.Retrieved);
                if (!updateSuccess)
                {
                    _logger.LogWarning("更新柜格状态失败，柜格ID: {LockerId}", UserLocker.LockerId);
                }

                // 显示成功提示
                SystemPrompt = $"{locker.LockerName} 柜门已开启\n请取出物品";

                await ExecuteReturnToMainAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行确认开柜操作时发生异常");
                SystemPrompt = "开柜失败\n开柜过程中发生错误";

                // 重新启用按钮
                IsConfirmOpenLockerEnabled = true;
            }
        }
        #endregion

        #region 返回主界面操作
        /// <summary>
        /// 返回主界面操作
        /// </summary>
        public async Task ExecuteReturnToMainAsync()
        {
            try
            {
                _logger.LogInformation("开始执行返回主界面操作");

                // 停止所有定时器
                StopNoFaceDetectionTimer();
                StopSubmissionTimeoutTimer();
                StopFaceRecognition();

                // 取消摄像头事件订阅
                SafeUnsubscribeCameraEvents();

                // 停止摄像头 - 仅在窗口关闭时停止
                await _cameraService.StopCameraAsync();

                // 通知AppStateService返回主界面
                _appStateService.ReturnToMainWindow();

                _logger.LogInformation("返回主界面操作完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行返回主界面操作时发生异常");
            }
        }
        #endregion

        #region 资源清理
        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            try
            {
                _logger.LogInformation("开始清理RetrieveWindowViewModel资源");

                // 标记为已释放
                _isDisposed = true;

                // 停止所有定时器
                StopNoFaceDetectionTimer();
                StopSubmissionTimeoutTimer();

                // 停止人脸识别
                StopFaceRecognition();

                // 取消摄像头事件订阅
                SafeUnsubscribeCameraEvents();

                // 释放取消令牌源
                try
                {
                    _faceDetectionCts?.Cancel();
                    _faceDetectionCts?.Dispose();
                    _faceDetectionCts = null;
                }
                catch (Exception ctsEx)
                {
                    _logger.LogWarning(ctsEx, "释放人脸检测取消令牌源时发生警告");
                }

                // 释放信号量
                try
                {
                    _faceDetectionLock?.Dispose();
                }
                catch (Exception lockEx)
                {
                    _logger.LogWarning(lockEx, "释放人脸检测信号量时发生警告");
                }

                // 占位图已移除

                _logger.LogInformation("RetrieveWindowViewModel资源清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理RetrieveWindowViewModel资源时发生异常");
            }
        }

        /// <summary>
        /// 实现IDisposable接口
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                CleanupResources();
                GC.SuppressFinalize(this);
            }
        }
        #endregion
    }
}
