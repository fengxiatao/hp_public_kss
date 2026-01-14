using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FaceLocker.Extensions;
using FaceLocker.Models.Settings;
using FaceLocker.Services;
using FaceLocker.ViewModels;
using FaceLocker.ViewModels.NumPad;
using FaceLocker.Views;
using FaceLocker.Views.NumPad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FaceLocker
{
    public partial class App : Application
    {
        #region 静态属性
        private static IServiceProvider? _serviceProvider;
        private static IConfiguration? _configuration;

        public static IConfiguration? Configuration => _configuration;

        public static IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Service provider has not been initialized.");

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static Window? MainWindow => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        #endregion

        #region 应用程序初始化
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // 解决与 CommunityToolkit.Mvvm 的 DataValidation 插件冲突
            if (BindingPlugins.DataValidators.Count > 0)
            {
                var plugin = BindingPlugins.DataValidators[0];
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }


        public override void OnFrameworkInitializationCompleted()
        {
            // 1. 配置配置文件
            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection();

            // 2. 日志和配置服务
            services.AddLogging(builder =>
            {
                builder.ClearProviders(); // 清除默认提供程序

                builder.AddConsole(options =>
                {
                    options.FormatterName = "simpleLog";
                });
                builder.AddConsoleFormatter<SimpleLogFormatter, ConsoleFormatterOptions>();

                builder.SetMinimumLevel(LogLevel.Trace);
            });

            // 3. 添加 AppSettings 配置
            services.Configure<AppSettings>(_configuration);

            // 4. EF Core Sqlite（Ubuntu ARM64 可用）
            var connectionString = _configuration.GetSection("Database:ConnectionString").Value ?? "Data Source=./Data/database.db";

            services.AddDbContext<FaceLockerDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
                //options.EnableDetailedErrors(true);
                //options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
            }, contextLifetime: ServiceLifetime.Scoped);

            services.AddSingleton(_configuration);

            // 5. 配置管理服务
            services.AddSingleton<IAppConfigManager, AppConfigManager>();

            // 6. 核心服务
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<IAdminLoginService, AdminLoginService>();
            services.AddSingleton<ISessionAuthService, SessionAuthService>();
            services.AddSingleton<IOperationModeService, OperationModeService>();
            services.AddSingleton<IAppStateService, AppStateService>();

            // 7. 业务服务
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IRoleService, RoleService>();
            services.AddScoped<IEnvironmentCheckService, EnvironmentCheckService>();
            services.AddSingleton<BaiduFaceService>();
            services.AddScoped<ILockerService, LockerService>();
            services.AddSingleton<ILockControlService, LockControlService>();
            services.AddScoped<IAccessLogService, AccessLogService>();
            // 摄像头服务配置：
            // 方案1：零拷贝视频渲染 + GStreamer cairooverlay 绘制人脸框（推荐）
            services.AddSingleton<ICameraService, NativeVideoCameraService>();
            // 备选软件渲染实现已移除，避免项目长期残留僵尸代码
            services.AddSingleton<IDataSyncService, DataSyncService>();

            // 8. 网络通讯服务
            services.AddSingleton<INetworkService, NetworkService>();

            // 9. ViewModels
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<AdminLoginViewModel>();
            services.AddTransient<StoreWindowViewModel>();
            services.AddTransient<RetrieveWindowViewModel>();
            services.AddSingleton<NumPadDialogViewModel>();
            services.AddTransient<NumPadDialogView>();

           

            // 10. Views
            services.AddTransient<MainWindow>();
            services.AddTransient<AdminLoginWindow>();
            services.AddTransient<StoreWindow>();
            services.AddTransient<RetrieveWindow>();
            services.AddTransient<AdminMainWindow>();
            services.AddTransient<AdminLockView>();
            services.AddTransient<AdminUserView>();
            services.AddTransient<AdminSettingsView>();
            services.AddTransient<AdminAccessLogViewModel>();

            // 11. ReactiveUI 相关服务
            services.AddSingleton<ViewLocator>();

            // 12. 注册数据库初始化服务
            services.AddSingleton<IDatabaseStartupInitializer, DatabaseStartupInitializer>();

            _serviceProvider = services.BuildServiceProvider();

            // 13. 数据库初始化
            using (var scope = _serviceProvider.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseStartupInitializer>();
                initializer.InitializeAsync().GetAwaiter().GetResult();
            }

            // 14.  预初始化关键服务
            _ = PreInitializeServicesAsync();

            // 15. 创建主窗口和ViewModel，属性注入Window
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 获取 AppStateService 并设置 DesktopLifetime 和 MainWindow
                var appStateService = _serviceProvider.GetRequiredService<IAppStateService>();
                appStateService.DesktopLifetime = desktop;

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                mainWindowViewModel.WindowReference = mainWindow;
                mainWindow.DataContext = mainWindowViewModel;

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public static IServiceProvider GetServiceProvider()
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("Service provider has not been initialized. Call BuildServiceProvider() first.");

            return _serviceProvider;
        }

        public static T GetService<T>() where T : class
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("Service provider has not been initialized.");
            return _serviceProvider.GetRequiredService<T>();
        }
        #endregion

        #region 预初始化关键服务
        /// <summary>
        /// 预初始化关键服务（异步，不阻塞UI）
        /// </summary>
        private async Task PreInitializeServicesAsync()
        {
            try
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("开始预初始化关键服务...");

                // 预初始化百度人脸服务 - 使用单独的作用域
                using (var faceScope = _serviceProvider.CreateScope())
                {
                    var baiduFaceService = faceScope.ServiceProvider.GetRequiredService<BaiduFaceService>();
                    await Task.Run(() => baiduFaceService.InitializeSDK());
                }

                // 预初始化摄像头服务
                await InitializeCameraServiceAsync();

                // GStreamer 摄像头服务不需要预初始化，因为已经是 Singleton
                var cameraService = _serviceProvider.GetRequiredService<ICameraService>();
                logger.LogInformation("GStreamer 摄像头服务准备就绪，类型: {CameraServiceType}", cameraService.GetType().Name);

                logger.LogInformation("关键服务预初始化完成");

            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "预初始化关键服务时发生异常");
            }
        }
        #endregion

        #region 初始化摄像头服务
        /// <summary>
        /// 初始化摄像头服务
        /// </summary>
        private async Task InitializeCameraServiceAsync()
        {
            try
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("开始初始化摄像头服务");

                var cameraService = _serviceProvider.GetRequiredService<ICameraService>();

                // 检查摄像头服务是否可用
                if (!cameraService.IsCameraAvailable)
                {
                    logger.LogWarning("摄像头服务初始化时设备不可用，但继续尝试健康检查");
                }

                // 检查摄像头健康状态（检测）
                var healthCheckResult = await cameraService.CheckCameraHealthAsync();

                if (healthCheckResult)
                {
                    logger.LogInformation("摄像头服务初始化成功，摄像头设备正常");

                    // 记录详细的摄像头设备信息
                    await LogCameraDeviceDetails();
                }
                else
                {
                    logger.LogWarning("摄像头服务初始化完成，但摄像头设备检测存在问题");
                }

                logger.LogInformation("摄像头服务初始化完成，类型: {CameraServiceType}, 设备可用: {IsAvailable}, 健康状态: {HealthStatus}",
                    cameraService.GetType().Name, cameraService.IsCameraAvailable, healthCheckResult);
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "初始化摄像头服务时发生异常");
            }
        }
        #endregion

        #region 记录摄像头设备详细信息
        /// <summary>
        /// 记录摄像头设备详细信息
        /// </summary>
        private async Task LogCameraDeviceDetails()
        {
            try
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("开始记录摄像头设备详细信息");

                // 获取配置的摄像头设备路径
                var cameraSettings = _configuration.GetSection("Camera");
                var devicePath = cameraSettings.GetValue<string>("DevicePath") ?? "/dev/video12";

                // 获取指定摄像头设备的详细信息
                var deviceDetails = await ExecuteShellCommand($"v4l2-ctl --device={devicePath} --all");
                if (!string.IsNullOrEmpty(deviceDetails))
                {
                    logger.LogInformation("摄像头设备 {DevicePath} 详细信息: {DeviceDetails}", devicePath, deviceDetails);
                }

                // 获取摄像头设备支持的格式
                var formats = await ExecuteShellCommand($"v4l2-ctl --device={devicePath} --list-formats-ext");
                if (!string.IsNullOrEmpty(formats))
                {
                    logger.LogInformation("摄像头设备支持的格式: {Formats}", formats);
                }

                logger.LogInformation("摄像头设备详细信息记录完成");
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "记录摄像头设备详细信息时发生异常");
            }
        }
        #endregion

        #region 执行 shell 命令
        /// <summary>
        /// 执行 shell 命令
        /// </summary>
        public static async Task<string> ExecuteShellCommand(string command)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                    logger.LogWarning("执行命令时出现错误输出: {Command}, 错误: {Error}", command, error);
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "执行 shell 命令时发生异常: {Command}", command);
                return string.Empty;
            }
        }
        #endregion
    }
}