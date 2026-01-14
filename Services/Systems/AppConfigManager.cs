using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FaceLocker.Models.Settings;

namespace FaceLocker.Services
{
    /// <summary>
    /// 应用程序配置管理器实现
    /// 负责管理应用程序配置的读取和更新
    /// </summary>
    public class AppConfigManager : IAppConfigManager
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AppConfigManager> _logger;
        private readonly string _configFilePath;

        #region 初始化配置管理器
        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <param name="logger">日志服务</param>
        public AppConfigManager(IConfiguration configuration, ILogger<AppConfigManager> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            _logger.LogInformation("AppConfigManager 初始化完成，配置文件路径: {ConfigFilePath}", _configFilePath);
        }
        #endregion

        #region 获取指定路径的配置值
        /// <summary>
        /// 获取指定路径的配置值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <returns>配置值</returns>
        public T GetValue<T>(string key)
        {
            try
            {
                var value = _configuration.GetValue<T>(key);
                _logger.LogDebug("获取配置值: {Key} = {Value}", key, value);
                return value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取配置值时发生错误: {Key}", key);
                return default(T);
            }
        }
        #endregion

        #region 更新指定路径的配置值
        /// <summary>
        /// 更新指定路径的配置值
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">新值</param>
        public void UpdateValue(string key, object value)
        {
            try
            {
                _logger.LogInformation("更新配置值: {Key} = {Value}", key, value);

                // 读取现有的配置文件
                var jsonString = File.ReadAllText(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return;
                }

                // 分割键路径
                var keys = key.Split(':');
                var currentNode = jsonObject;

                // 导航到目标节点（创建不存在的中间节点）
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (currentNode[keys[i]] == null)
                    {
                        currentNode[keys[i]] = new JsonObject();
                    }
                    currentNode = currentNode[keys[i]];
                }

                // 设置值
                var finalKey = keys[keys.Length - 1];
                currentNode[finalKey] = JsonValue.Create(value);

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("配置值更新成功: {Key} = {Value}", key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新配置值时发生错误: {Key}", key);
                throw;
            }
        }
        #endregion

        #region 添加新的配置节点
        /// <summary>
        /// 添加新的配置节点
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">值</param>
        public void AddNode(string key, object value)
        {
            UpdateValue(key, value);
        }
        #endregion

        #region 将当前配置保存到文件
        /// <summary>
        /// 将当前配置保存到文件
        /// </summary>
        public void Save()
        {
            try
            {
                _logger.LogInformation("保存配置到文件: {ConfigFilePath}", _configFilePath);
                // 在 UpdateValue 中已经实时保存，这里只需要记录日志
                _logger.LogInformation("配置保存完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存配置到文件时发生错误");
                throw;
            }
        }
        #endregion

        #region 配置类异步操作方法

        #region 保存配置方法
        /// <summary>
        /// 保存百度人脸识别设置
        /// </summary>
        /// <param name="settings">百度人脸识别设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveBaiduFaceAsync(BaiduFaceSettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存百度人脸识别设置");

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个BaiduFace节点
                var baiduFaceNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["BaiduFace"] = baiduFaceNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("百度人脸识别设置保存成功，识别分数阈值: {IdentifyScore}, 模型路径: {ModelPath}",
                    settings.IdentifyScore, settings.ModelPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存百度人脸识别设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存摄像头设置
        /// </summary>
        /// <param name="settings">摄像头设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveCameraAsync(CameraSettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存摄像头设置，摄像头索引: {DefaultCameraIndex}, 设备路径: {DevicePath}",
                    settings.DefaultCameraIndex, settings.DevicePath);

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个Camera节点
                var cameraNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["Camera"] = cameraNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("摄像头设置保存成功，分辨率: {Width}x{Height}, 帧率: {FrameRate}",
                    settings.Resolution.Width, settings.Resolution.Height, settings.FrameRate);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存摄像头设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存数据库设置
        /// </summary>
        /// <param name="settings">数据库设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveDatabaseAsync(DatabaseSettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存数据库设置，数据目录: {DataDirectory}", settings.DataDirectory);

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个Database节点
                var databaseNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["Database"] = databaseNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("数据库设置保存成功，连接字符串: {ConnectionString}", settings.ConnectionString);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存数据库设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存锁控制器设置
        /// </summary>
        /// <param name="settings">锁控制器设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveLockControllerAsync(LockControllerSettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存锁控制器设置，组名称: {GroupName}, IP地址: {IPAddress}, 锁控板数量: {BoardCount}",
                    settings.GroupName, settings.IPAddress, settings.Boards?.Count ?? 0);

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个LockController节点
                var lockControllerNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["LockController"] = lockControllerNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("锁控制器设置保存成功，波特率: {BaudRate}, 数据位: {DataBits}",
                    settings.BaudRate, settings.DataBits);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存锁控制器设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存服务器设置
        /// </summary>
        /// <param name="settings">服务器设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveServerAsync(ServerSettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存服务器设置，设备名称: {DeviceName}, 服务器地址: {ServerAddress}:{ServerPort}",
                    settings.DeviceName, settings.ServerAddress, settings.ServerPort);

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个Server节点
                var serverNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["Server"] = serverNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("服务器设置保存成功，设备名称: {DeviceName}, 服务器地址: {ServerAddress}:{ServerPort},检测间隔: {CheckInterval}秒",
                   settings.DeviceName, settings.ServerAddress, settings.ServerPort, settings.CheckInterval);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存服务器设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存安全设置
        /// </summary>
        /// <param name="settings">安全设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveSecurityAsync(SecuritySettings settings)
        {
            try
            {
                _logger.LogInformation("开始保存安全设置");

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 序列化设置对象为JsonNode并替换整个Security节点
                var securityNode = JsonSerializer.SerializeToNode(settings);
                jsonObject["Security"] = securityNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("安全设置保存成功，会话超时: {SessionTimeout}分钟", settings.SessionTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存安全设置时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 保存完整的应用程序设置
        /// </summary>
        /// <param name="appSettings">应用程序设置对象</param>
        /// <returns>保存是否成功</returns>
        public async Task<bool> SaveAppSettingsAsync(AppSettings appSettings)
        {
            try
            {
                _logger.LogInformation("开始保存完整的应用程序设置");

                var jsonString = await File.ReadAllTextAsync(_configFilePath);
                var jsonObject = JsonNode.Parse(jsonString);

                if (jsonObject == null)
                {
                    _logger.LogError("无法解析配置文件: {ConfigFilePath}", _configFilePath);
                    return false;
                }

                // 分别保存各个配置节
                var baiduFaceNode = JsonSerializer.SerializeToNode(appSettings.BaiduFace);
                jsonObject["BaiduFace"] = baiduFaceNode;

                var cameraNode = JsonSerializer.SerializeToNode(appSettings.Camera);
                jsonObject["Camera"] = cameraNode;

                var databaseNode = JsonSerializer.SerializeToNode(appSettings.Database);
                jsonObject["Database"] = databaseNode;

                var lockControllerNode = JsonSerializer.SerializeToNode(appSettings.LockController);
                jsonObject["LockController"] = lockControllerNode;

                var serverNode = JsonSerializer.SerializeToNode(appSettings.Server);
                jsonObject["Server"] = serverNode;

                var securityNode = JsonSerializer.SerializeToNode(appSettings.Security);
                jsonObject["Security"] = securityNode;

                // 保存回文件
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_configFilePath, jsonObject.ToJsonString(options));

                _logger.LogInformation("完整的应用程序设置保存成功");
                _logger.LogInformation("应用程序设置摘要 - 设备名称: {DeviceName}, 服务器地址: {ServerAddress}:{ServerPort}, 人脸识别分数: {IdentifyScore}",
                    appSettings.Server.DeviceName, appSettings.Server.ServerAddress, appSettings.Server.ServerPort,
                    appSettings.BaiduFace.IdentifyScore);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存完整的应用程序设置时发生错误");
                return false;
            }
        }
        #endregion

        #region 获取配置方法
        /// <summary>
        /// 获取百度人脸识别设置
        /// </summary>
        /// <returns>百度人脸识别设置对象</returns>
        public async Task<BaiduFaceSettings> GetBaiduFaceAsync()
        {
            try
            {
                _logger.LogDebug("开始获取百度人脸识别设置");

                var settings = _configuration.GetSection("BaiduFace").Get<BaiduFaceSettings>();

                if (settings != null)
                {
                    _logger.LogDebug("百度人脸识别设置获取成功，识别分数阈值: {IdentifyScore}, 模型路径: {ModelPath}",
                        settings.IdentifyScore, settings.ModelPath);
                }
                else
                {
                    _logger.LogWarning("百度人脸识别设置获取为空，返回默认设置");
                    settings = new BaiduFaceSettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取百度人脸识别设置时发生错误");
                return await Task.FromResult(new BaiduFaceSettings());
            }
        }

        /// <summary>
        /// 获取摄像头设置
        /// </summary>
        /// <returns>摄像头设置对象</returns>
        public async Task<CameraSettings> GetCameraAsync()
        {
            try
            {
                _logger.LogDebug("开始获取摄像头设置");

                var settings = _configuration.GetSection("Camera").Get<CameraSettings>();

                if (settings != null)
                {
                    _logger.LogDebug("摄像头设置获取成功，默认摄像头索引: {DefaultCameraIndex}, 分辨率: {Width}x{Height}",
                        settings.DefaultCameraIndex, settings.Resolution?.Width, settings.Resolution?.Height);
                }
                else
                {
                    _logger.LogWarning("摄像头设置获取为空，返回默认设置");
                    settings = new CameraSettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取摄像头设置时发生错误");
                return await Task.FromResult(new CameraSettings());
            }
        }

        /// <summary>
        /// 获取数据库设置
        /// </summary>
        /// <returns>数据库设置对象</returns>
        public async Task<DatabaseSettings> GetDatabaseAsync()
        {
            try
            {
                _logger.LogDebug("开始获取数据库设置");

                var settings = _configuration.GetSection("Database").Get<DatabaseSettings>();

                if (settings != null)
                {
                    _logger.LogDebug("数据库设置获取成功，数据目录: {DataDirectory}", settings.DataDirectory);
                }
                else
                {
                    _logger.LogWarning("数据库设置获取为空，返回默认设置");
                    settings = new DatabaseSettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取数据库设置时发生错误");
                return await Task.FromResult(new DatabaseSettings());
            }
        }

        /// <summary>
        /// 获取锁控制器设置
        /// </summary>
        /// <returns>锁控制器设置对象</returns>
        public async Task<LockControllerSettings> GetLockControllerAsync()
        {
            try
            {
                _logger.LogDebug("开始获取锁控制器设置");

                var settings = _configuration.GetSection("LockController").Get<LockControllerSettings>();

                if (settings != null)
                {
                    _logger.LogDebug("锁控制器设置获取成功，组名称: {GroupName}, 锁控板数量: {BoardCount}",
                        settings.GroupName, settings.Boards?.Count ?? 0);
                }
                else
                {
                    _logger.LogWarning("锁控制器设置获取为空，返回默认设置");
                    settings = new LockControllerSettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取锁控制器设置时发生错误");
                return await Task.FromResult(new LockControllerSettings());
            }
        }

        /// <summary>
        /// 获取服务器设置
        /// </summary>
        /// <returns>服务器设置对象</returns>
        public async Task<ServerSettings> GetServerAsync()
        {
            try
            {
                _logger.LogDebug("开始获取服务器设置");

                var settings = _configuration.GetSection("Server").Get<ServerSettings>();

                if (settings != null)
                {
                    _logger.LogInformation("服务器设置获取成功，设备名称: {DeviceName}, 服务器地址: {ServerAddress}:{ServerPort},检测间隔: {CheckInterval}秒",
                 settings.DeviceName, settings.ServerAddress, settings.ServerPort, settings.CheckInterval);
                }
                else
                {
                    _logger.LogWarning("服务器设置获取为空，返回默认设置");
                    settings = new ServerSettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取服务器设置时发生错误");
                return await Task.FromResult(new ServerSettings());
            }
        }

        /// <summary>
        /// 获取安全设置
        /// </summary>
        /// <returns>安全设置对象</returns>
        public async Task<SecuritySettings> GetSecurityAsync()
        {
            try
            {
                _logger.LogDebug("开始获取安全设置");

                var settings = _configuration.GetSection("Security").Get<SecuritySettings>();

                if (settings != null)
                {
                    _logger.LogDebug("安全设置获取成功，会话超时: {SessionTimeout}分钟", settings.SessionTimeout);
                }
                else
                {
                    _logger.LogWarning("安全设置获取为空，返回默认设置");
                    settings = new SecuritySettings();
                }

                return await Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取安全设置时发生错误");
                return await Task.FromResult(new SecuritySettings());
            }
        }

        /// <summary>
        /// 获取完整的应用程序设置
        /// </summary>
        /// <returns>应用程序设置对象</returns>
        public async Task<AppSettings> GetAppSettingsAsync()
        {
            try
            {
                _logger.LogDebug("开始获取完整的应用程序设置");

                var appSettings = new AppSettings();

                // 分别获取各个部分的设置
                appSettings.BaiduFace = await GetBaiduFaceAsync();
                appSettings.Camera = await GetCameraAsync();
                appSettings.Database = await GetDatabaseAsync();
                appSettings.LockController = await GetLockControllerAsync();
                appSettings.Server = await GetServerAsync();
                appSettings.Security = await GetSecurityAsync();

                _logger.LogDebug("完整的应用程序设置获取成功，包含所有配置节");
                _logger.LogDebug("应用程序设置详情 - 服务器: {DeviceName}, 人脸识别分数: {IdentifyScore}, 摄像头索引: {CameraIndex}",
                    appSettings.Server.DeviceName, appSettings.BaiduFace.IdentifyScore, appSettings.Camera.DefaultCameraIndex);

                return appSettings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取完整的应用程序设置时发生错误");
                return await Task.FromResult(new AppSettings());
            }
        }
        #endregion

        #endregion
    }
}