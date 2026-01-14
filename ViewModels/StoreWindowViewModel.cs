using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FaceLocker.Models;
using FaceLocker.Services;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views.NumPad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using ReactiveUI;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ursa.Controls;
using static FaceLocker.Services.BaiduFaceSDKInterop;
using FaceLocker.Services.FaceRecognitions;


namespace FaceLocker.ViewModels
{
    /// <summary>
    /// 存放物品界面视图模型
    /// </summary>
    public class StoreWindowViewModel : ViewModelBase, IDisposable
    {
        #region 私有字段
        private readonly ILogger<StoreWindowViewModel> _logger;
        private readonly NumPadDialogViewModel _numPadDialogViewModel;
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

        private const int NO_FACE_TIMEOUT_SECONDS = 15;
        private const int RETURN_COUNTDOWN_SECONDS = 10;
        private const int SUBMISSION_TIMEOUT_SECONDS = 180;
        private const int LOCKER_CLOSE_DELAY_SECONDS = 1;

        private DispatcherTimer? _submissionTimeoutTimer;
        private DateTime _windowOpenedTime;
        private bool _isSubmissionTimeoutActive = false;
        private int _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS;

        // 人脸识别相关字段
        private bool _isRecognitionRunning = false;    //是否在人脸识别中
        private bool _isRecognitionCompleted = false;  //是否完成人脸识别
        private DateTime _lastFaceDetectionTime = DateTime.Now;
        private const int FACE_DETECTION_INTERVAL_MS = 100; // 优化：100ms间隔，每秒10次检测，减少CPU负载，配合卡尔曼预测保持流畅
        private byte[]? _recognizedCapturedFaceImage = null;
        private bool _hasDetectedFaceInCycle = false; //是否检测到人脸
        private bool _isCameraReady = false;
        private bool _isEventSubscribed = false;

        private readonly SemaphoreSlim _faceDetectionLock = new(1, 1);
        private WrapperFaceBox[] _currentFaces = [];
        private readonly object _faceLock = new();
        private readonly FaceTracker _faceTracker = new(); // 卡尔曼滤波人脸追踪器

        // 1×1 透明占位图
        private readonly WriteableBitmap _emptyFrame = new(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        // 人脸检测预分配缓冲区 - 避免每帧分配内存
        private byte[]? _faceDetectionBuffer;
        private int _faceDetectionBufferSize = 0;

        // 遮罩相关字段
        private DispatcherTimer? _maskTimer;
        private bool _showLockerMask = false;
        private string _maskLockerName = string.Empty;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化存放物品界面视图模型
        /// </summary>
        public StoreWindowViewModel(
            ILogger<StoreWindowViewModel> logger,
            NumPadDialogViewModel numPadDialogViewModel,
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
            _numPadDialogViewModel = numPadDialogViewModel;
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _lockerService = lockerService ?? throw new ArgumentNullException(nameof(lockerService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _appStateService = appStateService ?? throw new ArgumentNullException(nameof(appStateService));
            _lockControlService = lockControlService ?? throw new ArgumentNullException(nameof(lockControlService));
            _accessLogService = accessLogService ?? throw new ArgumentNullException(nameof(accessLogService));
            _baiduFaceService = baiduFaceService ?? throw new ArgumentNullException(nameof(baiduFaceService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            _logger.LogInformation("StoreWindowViewModel 开始初始化");

            // 记录摄像头服务状态
            _logger.LogInformation("摄像头服务类型: {CameraServiceType}, 可用状态: {IsCameraAvailable}",
                _cameraService.GetType().Name, _cameraService.IsCameraAvailable);

            // 记录百度人脸服务状态
            _logger.LogInformation("百度人脸服务初始化状态: {IsInitialized}", _baiduFaceService.IsInitialized);

            // 初始化属性
            InitializeProperties();

            // 初始化命令
            InitializeCommands();

            // 初始化CameraImage为占位图，避免null异常
            _cameraVideoElement = _emptyFrame;

            _logger.LogInformation("StoreWindowViewModel 初始化完成");
        }
        #endregion

        #region 界面属性
        /// <summary>
        /// 当前用户
        /// </summary>
        private User? _user;
        public User User
        {
            get => _user;
            set => this.RaiseAndSetIfChanged(ref _user, value);
        }

        /// <summary>
        /// 当前用户分配的柜格
        /// </summary>
        private UserLocker _userLocker;
        public UserLocker UserLocker
        {
            get => _userLocker;
            set => this.RaiseAndSetIfChanged(ref _userLocker, value);
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

        private string? _phoneNumber;
        public string? PhoneNumber
        {
            get => _phoneNumber;
            set
            {
                // 先设置属性
                this.RaiseAndSetIfChanged(ref _phoneNumber, value);

                if (!string.IsNullOrEmpty(value) && IsValidPhoneNumber(value))
                {
                    IsSubmitEnabled = true;
                }
                else
                {
                    SystemPrompt = "请正确填写手机号码";
                    IsSubmitEnabled = false;
                }
            }
        }

        private string _systemPrompt = "请正对摄像头";
        public string SystemPrompt
        {
            get => _systemPrompt;
            set => this.RaiseAndSetIfChanged(ref _systemPrompt, value);
        }

        private string _countdownText = "倒计时 10 秒后返回主界面";
        public string CountdownText
        {
            get => _countdownText;
            set => this.RaiseAndSetIfChanged(ref _countdownText, value);
        }

        private string? _businessInfo1;
        public string? BusinessInfo1
        {
            get => _businessInfo1;
            set => this.RaiseAndSetIfChanged(ref _businessInfo1, value);
        }

        #region 提交按钮可用性控制
        private bool _isSubmitEnabled = false;
        public bool IsSubmitEnabled
        {
            get => _isSubmitEnabled;
            set => this.RaiseAndSetIfChanged(ref _isSubmitEnabled, value);
        }
        #endregion

        #region 按钮状态控制
        private bool _isOpenLockerButtonEnabled;
        public bool IsOpenLockerButtonEnabled
        {
            get => _isOpenLockerButtonEnabled;
            set => this.RaiseAndSetIfChanged(ref _isOpenLockerButtonEnabled, value);
        }

        private bool _isSubmitButtonVisible = true;
        public bool IsSubmitButtonVisible
        {
            get => _isSubmitButtonVisible;
            set => this.RaiseAndSetIfChanged(ref _isSubmitButtonVisible, value);
        }

        private bool _isOpenLockerButtonVisible = false;
        public bool IsOpenLockerButtonVisible
        {
            get => _isOpenLockerButtonVisible;
            set => this.RaiseAndSetIfChanged(ref _isOpenLockerButtonVisible, value);
        }
        #endregion

        private WriteableBitmap? _cameraVideoElement;
        public WriteableBitmap? CameraVideoElement
        {
            get => _cameraVideoElement;
            private set
            {
                if (_cameraVideoElement == value) return;

                var old = _cameraVideoElement;
                _cameraVideoElement = value ?? _emptyFrame;
                this.RaisePropertyChanged();

                // 延迟释放旧帧
                if (old != null && old != _emptyFrame)
                {
                    _ = SafeDisposeWriteableBitmapAsync(old);
                }
            }
        }

        private bool _isFaceDetectionTimeout;
        public bool IsFaceDetectionTimeout
        {
            get => _isFaceDetectionTimeout;
            set => this.RaiseAndSetIfChanged(ref _isFaceDetectionTimeout, value);
        }

        private int _faceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
        public int FaceDetectionRemainingSeconds
        {
            get => _faceDetectionRemainingSeconds;
            set => this.RaiseAndSetIfChanged(ref _faceDetectionRemainingSeconds, value);
        }

        private bool _showCameraNotConnected;
        public bool ShowCameraNotConnected
        {
            get => _showCameraNotConnected;
            set => this.RaiseAndSetIfChanged(ref _showCameraNotConnected, value);
        }
        
        /// <summary>
        /// 是否使用原生视频模式（零拷贝渲染）
        /// 当使用 NativeVideoCameraService 时为 true
        /// </summary>
        private bool _useNativeVideoMode = false;
        public bool UseNativeVideoMode
        {
            get => _useNativeVideoMode;
            set => this.RaiseAndSetIfChanged(ref _useNativeVideoMode, value);
        }

        #region 人脸框覆盖层属性（用于原生视频模式）
        
        /// <summary>
        /// 人脸框列表（用于覆盖层绘制）
        /// </summary>
        private System.Collections.Generic.List<FaceLocker.Views.Controls.FaceBoxInfo>? _faceBoxes;
        public System.Collections.Generic.List<FaceLocker.Views.Controls.FaceBoxInfo>? FaceBoxes
        {
            get => _faceBoxes;
            set => this.RaiseAndSetIfChanged(ref _faceBoxes, value);
        }

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

        #endregion

        private bool _showCountdown;
        public bool ShowCountdown
        {
            get => _showCountdown;
            set => this.RaiseAndSetIfChanged(ref _showCountdown, value);
        }

        private bool _showLockerInfo;
        public bool ShowLockerInfo
        {
            get => _showLockerInfo;
            set => this.RaiseAndSetIfChanged(ref _showLockerInfo, value);
        }

        private bool _showFaceRecognitionPrompt = true;
        public bool ShowFaceRecognitionPrompt
        {
            get => _showFaceRecognitionPrompt;
            set => this.RaiseAndSetIfChanged(ref _showFaceRecognitionPrompt, value);
        }

        /// <summary>
        /// 是否正在提交处理中
        /// </summary>
        private bool _isProcessingSubmit = false;
        public bool IsProcessingSubmit
        {
            get => _isProcessingSubmit;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isProcessingSubmit, value);
            }
        }
        #region 遮罩相关属性
        /// <summary>
        /// 是否显示柜子名称遮罩
        /// </summary>
        public bool ShowLockerMask
        {
            get => _showLockerMask;
            set
            {
                _logger.LogInformation($"设置ShowLockerMask: {value}");
                this.RaiseAndSetIfChanged(ref _showLockerMask, value);
            }
        }

        /// <summary>
        /// 遮罩中显示的柜子名称
        /// </summary>
        public string MaskLockerName
        {
            get => _maskLockerName;
            set
            {
                _logger.LogInformation($"设置MaskLockerName: {value}");
                this.RaiseAndSetIfChanged(ref _maskLockerName, value);
            }
        }
        #endregion
        #endregion

        #region 命令
        /// <summary>
        /// 提交按钮命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> SubmitCommand { get; private set; } = null!;
        /// <summary>
        /// 返回主界面命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ReturnToMainCommand { get; private set; } = null!;
        /// <summary>
        /// 重拍命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ReTakeCommand { get; private set; } = null!;
        /// <summary>
        /// 开柜命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> OpenLockerCommand { get; private set; } = null!;
        #endregion

        #region 初始化属性
        private void InitializeProperties()
        {
            try
            {
                _logger.LogDebug("初始化界面属性");

                // 重置所有状态
                PhoneNumber = null;
                SystemPrompt = "请正对摄像头";
                CountdownText = "倒计时 10 秒后返回主界面";
                BusinessInfo1 = null;

                FaceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                IsFaceDetectionTimeout = false;
                ShowCountdown = false;
                ShowLockerInfo = false;
                ShowFaceRecognitionPrompt = true;
                IsSubmitEnabled = false;
                ShowCameraNotConnected = true; // 初始显示摄像头连接中
                _hasDetectedFaceInCycle = false; // 重置人脸检测状态

                // 遮罩状态初始化
                ShowLockerMask = false;
                MaskLockerName = string.Empty;

                // 按钮状态初始化
                IsSubmitButtonVisible = true;
                IsOpenLockerButtonVisible = false;
                IsOpenLockerButtonEnabled = false;

                // 重置3分钟倒计时状态
                _isSubmissionTimeoutActive = false;
                _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS;

                _logger.LogDebug("界面属性初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化属性时发生异常");
            }
        }
        #endregion

        #region 初始化命令
        private void InitializeCommands()
        {
            try
            {
                _logger.LogDebug("初始化命令");

                // 提交命令 - 手机号验证通过或人脸识别成功
                var canSubmit = this.WhenAnyValue(x => x.IsSubmitEnabled);

                SubmitCommand = ReactiveCommand.CreateFromTask(ExecuteSubmitAsync, canSubmit);

                // 返回主界面命令
                ReturnToMainCommand = ReactiveCommand.CreateFromTask(ExecuteReturnToMainAsync);

                // 重拍命令
                ReTakeCommand = ReactiveCommand.CreateFromTask(ExecuteReTakeAsync);

                // 打开柜门命令 - 根据按钮可用性控制
                var canOpenLocker = this.WhenAnyValue(x => x.IsOpenLockerButtonEnabled);

                OpenLockerCommand = ReactiveCommand.CreateFromTask(ExecuteOpenLockerAsync, canOpenLocker);

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
                _logger.LogInformation("开始初始化存放物品窗口");

                // 重置所有状态
                InitializeProperties();

                // 记录窗口打开时间
                _windowOpenedTime = DateTime.Now;

                SystemPrompt = "请填写手机号码";

                // 启动摄像头显示
                await StartCameraDisplay();

                // 启动人脸识别
                await StartFaceRecognitionAsync();

                // 启动人脸检测超时定时器（在摄像头就绪后启动）
                if (_isCameraReady)
                {
                    StartNoFaceTimer();
                }

                // 启动3分钟提交超时定时器
                StartSubmissionTimeoutTimer();

                _logger.LogInformation("存放物品窗口初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化存放物品窗口时发生异常");
                SystemPrompt = "初始化失败，请检查设备连接";
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
                    
                    // 设置窗口
                    // 原生视频模式 + GStreamer cairooverlay 绘制人脸框（推荐）
                    // 人脸框直接在 GStreamer 管道中绘制，无需透明窗口
                    bool windowSet = await nativeService.SetWindowAsync(x11WindowId);
                    if (windowSet)
                    {
                        UseNativeVideoMode = true;
                        _logger.LogInformation("原生视频模式已启用（注意：人脸框将不显示）");
                        
                        // 如果摄像头已启动但未播放，启动播放
                        if (_isCameraReady)
                        {
                            await nativeService.StartPlaybackAsync();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("设置原生视频窗口失败，将使用软件渲染模式");
                        UseNativeVideoMode = false;
                    }
                }
                else
                {
                    _logger.LogDebug("摄像头服务不支持原生视频模式，使用软件渲染");
                    UseNativeVideoMode = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置原生视频窗口时发生异常");
                UseNativeVideoMode = false;
            }
        }
        #endregion

        #region 设置用户头像
        private async Task SetUserAvatarAsync(byte[] faceImage)
        {
            _logger.LogDebug("开始设置用户头像");
            if (faceImage == null)
            {
                _logger.LogDebug("用户头像字节数组为null，跳过设置");
                UserAvatar = null;
                return;
            }

            try
            {
                _logger.LogDebug("将字节数组转换为Bitmap");
                using (var stream = new MemoryStream(faceImage))
                {
                    var bitmap = new Bitmap(stream);
                    _logger.LogDebug("用户头像转换成功");
                    UserAvatar = bitmap;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置用户头像时发生异常");
                UserAvatar = null;
            }
        }
        #endregion

        #region 启动摄像头显示
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

                // 先停止可能正在运行的摄像头
                await _cameraService.StopCameraAsync();

                // 启动摄像头
                var startResult = await _cameraService.StartCameraAsync();
                _logger.LogInformation("摄像头启动结果: {StartResult}", startResult);

                if (!startResult)
                {
                    _logger.LogWarning("启动摄像头失败");
                    ShowCameraNotConnected = true;
                    SystemPrompt = "启动摄像头失败，请检查权限和设备连接";
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
                SystemPrompt = "摄像头初始化异常，请检查设备";
                SafeUnsubscribeCameraEvents(); // 确保事件被取消订阅
            }
        }
        #endregion

        #region 启动人脸识别
        /// <summary>
        /// 启动人脸识别
        /// </summary>
        private async Task StartFaceRecognitionAsync()
        {
            try
            {
                _logger.LogInformation("开始启动人脸识别");

                // 检查百度人脸服务是否初始化
                if (!_baiduFaceService.IsInitialized)
                {
                    _logger.LogWarning("百度人脸服务未初始化，无法开始识别");
                    SystemPrompt = "人脸识别服务未就绪";
                    return;
                }

                // 检查摄像头是否就绪
                if (!_isCameraReady)
                {
                    _logger.LogWarning("摄像头未就绪，无法开始识别");
                    SystemPrompt = "摄像头未连接，请检查设备";
                    return;
                }

                // 设置初始提示
                SystemPrompt = "请填写手机号码";

                // 重置人脸检测状态
                _hasDetectedFaceInCycle = false;
                _lastFaceDetectionTime = DateTime.Now;

                _logger.LogInformation("人脸识别已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动人脸识别时发生异常");
                SystemPrompt = "人脸识别启动失败";
                _isRecognitionRunning = false;
            }
        }
        #endregion

        #region 摄像头帧捕获事件处理
        // 性能监测变量
        private int _frameCount = 0;
        private DateTime _lastFpsTime = DateTime.Now;
        private long _totalFrameProcessTimeMs = 0;
        
        /// <summary>
        /// 摄像头帧捕获事件处理 - 优化版本（参考原版本）
        /// 分离显示逻辑和人脸检测逻辑，确保视频流畅
        /// </summary>
        private void OnCameraFrameCaptured(object? sender, WriteableBitmap frame)
        {
            if (frame == null || _isDisposed) return;
            
            var frameStartTime = DateTime.Now;

            try
            {
                // 使用卡尔曼滤波预测的人脸位置（每帧更新，平滑跟踪）
                WrapperFaceBox[] facesToDraw = _faceTracker.GetPredictedFaces();

                // 获取帧尺寸
                int frameWidth = frame.PixelSize.Width;
                int frameHeight = frame.PixelSize.Height;

                // 绘制预测的人脸框（平滑跟踪效果）
                // 暂时禁用直接在帧上绘制，使用 FaceBoxOverlay 控件代替
                // if (facesToDraw != null && facesToDraw.Length > 0 && _isCameraReady)
                // {
                //     try
                //     {
                //         FaceBoxRenderer.RenderFaceBoxes(frame, facesToDraw, frameWidth, frameHeight);
                //     }
                //     catch
                //     {
                //         // 忽略绘制异常
                //     }
                // 已切换到 Native GStreamer+cairooverlay 绘制人脸框；此处不再保留空的禁用分支

                // 更新摄像头图像
                CameraVideoElement = frame;
                
                // 性能统计
                _frameCount++;
                var frameProcessTime = (DateTime.Now - frameStartTime).TotalMilliseconds;
                _totalFrameProcessTimeMs += (long)frameProcessTime;
                
                var elapsed = (DateTime.Now - _lastFpsTime).TotalSeconds;
                if (elapsed >= 5.0) // 每5秒输出一次性能统计
                {
                    var currentFps = _frameCount / elapsed;
                    var avgFrameTime = _totalFrameProcessTimeMs / (double)_frameCount;
                    var msg = $"[性能统计] 显示FPS: {currentFps:F1}, 平均帧处理: {avgFrameTime:F2}ms, 总帧数: {_frameCount}";
                    _logger.LogInformation(msg);
                    _frameCount = 0;
                    _totalFrameProcessTimeMs = 0;
                    _lastFpsTime = DateTime.Now;
                }

                // 如果是第一次收到帧，更新连接状态
                if (ShowCameraNotConnected)
                {
                    ShowCameraNotConnected = false;
                    _logger.LogInformation("摄像头连接成功");
                }

                // 异步进行人脸检测（不阻塞显示）
                ScheduleFaceDetectionAsync(frame, frameWidth, frameHeight);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理摄像头帧时发生异常");
            }
        }

        /// <summary>
        /// 异步调度人脸检测（不阻塞主线程）
        /// 关键优化：使用预分配缓冲区，快速复制帧数据，立即释放帧锁，避免阻塞UI渲染
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

            // 存物窗口：持续检测人脸，不受识别完成状态限制

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

            // 在后台线程进行人脸检测（使用复制的数据，不锁定原帧）
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

        /// <summary>
        /// 从字节数组处理人脸检测（避免锁定UI帧）
        /// </summary>
        private async Task ProcessFaceDetectionFromBytesAsync(byte[] frameData, int width, int height, int rowBytes)
        {
            if (_isDisposed || !_isCameraReady) return;

            Mat? mat = null;
            try
            {
                // 从字节数组创建Mat（BGRA格式）
                mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, frameData);
                if (mat == null || mat.Empty())
                {
                    return;
                }

                // 转换为BGR格式（百度SDK需要）
                using var bgrMat = new Mat();
                Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);

                // 进行人脸检测
                var detectionResult = await DetectFacesAsync(bgrMat);

                // 处理检测结果
                ProcessFaceDetectionResult(detectionResult, bgrMat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台人脸检测处理时发生异常");
            }
            finally
            {
                mat?.Dispose();
            }
        }

        /// <summary>
        /// 处理人脸检测结果 - 存物窗口只做检测和保存人脸，不做识别
        /// </summary>
        private void ProcessFaceDetectionResult(FaceDetectionResult detectionResult, Mat mat)
        {
            // 更新卡尔曼滤波追踪器（在锁外执行，因为追踪器是线程安全的）
            _faceTracker.Update(detectionResult.FaceBoxes);

            lock (_faceLock)
            {
                if (detectionResult.Success && detectionResult.FaceCount > 0)
                {
                    _currentFaces = detectionResult.FaceBoxes;
                    _hasDetectedFaceInCycle = true;

                    // 更新人脸框覆盖层数据（用于原生视频模式）
                    UpdateFaceBoxOverlay(_currentFaces);

                    // 在UI线程更新状态
                    Dispatcher.UIThread.Post(() =>
                    {
                        StopNoFaceTimer();
                        IsFaceDetectionTimeout = false;
                        ShowCountdown = false;
                        FaceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                    });

                    // 取出最高置信度的人脸框
                    var highConfidenceFaces = _currentFaces.OrderByDescending(face => face.score).First();

                    // 置信度阈值：0.8 表示 80% 置信度
                    if (highConfidenceFaces.score < 0.8f)
                    {
                        _hasDetectedFaceInCycle = false;
                        return;
                    }

                    // 存物窗口：只保存人脸图像，不做人脸识别
                    // 保存人脸图像用于后续提交
                    if (!IsProcessingSubmit && _recognizedCapturedFaceImage == null)
                    {
                        try
                        {
                            var clonedMat = mat.Clone();
                            _recognizedCapturedFaceImage = clonedMat.ToBytes(".jpg");
                            clonedMat.Dispose();
                            
                            // 更新用户头像显示
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    using var ms = new System.IO.MemoryStream(_recognizedCapturedFaceImage);
                                    UserAvatar = new Avalonia.Media.Imaging.Bitmap(ms);
                                }
                                catch { }
                            });
                            
                            _logger.LogInformation("已保存人脸图像");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "保存人脸图像失败");
                        }
                    }
                }
                else
                {
                    _currentFaces = [];
                    if (_hasDetectedFaceInCycle)
                    {
                        _hasDetectedFaceInCycle = false;
                    }
                    // 清空人脸框覆盖层
                    UpdateFaceBoxOverlay(null);
                }
            }
        }

        /// <summary>
        /// 更新人脸框覆盖层数据（用于原生视频模式）
        /// </summary>
        private void UpdateFaceBoxOverlay(WrapperFaceBox[]? faces)
        {
            // 更新 GStreamer cairooverlay（原生视频模式）
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

            // 同时更新 Avalonia 覆盖层（软件渲染模式备用）
            Dispatcher.UIThread.Post(() =>
            {
                if (faces == null || faces.Length == 0)
                {
                    FaceBoxes = null;
                    return;
                }

                var boxList = new System.Collections.Generic.List<FaceLocker.Views.Controls.FaceBoxInfo>();
                foreach (var face in faces)
                {
                    boxList.Add(FaceLocker.Views.Controls.FaceBoxInfo.FromWrapperFaceBox(
                        face.center_x, face.center_y, face.width, face.height, face.score));
                }
                FaceBoxes = boxList;
            });
        }

        #endregion

        #region 异步进行人脸检测
        /// <summary>
        /// 异步进行人脸检测
        /// </summary>
        private async Task<FaceDetectionResult> DetectFacesAsync(Mat mat)
        {
            if (!await _faceDetectionLock.WaitAsync(100))
            {
                _logger.LogDebug("人脸检测锁被占用，跳过本次检测");
                return new FaceDetectionResult { Success = false };
            }

            try
            {
                var detectionResult = await _baiduFaceService.DetectFacesOnlyAsync(mat);
                return detectionResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "人脸检测时发生异常");
                return new FaceDetectionResult { Success = false };
            }
            finally
            {
                _faceDetectionLock.Release();
            }
        }
        #endregion

        #region 处理人脸识别
        /// <summary>
        /// 处理人脸识别 - 按照需求完善逻辑
        /// </summary>
        private async Task ProcessFaceRecognitionAsync(Mat mat, WrapperFaceBox[] detectedFaces)
        {
            try
            {
                if (detectedFaces == null || detectedFaces.Length == 0)
                {
                    _logger.LogWarning("未检测到人脸，跳过识别");
                    return;
                }

                _logger.LogInformation("开始人脸识别：_isRecognitionRunning：{_isRecognitionRunning}", _isRecognitionRunning);

                // 设置识别运行状态
                _isRecognitionRunning = true;
                SystemPrompt = "人脸识别中...";

                // 提取用于显示的图像（保持高宽比）
                var faceBytesForDisplay = BaiduFaceService.MatToByteArray(mat);

                if (faceBytesForDisplay == null)
                {
                    _logger.LogWarning("无法提取人脸图像用于识别");
                    return;
                }

                // 保存人脸用于后续注册
                _recognizedCapturedFaceImage = faceBytesForDisplay;

                // 设置用户头像
                if (_recognizedCapturedFaceImage != null)
                {
                    //识别了一张人脸后，就不在更新头像
                    await SetUserAvatarAsync(_recognizedCapturedFaceImage);
                }

                // 调用百度人脸识别服务（1:N识别，不指定角色，使用用户角色Id"4"）
                var recognitionResult = await _baiduFaceService.RecognizeFaceWithRoleAsync(mat, 4);
                #region 匹配到注册用户
                if (recognitionResult.Success && !string.IsNullOrEmpty(recognitionResult.JsonResult))
                {
                    var result = JsonResultParser.ParseFaceRecognitionResponse(recognitionResult.JsonResult);
                    if (result != null && result.UserList != null && result.UserList.Any())
                    {

                        var bestUser = result.UserList.OrderByDescending(u => u.Score).FirstOrDefault();
                        if (bestUser != null && bestUser.Score >= 80.0f) // 阈值80分
                        {
                            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == bestUser.UserId);
                            if (user != null)
                            {
                                _user = user;

                                _isRecognitionRunning = false;
                                _isRecognitionCompleted = true;

                                if (!string.IsNullOrEmpty(user.PhoneNumber))
                                {
                                    _logger.LogInformation("人脸匹配到用户，用户ID: {UserId}, 手机号: {PhoneNumber}，自动执行分配柜子", user.Id, user.PhoneNumber);
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        PhoneNumber = user.PhoneNumber;
                                        SystemPrompt = "人脸识别成功，正在分配柜子...";
                                    });

                                    // 自动执行分配柜子操作
                                    await ExecuteSubmitAsync();
                                }
                                else
                                {
                                    _logger.LogWarning("人脸匹配到用户,用户ID: {UserId}, 未填写手机号，自动执行分配柜子", user.Id);

                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        SystemPrompt = "人脸识别成功，正在分配柜子...";
                                    });

                                    // 自动执行分配柜子操作
                                    await ExecuteSubmitAsync();
                                }

                                return;
                            }
                        }
                    }
                }
                #endregion

                _logger.LogWarning("未匹配到用户,注册新用户，自动执行分配柜子");

                // 创建新用户后自动执行分配柜子
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemPrompt = "正在创建用户并分配柜子...";
                    ShowFaceRecognitionPrompt = false;
                });

                // 自动执行分配柜子操作
                await ExecuteSubmitAsync();

                _isRecognitionRunning = false;
                _isRecognitionCompleted = true;

                // 保留人脸框，不清空，让用户看到检测结果
                // lock (_faceLock)
                // {
                //     _currentFaces = [];
                // }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "人脸识别处理时发生异常");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemPrompt = "识别过程中发生错误，请重试";
                    ShowFaceRecognitionPrompt = false;
                });
                _isRecognitionRunning = false;
                _isRecognitionCompleted = false;
            }
        }
        #endregion

        #region 安全释放WriteableBitmap
        /// <summary>
        /// 安全释放WriteableBitmap
        /// </summary>
        private async Task SafeDisposeWriteableBitmapAsync(WriteableBitmap bitmap)
        {
            try
            {
                await Task.Delay(300); // 增加延迟时间

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (bitmap != null && bitmap != _emptyFrame)
                        {
                            bitmap.Dispose();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogDebug("WriteableBitmap已被释放，跳过重复释放");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "释放WriteableBitmap资源时发生警告");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "安排安全释放WriteableBitmap时发生异常");
            }
        }
        #endregion

        #region 15秒人脸检测超时定时器
        /// <summary>
        /// 启动人脸检测超时定时器
        /// </summary>
        private void StartNoFaceTimer()
        {
            try
            {
                // 停止现有定时器
                StopNoFaceTimer();

                // 创建新定时器
                _noFaceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _noFaceTimer.Tick += OnNoFaceTimerTick;

                // 重置状态
                FaceDetectionRemainingSeconds = NO_FACE_TIMEOUT_SECONDS;
                IsFaceDetectionTimeout = false;
                ShowCountdown = false;
                _hasDetectedFaceInCycle = false;

                // 启动定时器
                _noFaceTimer.Start();

                _logger.LogDebug("人脸检测超时定时器已启动，超时时间: {Seconds}秒", NO_FACE_TIMEOUT_SECONDS);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动人脸检测超时定时器时发生异常");
            }
        }

        /// <summary>
        /// 停止人脸检测超时定时器
        /// </summary>
        private void StopNoFaceTimer()
        {
            try
            {
                if (_noFaceTimer != null)
                {
                    if (_noFaceTimer.IsEnabled)
                    {
                        _noFaceTimer.Stop();
                    }
                    _noFaceTimer.Tick -= OnNoFaceTimerTick;
                    _noFaceTimer = null;
                    _logger.LogDebug("人脸检测超时定时器已停止");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止人脸检测超时定时器时发生异常");
            }
        }
        #endregion

        #region 人脸检测超时处理
        /// <summary>
        /// 人脸检测超时定时器事件
        /// </summary>
        private void OnNoFaceTimerTick(object? sender, EventArgs e)
        {
            try
            {
                // 如果已经检测到人脸，停止定时器
                if (_hasDetectedFaceInCycle)
                {
                    _logger.LogDebug("已检测到人脸，停止超时定时器");
                    StopNoFaceTimer();
                    return;
                }

                FaceDetectionRemainingSeconds--;
                _logger.LogDebug("人脸检测剩余时间: {Seconds}秒", FaceDetectionRemainingSeconds);

                if (FaceDetectionRemainingSeconds <= 0)
                {
                    // 人脸检测超时（15秒未检测到人脸）
                    HandleFaceDetectionTimeout();
                }
                else if (FaceDetectionRemainingSeconds <= 3)
                {
                    // 最后3秒开始显示倒计时
                    ShowCountdown = true;
                    CountdownText = $"倒计时 {FaceDetectionRemainingSeconds} 秒后返回主界面";
                    SystemPrompt = "人脸检测超时\n即将返回主界面";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理人脸检测超时定时器事件时发生异常");
            }
        }

        /// <summary>
        /// 处理人脸检测超时
        /// </summary>
        private async void HandleFaceDetectionTimeout()
        {
            try
            {
                _logger.LogInformation("人脸检测超时（15秒未检测到人脸）");

                // 停止人脸检测超时定时器
                StopNoFaceTimer();

                // 更新界面状态
                IsFaceDetectionTimeout = true;
                SystemPrompt = "人脸检测超时，请重新尝试";

                // 开始返回主界面倒计时（10秒）
                StartReturnCountdown(10);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理人脸检测超时时发生异常");
                await ExecuteReturnToMainAsync();
            }
        }
        #endregion

        #region 启动3分钟提交超时定时器
        /// <summary>
        /// 启动3分钟提交超时定时器
        /// </summary>
        private void StartSubmissionTimeoutTimer()
        {
            try
            {
                _logger.LogDebug("启动3分钟提交超时定时器");

                // 停止现有定时器
                StopSubmissionTimeoutTimer();

                // 创建新定时器
                _submissionTimeoutTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _submissionTimeoutTimer.Tick += OnSubmissionTimeoutTimerTick;

                // 重置状态
                _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS;
                _isSubmissionTimeoutActive = false;

                // 启动定时器
                _submissionTimeoutTimer.Start();

                _logger.LogInformation("3分钟提交超时定时器已启动，超时时间: {Seconds}秒", SUBMISSION_TIMEOUT_SECONDS);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动3分钟提交超时定时器时发生异常");
            }
        }
        #endregion

        #region 停止3分钟提交超时定时器
        /// <summary>
        /// 停止3分钟提交超时定时器
        /// </summary>
        private void StopSubmissionTimeoutTimer()
        {
            try
            {
                if (_submissionTimeoutTimer != null)
                {
                    if (_submissionTimeoutTimer.IsEnabled)
                    {
                        _submissionTimeoutTimer.Stop();
                        _logger.LogDebug("3分钟提交超时定时器已停止");
                    }
                    _submissionTimeoutTimer.Tick -= OnSubmissionTimeoutTimerTick;
                    _submissionTimeoutTimer = null;
                }
                _isSubmissionTimeoutActive = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止3分钟提交超时定时器时发生异常");
            }
        }
        #endregion

        #region 3分钟提交超时定时器事件
        /// <summary>
        /// 3分钟提交超时定时器事件
        /// </summary>
        private async void OnSubmissionTimeoutTimerTick(object? sender, EventArgs e)
        {
            try
            {
                // 计算已过去的时间
                var elapsedTime = DateTime.Now - _windowOpenedTime;
                _submissionRemainingSeconds = SUBMISSION_TIMEOUT_SECONDS - (int)elapsedTime.TotalSeconds;

                _logger.LogTrace("3分钟提交超时倒计时，剩余时间: {Seconds}秒", _submissionRemainingSeconds);

                if (_submissionRemainingSeconds <= 0 && !_isSubmissionTimeoutActive)
                {
                    // 3分钟时间到，立即返回主界面
                    _logger.LogInformation("3分钟总时间到，立即返回主界面");

                    // 设置超时激活状态
                    _isSubmissionTimeoutActive = true;

                    // 停止所有定时器
                    StopNoFaceTimer();
                    StopSubmissionTimeoutTimer();
                    StopReturnCountdownTimer();
                    StopMaskTimer();

                    // 停止人脸识别
                    _isRecognitionRunning = false;

                    // 显示提示
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SystemPrompt = "操作超时\n即将返回主界面";
                        ShowCountdown = true;
                        CountdownText = $"倒计时 3 秒后返回主界面";
                    });

                    // 启动3秒返回主界面倒计时
                    StartReturnCountdown(3);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理3分钟提交超时定时器事件时发生异常");
            }
        }
        #endregion

        #region 返回主界面倒计时管理
        /// <summary>
        /// 启动返回主界面倒计时
        /// </summary>
        private void StartReturnCountdown(int seconds)
        {
            try
            {
                // 停止现有定时器
                StopReturnCountdownTimer();

                // 创建新定时器
                _returnCountdownTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _returnCountdownTimer.Tick += OnReturnCountdownTimerTick;

                // 设置倒计时秒数
                int remainingSeconds = seconds;

                // 显示倒计时
                ShowCountdown = true;
                CountdownText = $"倒计时 {remainingSeconds} 秒后返回主界面";

                // 启动定时器
                _returnCountdownTimer.Start();

                _logger.LogDebug("返回主界面倒计时定时器已启动，倒计时: {Seconds}秒", remainingSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动返回倒计时定时器时发生异常");
            }
        }

        /// <summary>
        /// 停止返回主界面倒计时定时器
        /// </summary>
        private void StopReturnCountdownTimer()
        {
            try
            {
                if (_returnCountdownTimer != null)
                {
                    if (_returnCountdownTimer.IsEnabled)
                    {
                        _returnCountdownTimer.Stop();
                    }
                    _returnCountdownTimer.Tick -= OnReturnCountdownTimerTick;
                    _returnCountdownTimer = null;
                    _logger.LogDebug("返回主界面倒计时定时器已停止");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止返回倒计时定时器时发生异常");
            }
        }

        /// <summary>
        /// 返回主界面倒计时定时器事件
        /// </summary>
        private async void OnReturnCountdownTimerTick(object? sender, EventArgs e)
        {
            try
            {
                // 解析当前倒计时秒数
                var currentText = CountdownText;
                if (currentText.StartsWith("倒计时 "))
                {
                    var secondsStr = currentText.Replace("倒计时 ", "").Replace(" 秒后返回主界面", "");
                    if (int.TryParse(secondsStr, out int currentSeconds))
                    {
                        currentSeconds--;

                        if (currentSeconds <= 0)
                        {
                            // 倒计时结束，返回主界面
                            StopReturnCountdownTimer();
                            await ExecuteReturnToMainAsync();
                        }
                        else
                        {
                            // 更新倒计时文本
                            CountdownText = $"倒计时 {currentSeconds} 秒后返回主界面";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理返回倒计时定时器事件时发生异常");
                await ExecuteReturnToMainAsync();
            }
        }
        #endregion

        #region 遮罩相关方法
        /// <summary>
        /// 显示柜子名称弹窗
        /// </summary>
        private void ShowLockerNameMask(string lockerName)
        {
            try
            {
                _logger.LogInformation($"显示柜子名称弹窗: {lockerName}");

                // 确保在UI线程上设置属性
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MaskLockerName = lockerName;
                    ShowLockerMask = true;
                    _logger.LogInformation($"UI线程设置遮罩: ShowLockerMask={ShowLockerMask}, MaskLockerName={MaskLockerName}");
                });

                // 停止之前的定时器（如果有）
                StopMaskTimer();

                // 启动15秒自动关闭定时器
                _maskTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(15)
                };
                _maskTimer.Tick += OnMaskTimerTick;
                _maskTimer.Start();

                _logger.LogDebug($"柜子名称弹窗已显示，将在15秒后自动关闭");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "显示柜子名称弹窗时发生异常");
            }
        }

        /// <summary>
        /// 遮罩定时器事件
        /// </summary>
        private void OnMaskTimerTick(object? sender, EventArgs e)
        {
            try
            {
                _logger.LogDebug("弹窗显示15秒结束，自动关闭");
                HideLockerMask();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理遮罩定时器事件时发生异常");
            }
        }

        /// <summary>
        /// 隐藏遮罩
        /// </summary>
        private void HideLockerMask()
        {
            try
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowLockerMask = false;
                    _logger.LogInformation($"UI线程隐藏遮罩: ShowLockerMask={ShowLockerMask}");
                });
                StopMaskTimer();
                _logger.LogDebug("柜子名称弹窗已隐藏");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "隐藏柜子名称弹窗时发生异常");
            }
        }

        /// <summary>
        /// 停止遮罩定时器
        /// </summary>
        private void StopMaskTimer()
        {
            try
            {
                if (_maskTimer != null)
                {
                    if (_maskTimer.IsEnabled)
                    {
                        _maskTimer.Stop();
                    }
                    _maskTimer.Tick -= OnMaskTimerTick;
                    _maskTimer = null;
                    _logger.LogDebug("遮罩定时器已停止");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止遮罩定时器时发生异常");
            }
        }
        #endregion

        #region 分配柜子
        /// <summary>
        /// 分配柜子
        /// </summary>
        private async Task ExecuteSubmitAsync()
        {
            // 防止重复提交
            if (IsProcessingSubmit)
            {
                _logger.LogDebug("正在处理提交操作，跳过重复请求");
                return;
            }
            IsProcessingSubmit = true;
            try
            {
                _logger.LogInformation("开始执行提交操作");

                //必须填写手机号码
                if (string.IsNullOrEmpty(PhoneNumber))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SystemPrompt = "请填写手机号码";
                    });

                    _logger.LogWarning("用户未填写手机号码");

                    IsProcessingSubmit = false;
                    return;
                }

                // 验证手机号格式
                if (!IsValidPhoneNumber(PhoneNumber))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SystemPrompt = "手机号码格式不正确，请重新输入";
                    });

                    _logger.LogWarning("用户输入了无效的手机号码: {PhoneNumber}", PhoneNumber);

                    _isRecognitionCompleted = false;
                    _isRecognitionRunning = false;
                    ShowFaceRecognitionPrompt = true;
                    IsSubmitEnabled = false;
                    IsSubmitButtonVisible = true;
                    IsOpenLockerButtonVisible = false;
                    IsOpenLockerButtonEnabled = false;
                    IsProcessingSubmit = false;
                    return;
                }

                // 只停止人脸检测超时定时器，不停止3分钟总定时器
                StopNoFaceTimer();
                StopReturnCountdownTimer();

                // 显示处理中提示
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemPrompt = "正在处理您的请求...";
                });

                #region 处理用户信息（在后台线程执行）
                await Task.Run(async () =>
                {
                    try
                    {
                        if (_user != null)
                        {
                            if (_recognizedCapturedFaceImage != null)
                            {
                                _user.Avatar = Convert.ToBase64String(_recognizedCapturedFaceImage);
                            }
                            if (!string.IsNullOrEmpty(PhoneNumber))
                            {
                                _user.PhoneNumber = PhoneNumber;
                            }
                            _user.UpdatedAt = DateTime.Now;
                            await _userService.UpdateUserAsync(_user);

                            _logger.LogInformation($"匹配到用户，只更新用户头像");
                        }
                        else
                        {
                            long maxUserId = await _userService.GetMaxUserIdAsync();
                            var user = new User
                            {
                                UserNumber = $"{maxUserId + 1}",
                                Name = "拍照用户",
                                IdNumber = "",
                                PhoneNumber = string.IsNullOrEmpty(PhoneNumber) ? "" : PhoneNumber,
                                Password = "",
                                RoleId = 4,
                                AssignedLockers = [],
                                Avatar = _recognizedCapturedFaceImage != null ? Convert.ToBase64String(_recognizedCapturedFaceImage) : "",
                                FaceFeatureData = null,
                                FaceConfidence = 0.0f,
                                FaceFeatureVersion = 1,
                                LastFaceUpdate = DateTime.Now,
                                IsActive = true,
                                CreatedAt = DateTime.Now
                            };

                            var resultUser = await _userService.AddFaceImageUserAsync(user);
                            if (resultUser)
                            {
                                _logger.LogInformation($"未匹配到用户，新建用户: {user.UserNumber}");
                                _user = user;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理用户信息时发生异常");
                        throw;
                    }
                });
                #endregion

                #region 异步生成人脸特征数据（在后台线程执行）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_user == null)
                        {
                            _logger.LogWarning("用户对象为空，跳过人脸特征数据生成");
                            return;
                        }

                        await _baiduFaceService.GenerateFaceFeatureFromAvatarAsync(_user);
                        _logger.LogDebug("人脸特征数据生成完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "生成人脸特征数据时发生异常");
                    }
                });
                #endregion

                #region 分配柜格（在后台线程执行）
                Locker? assignedLocker = null;

                try
                {
                    if (_user == null)
                    {
                        _logger.LogError("用户对象为空，无法分配柜格");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            SystemPrompt = "用户信息错误，请重新尝试";
                        });
                        return;
                    }

                    if (_userLocker == null)
                    {
                        var assignUserLockerList = await _lockerService.GetUserLockersAsync(_user.Id);
                        #region 检测是否存在已分配未存入的柜子
                        var assignUnusedLocker = assignUserLockerList.FirstOrDefault(o => o.StorageStatus == StorageStatus.Unused && o.ExpiresAt > DateTime.Now);
                        if (assignUnusedLocker != null)
                        {
                            _userLocker = assignUnusedLocker;

                            var assignlocker = await _lockerService.GetLockerAsync(assignUnusedLocker.LockerId);
                            if (assignlocker != null)
                            {
                                _isRecognitionCompleted = true;
                                _logger.LogInformation($"检测到已分配未存入的柜子: {assignlocker.LockerName}");
                                assignedLocker = assignlocker;
                            }
                        }
                        #endregion

                        #region 检测已分配的还在使用的柜子
                        if (assignedLocker == null)
                        {
                            var assignStoredLocker = assignUserLockerList.FirstOrDefault(o => o.StorageStatus == StorageStatus.Stored && o.ExpiresAt > DateTime.Now);
                            if (assignStoredLocker != null)
                            {
                                var assignlocker = await _lockerService.GetLockerAsync(assignStoredLocker.LockerId);
                                if (assignlocker != null)
                                {
                                    _logger.LogInformation($"检测到已分配的柜子: {assignlocker.LockerName}");

                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        SystemPrompt = $"柜号: {assignlocker.LockerName}，请先取出物品才可分配柜子";
                                        ShowLockerInfo = false;
                                        IsOpenLockerButtonEnabled = false;


                                        _isRecognitionCompleted = true;

                                        // 切换到开启柜门按钮
                                        IsSubmitEnabled = false;
                                        IsSubmitButtonVisible = true;
                                        IsOpenLockerButtonVisible = false;

                                        BusinessInfo1 = $"{assignlocker.LockerName}";
                                    });

                                    IsProcessingSubmit = false;
                                    return;
                                }
                            }
                        }
                        #endregion

                        #region 获取一个可用柜子
                        if (assignedLocker == null)
                        {
                            var locker = await _lockerService.GetAvailableLockerAsync();
                            if (locker == null)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    SystemPrompt = "当前无可用柜格，请稍后再试";
                                });
                                IsProcessingSubmit = false;
                                _isRecognitionCompleted = true;
                                return;
                            }
                            assignedLocker = locker;
                        }
                        #endregion

                        #region 分配柜子给用户
                        if (_userLocker == null && assignedLocker != null)
                        {
                            var assignLockerResult = await _lockerService.AssignLockerToUserAsync(_user.Id, assignedLocker.LockerId);
                            if (!assignLockerResult.Item1)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    SystemPrompt = "分配柜格失败，请稍后再试";
                                });
                                IsProcessingSubmit = false;
                                _isRecognitionCompleted = true;
                                return;
                            }
                            else
                            {
                                _logger.LogInformation($"分配柜子成功: {assignedLocker.LockerName}");
                                _userLocker = assignLockerResult.Item2;
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        _logger.LogInformation($"已有分配柜子: {_userLocker.Locker.LockerName}");
                        assignedLocker = await _lockerService.GetLockerAsync(_userLocker.LockerId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "分配柜格失败");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SystemPrompt = "分配柜格失败，请稍后再试";
                    });
                    IsProcessingSubmit = false;
                    return;
                }
                #endregion

                // 在UI线程更新界面
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (assignedLocker != null)
                        {
                            // 显示分配的柜格信息
                            BusinessInfo1 = $"{assignedLocker.LockerName}";
                            ShowLockerInfo = true;
                            SystemPrompt = $"柜号: {assignedLocker.LockerName}，请点击开启柜门";

                            // 切换到开启柜门按钮
                            IsSubmitButtonVisible = false;
                            IsOpenLockerButtonVisible = true;

                            // 打开柜门按钮可用
                            IsOpenLockerButtonEnabled = true;

                            //禁止再提交
                            IsSubmitEnabled = false;
                            _isRecognitionCompleted = true;

                            // 显示柜子名称弹窗
                            ShowLockerNameMask(assignedLocker.LockerName);

                            _logger.LogInformation("分配柜子成功，已切换到开启柜门按钮，柜号: {LockerName}", assignedLocker.LockerName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "更新UI时发生异常");
                    }
                });

                IsProcessingSubmit = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行提交操作时发生异常");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemPrompt = "提交过程中发生错误，请稍后重试";
                });

                IsProcessingSubmit = false;

                // 重置按钮状态
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowFaceRecognitionPrompt = true;
                    IsSubmitEnabled = false;
                    IsSubmitButtonVisible = true;
                    IsOpenLockerButtonVisible = false;
                    IsOpenLockerButtonEnabled = false;
                });
            }
        }
        #endregion

        #region 开启柜门
        /// <summary>
        /// 执行开启柜门操作
        /// </summary>
        public async Task ExecuteOpenLockerAsync()
        {
            try
            {
                _logger.LogInformation("执行打开柜门操作");

                if (_user == null)
                {
                    _logger.LogWarning("未找到用户信息，无法打开柜门");
                    SystemPrompt = "请先完成人脸识别";
                    return;
                }

                // 检查是否有分配的柜格
                if (_userLocker == null)
                {
                    _logger.LogWarning("未分配柜格，无法打开柜门");
                    SystemPrompt = "请先完成人脸识别";
                    return;
                }

                // 获取当前分配的柜格
                var locker = await _lockerService.GetLockerAsync(_userLocker.LockerId);
                if (locker == null)
                {
                    _logger.LogWarning("未找到柜格: {LockerName}", BusinessInfo1);
                    SystemPrompt = "柜格信息错误，无法打开";
                    return;
                }

                #region 打开柜格
                var resultOpenLocker = await _lockControlService.OpenLockAsync(locker.BoardAddress, locker.ChannelNumber);
                _logger.LogInformation("打开柜格: {LockerName}, 地址: {BoardAddress}, 通道: {ChannelNumber}, 结果: {Result}", locker.LockerName, locker.BoardAddress, locker.ChannelNumber, resultOpenLocker);

                if (resultOpenLocker)
                {
                    await _lockerService.UpdateUserLockerStatusAsync(_userLocker.UserLockerId, StorageStatus.Stored);
                    SystemPrompt = $"{locker.LockerName} 柜格已打开\n放入物品请关闭柜门";
                }
                await _accessLogService.LogAccessAsync(_user.Id, _user.UserName, locker.LockerId, locker.LockerName, AccessAction.Store, AccessResult.Success, $"存放物品");
                #endregion

                #region 等待柜格关闭
                _logger.LogInformation("等待柜格关闭，超时时间120秒...");
                var timeout = TimeSpan.FromSeconds(120);
                var startTime = DateTime.Now;
                bool lockerClosed = false;

                // 循环检查柜格状态，直到超时或柜门关闭
                while ((DateTime.Now - startTime) < timeout)
                {
                    try
                    {
                        // 读取柜格状态
                        var status = await _lockControlService.ReadOneStatusAsync(locker.BoardAddress, locker.ChannelNumber);

                        // 根据关门反馈锁逻辑：状态false表示关闭
                        if (status == false)
                        {
                            lockerClosed = true;
                            _logger.LogInformation($"{locker.LockerName} 柜格已关闭");
                            break;
                        }
                        else
                        {
                            _logger.LogDebug($"柜格状态: {(status == true ? "打开" : "关闭")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "读取柜格状态时发生异常");
                    }

                    // 每次检查间隔1秒
                    await Task.Delay(1000);
                }

                if (lockerClosed)
                {
                    // 柜门已关闭，显示提示并启动3秒倒计时
                    SystemPrompt = $"{locker.LockerName} 柜格已关闭，即将返回主界面";
                    StartReturnCountdown(3);
                }
                else
                {
                    // 超时仍未关闭，直接返回主界面
                    SystemPrompt = "柜格未关闭，即将返回主界面";
                    StartReturnCountdown(1);
                }
                #endregion
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开柜门操作时发生异常");
                SystemPrompt = "打开柜门时发生错误，请重试";
            }
        }
        #endregion

        #region 重拍
        /// <summary>
        /// 执行重拍操作
        /// </summary>
        public async Task ExecuteReTakeAsync()
        {
            try
            {
                _logger.LogInformation("执行重拍操作");

                // 停止所有定时器
                StopNoFaceTimer();

                // 停止遮罩定时器
                StopMaskTimer();

                // 重置人脸识别相关状态
                _isRecognitionRunning = false;
                _isRecognitionCompleted = false;
                _hasDetectedFaceInCycle = false;

                // 清空已捕获的人脸图像，等待重新识别
                _recognizedCapturedFaceImage = null;

                // 清空当前检测到的人脸框数据
                lock (_faceLock)
                {
                    _currentFaces = Array.Empty<WrapperFaceBox>();
                }

                // 重置用户头像
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UserAvatar = null;
                });

                // 更新系统提示，指示用户重新进行人脸识别
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemPrompt = "请重新正对摄像头进行识别";
                    ShowFaceRecognitionPrompt = true;

                    // 重置遮罩状态
                    ShowLockerMask = false;
                    MaskLockerName = string.Empty;
                });

                // 重置人脸检测超时定时器，准备重新检测
                if (_isCameraReady)
                {
                    StartNoFaceTimer();
                }

                _logger.LogInformation("重拍操作完成，已重置按钮状态");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重拍操作时发生异常");
                SystemPrompt = "重拍时发生错误，请重试";
            }
        }
        #endregion

        #region 执行返回主界面操作
        /// <summary>
        /// 执行返回主界面操作
        /// </summary>
        public async Task ExecuteReturnToMainAsync()
        {
            try
            {
                _logger.LogInformation("用户点击返回主界面按钮或倒计时结束");

                // 停止所有定时器和任务
                await CleanupResourcesAsync();

                // 通知应用状态服务返回主界面
                _appStateService.ReturnToMainWindow();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行返回主界面操作时发生异常");
            }
        }
        #endregion

        #region 清理所有资源
        /// <summary>
        /// 清理所有资源
        /// </summary>
        private async Task CleanupResourcesAsync()
        {
            try
            {
                _logger.LogInformation("开始清理资源");

                // 停止所有定时器
                StopNoFaceTimer();
                StopReturnCountdownTimer();
                StopSubmissionTimeoutTimer();

                // 停止遮罩定时器
                StopMaskTimer();

                // 取消所有任务
                _faceDetectionCts?.Cancel();

                // 停止人脸识别
                _isRecognitionRunning = false;

                // 清空人脸框数据
                lock (_faceLock)
                {
                    _currentFaces = Array.Empty<WrapperFaceBox>();
                }

                // 退订摄像头事件
                SafeUnsubscribeCameraEvents();

                // 停止摄像头服务
                if (_cameraService != null)
                {
                    await _cameraService.StopCameraAsync();
                    _logger.LogDebug("摄像头资源已清理");
                }

                // 隐藏遮罩
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowLockerMask = false;
                });

                // 重置所有状态
                _hasDetectedFaceInCycle = false;
                _isCameraReady = false;
                _isSubmissionTimeoutActive = false;

                _logger.LogInformation("资源清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理资源时发生异常");
            }
        }
        #endregion

        #region 验证手机号码格式
        /// <summary>
        /// 验证手机号码格式
        /// </summary>
        private bool IsValidPhoneNumber(string phoneNumber)
        {
            // 简单的手机号格式验证（11位数字，1开头）
            return Regex.IsMatch(phoneNumber, @"^1[3-9]\d{9}$");
        }
        #endregion

        #region IDisposable 实现
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _logger.LogInformation("开始释放StoreWindowViewModel资源");

                    // 停止所有定时器
                    StopNoFaceTimer();
                    StopReturnCountdownTimer();
                    StopSubmissionTimeoutTimer();

                    // 停止遮罩定时器
                    StopMaskTimer();

                    // 取消所有任务
                    _faceDetectionCts?.Cancel();
                    _faceDetectionCts?.Dispose();

                    // 清空人脸框数据
                    lock (_faceLock)
                    {
                        _currentFaces = Array.Empty<WrapperFaceBox>();
                    }

                    // 释放信号量
                    try
                    {
                        _faceDetectionLock?.Dispose();
                        _logger.LogDebug("人脸检测锁已释放");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "释放人脸检测锁时发生异常");
                    }

                    // 退订摄像头事件
                    SafeUnsubscribeCameraEvents();

                    // 异步停止摄像头
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _cameraService.StopCameraAsync();
                            _logger.LogDebug("摄像头服务已停止");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "停止摄像头服务时发生异常");
                        }
                    });

                    _logger.LogInformation("StoreWindowViewModel资源释放完成");
                }

                _isDisposed = true;
            }
        }
        #endregion
    }
}
