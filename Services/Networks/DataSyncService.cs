using FaceLocker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 数据同步服务实现
    /// </summary>
    public class DataSyncService : IDataSyncService
    {
        #region 私有字段

        private readonly IConfiguration _configuration;
        private readonly IAppConfigManager _appConfigManager;
        private readonly ILogger<DataSyncService> _logger;
        private readonly FaceLockerDbContext _dbContext;
        private readonly BaiduFaceService _baiduFaceService;
        private readonly IServiceProvider _serviceProvider;
        private INetworkService _networkService;
        private bool _isSyncing = false;
        private DateTime? _lastSyncTime = null;

        // 用于等待服务器响应的同步对象
        private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingRequests = new();
        private readonly object _pendingRequestsLock = new object();

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        #endregion

        #region 构造函数

        public DataSyncService(
            IConfiguration configuration,
            IAppConfigManager appConfigManager,
            ILogger<DataSyncService> logger,
            FaceLockerDbContext dbContext,
            BaiduFaceService baiduFaceService,
            IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _appConfigManager = appConfigManager;
            _logger = logger;
            _dbContext = dbContext;
            _baiduFaceService = baiduFaceService;
            _serviceProvider = serviceProvider;

            _logger.LogInformation("数据同步服务初始化完成");

        }

        #endregion

        #region 初始化和管理

        public void SetNetworkService(INetworkService networkService)
        {
            _networkService = networkService;
            _logger.LogInformation("数据同步服务已设置网络服务引用");
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.LogInformation("正在初始化数据同步服务");

                if (_networkService == null)
                {
                    _logger.LogWarning("网络服务未设置，数据同步服务无法完全初始化");
                    return false;
                }

                // 注册网络服务事件
                _networkService.DataReceived += OnNetworkDataReceived;
                _networkService.ConnectionStatusChanged += OnNetworkConnectionStatusChanged;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据同步服务初始化失败");
                return false;
            }
        }
        #endregion

        #region 核心数据处理方法

        /// <summary>
        /// 统一的数据处理方法 - 处理所有类型的数据
        /// </summary>
        /// <param name="dataType">数据类型</param>
        /// <param name="data">JSON数据</param>
        /// <returns>处理结果</returns>
        private async Task<bool> ProcessDataAsync(string dataType, string data)
        {
            try
            {
                _logger.LogInformation("开始处理 {DataType} 数据", dataType);

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("{DataType} 数据为空", dataType);
                    return false;
                }

                bool result = dataType.ToLower() switch
                {
                    "boards" => await ProcessBoardsDataAsync(data),
                    "roles" => await ProcessRolesDataAsync(data),
                    "users" => await ProcessUsersDataAsync(data),
                    "lockers" => await ProcessLockersDataAsync(data),
                    "userlockers" => await ProcessUserLockersDataAsync(data),
                    _ => throw new ArgumentException($"未知的数据类型: {dataType}")
                };

                // 如果是用户数据同步成功，启动人脸特征生成流程
                if (result && dataType.ToLower() == "users")
                {
                    _ = Task.Run(async () => await GenerateFaceFeaturesInSeparateScopeAsync());
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 {DataType} 数据时发生异常", dataType);
                return false;
            }
        }
        #endregion

        #region 在独立作用域中为人脸特征生成数据

        /// <summary>
        /// 在独立的作用域中生成人脸特征数据，避免DbContext并发冲突
        /// </summary>
        /// <returns>处理结果</returns>
        private async Task<bool> GenerateFaceFeaturesInSeparateScopeAsync()
        {
            _logger.LogInformation("开始在独立作用域中生成人脸特征数据");

            // 创建独立的作用域，避免DbContext并发冲突
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            try
            {
                var dbContext = scopedServices.GetRequiredService<FaceLockerDbContext>();
                var baiduFaceService = scopedServices.GetRequiredService<BaiduFaceService>();

                _logger.LogInformation("独立作用域服务获取成功，开始查询需要生成人脸特征的用户");

                // 先获取所有有头像的用户，然后在内存中过滤
                var usersWithAvatar = await dbContext.Users
                    .Where(u => !string.IsNullOrEmpty(u.Avatar))
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogInformation("在独立作用域中找到 {UserCount} 个需要生成人脸特征数据的用户", usersWithAvatar.Count);

                int successCount = 0;
                int failureCount = 0;
                var failedUsers = new List<string>();

                // 为每个用户生成人脸特征数据
                foreach (var user in usersWithAvatar)
                {
                    try
                    {
                        _logger.LogInformation("开始为用户 {UserName} (ID: {UserId}) 生成人脸特征数据",
                            user.Name, user.Id);

                        // 重新从数据库获取用户实体，确保在当前的DbContext中跟踪
                        var trackedUser = await dbContext.Users.FindAsync(user.Id);
                        if (trackedUser == null)
                        {
                            _logger.LogWarning("用户 {UserName} (ID: {UserId}) 在数据库中不存在，跳过处理", user.Name, user.Id);
                            failureCount++;
                            continue;
                        }

                        #region 实例化和初始化百度人脸识别服务
                        baiduFaceService.InitializeSDK();

                        #endregion

                        bool result = await baiduFaceService.GenerateFaceFeatureFromAvatarAsync(trackedUser);

                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation("成功为用户 {UserName} 生成人脸特征数据", user.Name);
                        }
                        else
                        {
                            failureCount++;
                            failedUsers.Add($"{user.Name} (ID: {user.Id})");
                            _logger.LogWarning("为用户 {UserName} 生成人脸特征数据失败", user.Name);
                        }

                        // 添加短暂延迟，避免对百度API造成过大压力
                        await Task.Delay(300);
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        failedUsers.Add($"{user.Name} (ID: {user.Id})");
                        _logger.LogError(ex, "为用户 {UserName} 生成人脸特征数据时发生异常", user.Name);
                    }
                }

                _logger.LogInformation("独立作用域中的人脸特征数据生成完成：成功 {SuccessCount} 个，失败 {FailureCount} 个",
                    successCount, failureCount);

                if (failureCount > 0)
                {
                    _logger.LogWarning("以下用户的人脸特征数据生成失败：{FailedUsers}",
                        string.Join("; ", failedUsers));
                }

                return failureCount == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在独立作用域中生成人脸特征数据时发生异常");
                return false;
            }
        }
        #endregion

        #region 执行数据覆盖操作 - 先删除本地数据，再添加新数据
        /// <summary>
        /// 执行数据覆盖操作 - 先删除本地数据，再添加新数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="dataType">数据类型描述</param>
        /// <param name="deleteAction">删除操作</param>
        /// <param name="addAction">添加操作</param>
        /// <param name="items">要添加的数据项</param>
        /// <returns>处理结果</returns>
        private async Task<bool> PerformDataOverwriteAsync<T>(
            string dataType,
            Func<Task<bool>> deleteAction,
            Func<T, Task<bool>> addAction,
            IEnumerable<T> items)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("开始覆盖 {DataType} 数据，共 {Count} 项", dataType, items.Count());

                // 步骤1: 删除现有数据
                _logger.LogInformation("正在删除现有 {DataType} 数据", dataType);
                bool deleteSuccess = await deleteAction();
                if (!deleteSuccess)
                {
                    _logger.LogError("删除 {DataType} 数据失败", dataType);
                    await transaction.RollbackAsync();
                    return false;
                }
                _logger.LogInformation("成功删除现有 {DataType} 数据", dataType);

                // 步骤2: 添加新数据
                _logger.LogInformation("正在添加新的 {DataType} 数据", dataType);
                int successCount = 0;
                int errorCount = 0;
                var errorItems = new List<string>();

                foreach (var item in items)
                {
                    try
                    {
                        bool addSuccess = await addAction(item);
                        if (addSuccess)
                        {
                            successCount++;
                            _logger.LogDebug("成功添加 {DataType} 数据项", dataType);
                        }
                        else
                        {
                            errorCount++;
                            errorItems.Add(item?.ToString() ?? "未知项");
                            _logger.LogWarning("添加 {DataType} 数据项失败: {Item}", dataType, item);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        errorItems.Add(item?.ToString() ?? "未知项");
                        _logger.LogError(ex, "添加 {DataType} 数据项时发生异常: {Item}", dataType, item);
                    }
                }

                if (errorCount > 0)
                {
                    _logger.LogWarning("{DataType} 数据覆盖部分失败：成功 {SuccessCount} 项，失败 {ErrorCount} 项。失败项: {ErrorItems}",
                        dataType, successCount, errorCount, string.Join("; ", errorItems));

                    // 如果有失败项，回滚事务
                    await transaction.RollbackAsync();
                    _logger.LogError("{DataType} 数据覆盖失败，已回滚事务", dataType);
                    return false;
                }

                // 提交事务
                await transaction.CommitAsync();

                _logger.LogInformation("{DataType} 数据覆盖完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项",
                    dataType, successCount, errorCount);

                return errorCount == 0;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "覆盖 {DataType} 数据时发生异常，已回滚事务", dataType);
                return false;
            }
        }

        #endregion

        #region 处理锁控板数据
        /// <summary>
        /// 处理锁控板数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<bool> ProcessBoardsDataAsync(string data)
        {
            _logger.LogInformation("开始处理锁控板数据");

            if (string.IsNullOrWhiteSpace(data))
            {
                _logger.LogWarning("锁控板数据为空");
                return await Task.FromResult(false);
            }

            ServerBoardResponse boardResponse = null;
            ServerBoardData boardData = null;
            try
            {
                boardResponse = JsonSerializer.Deserialize<ServerBoardResponse>(data, _jsonOptions);
                if (boardResponse == null)
                {
                    _logger.LogWarning("无法解析锁控板数据");
                    return await Task.FromResult(false);
                }
                else
                {
                    boardData = boardResponse.Data;
                }

                _logger.LogInformation("正在处理 {DeviceName} 锁控板数据", boardResponse.DeviceName);

                if (boardData == null)
                {
                    _logger.LogWarning("无法获取锁控板数据");
                }
                var groupName = boardData.GroupName;
                var ipAddress = boardData.IPAddress;
                var direction = boardData.Direction;
                var boards = boardData.Boards;
                if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(ipAddress) || direction <= 0 || boards == null || boards.Length <= 0)
                {
                    _logger.LogWarning("无法获取有效的锁控板数据");
                    return await Task.FromResult(false);
                }
                //写入appsettings.json
                _appConfigManager.UpdateValue("LockController:GroupName", groupName);
                _appConfigManager.UpdateValue("LockController:IPAddress", ipAddress);
                _appConfigManager.UpdateValue("LockController:Direction", direction);
                _appConfigManager.UpdateValue("LockController:Boards", boards);
                _appConfigManager.Save();
                _logger.LogInformation("成功处理 {DeviceName} 锁控板数据", boardResponse.DeviceName);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理锁控板数据失败");
                return false;
            }
        }
        #endregion

        #region 处理角色数据

        public async Task<bool> ProcessRolesDataAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始处理角色数据");

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("角色数据为空");
                    return await EnsureSuperAdminRoleExistsAsync();
                }

                ServerRoleResponse roleResponse = null;
                ServerRoleData[]? rolesData = null;

                try
                {
                    roleResponse = JsonSerializer.Deserialize<ServerRoleResponse>(data, _jsonOptions);
                    if (roleResponse == null)
                    {
                        _logger.LogWarning("角色数据解析失败，尝试直接解析数组格式");
                        // 尝试直接解析数组格式
                        rolesData = JsonSerializer.Deserialize<ServerRoleData[]>(data, _jsonOptions);
                        if (rolesData == null)
                        {
                            _logger.LogError("角色数据两种解析方式都失败");
                            return false;
                        }
                    }
                    else
                    {
                        rolesData = roleResponse.Data;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "角色数据JSON格式错误");
                    return false;
                }

                if (rolesData == null || rolesData.Length == 0)
                {
                    _logger.LogWarning("角色数据为空数组");
                    return await EnsureSuperAdminRoleExistsAsync(); // 空数据也确保超级管理员存在
                }

                _logger.LogInformation("收到 {RoleCount} 个角色数据", rolesData.Length);

                // 转换服务器数据到本地模型
                var localRoles = new List<Role>();
                ServerRoleData? serverSuperAdminRole = null;
                int conversionSuccessCount = 0;
                int conversionErrorCount = 0;

                foreach (var serverRole in rolesData)
                {
                    try
                    {
                        var localRole = ConvertServerRoleToLocalRole(serverRole);
                        if (localRole != null && localRole.Id > 0)
                        {
                            // 检查是否为超级管理员角色
                            if (localRole.Id == 1)
                            {
                                serverSuperAdminRole = serverRole;
                                _logger.LogInformation("发现服务器超级管理员角色，将用于更新本地超级管理员角色");
                            }
                            else
                            {
                                localRoles.Add(localRole);
                            }
                            conversionSuccessCount++;
                            _logger.LogDebug("成功转换角色: {RoleId} - {RoleName}", localRole.Id, localRole.Name);
                        }
                        else
                        {
                            conversionErrorCount++;
                            _logger.LogWarning("角色转换失败，ID为空或转换结果为null: {ServerRole}",
                                JsonSerializer.Serialize(serverRole));
                        }
                    }
                    catch (Exception ex)
                    {
                        conversionErrorCount++;
                        _logger.LogError(ex, "转换角色数据时发生异常: {ServerRole}",
                            JsonSerializer.Serialize(serverRole));
                    }
                }

                _logger.LogInformation("角色数据转换完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项",
                    conversionSuccessCount, conversionErrorCount);

                // 执行数据覆盖操作 - 保留ID="1"的超级管理员角色
                return await PerformRolesDataOverwriteAsync(localRoles, serverSuperAdminRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理角色数据失败");
                return false;
            }
        }
        #endregion

        #region 处理用户数据
        /// <summary>
        /// 处理用户数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<bool> ProcessUsersDataAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始处理用户数据");

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("用户数据为空");
                    return await EnsureAdminUserExistsAsync();
                }

                ServerUserResponse userResponse = null;
                ServerUserData[]? usersData = null;

                try
                {
                    userResponse = JsonSerializer.Deserialize<ServerUserResponse>(data, _jsonOptions);
                    if (userResponse == null)
                    {
                        _logger.LogWarning("用户数据解析失败，尝试直接解析数组格式");
                        // 尝试直接解析数组格式
                        usersData = JsonSerializer.Deserialize<ServerUserData[]>(data, _jsonOptions);
                        if (usersData == null)
                        {
                            _logger.LogError("用户数据两种解析方式都失败");
                            return false;
                        }
                    }
                    else
                    {
                        usersData = userResponse.Data;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "用户数据JSON格式错误");
                    return false;
                }

                if (usersData == null || usersData.Length == 0)
                {
                    _logger.LogWarning("用户数据为空数组");
                    return await EnsureAdminUserExistsAsync(); // 空数据也确保admin用户存在
                }

                _logger.LogInformation("收到 {UserCount} 个用户数据", usersData.Length);

                // 转换服务器数据到本地模型
                var localUsers = new List<User>();
                ServerUserData? serverAdminUser = null;
                int conversionSuccessCount = 0;
                int conversionErrorCount = 0;

                foreach (var serverUser in usersData)
                {
                    try
                    {
                        var localUser = ConvertServerUserToLocalUser(serverUser);
                        if (localUser != null && localUser.Id > 0)
                        {
                            // 检查是否为admin用户
                            if (localUser.Name?.ToLower() == "admin")
                            {
                                serverAdminUser = serverUser;
                                _logger.LogInformation("发现服务器admin用户，将用于更新本地admin用户");
                            }
                            else
                            {
                                localUsers.Add(localUser);
                            }
                            conversionSuccessCount++;
                            _logger.LogDebug("成功转换用户: {UserId} - {UserName}", localUser.Id, localUser.Name);
                        }
                        else
                        {
                            conversionErrorCount++;
                            _logger.LogWarning("用户转换失败，ID为空或转换结果为null: {ServerUser}",
                                JsonSerializer.Serialize(serverUser));
                        }
                    }
                    catch (Exception ex)
                    {
                        conversionErrorCount++;
                        _logger.LogError(ex, "转换用户数据时发生异常: {ServerUser}",
                            JsonSerializer.Serialize(serverUser));
                    }
                }

                _logger.LogInformation("用户数据转换完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项",
                    conversionSuccessCount, conversionErrorCount);

                // 执行数据覆盖操作（保留admin账户）
                bool syncResult = await PerformUsersDataOverwriteAsync(localUsers, serverAdminUser);

                return syncResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理用户数据失败");
                return false;
            }
        }
        #endregion

        #region 处理锁柜数据
        /// <summary>
        /// 处理锁柜数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<bool> ProcessLockersDataAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始处理锁柜数据");

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("锁柜数据为空");
                    return false;
                }

                ServerLockerResponse lockerResponse = null;
                ServerLockerData[]? lockersData = null;

                try
                {
                    lockerResponse = JsonSerializer.Deserialize<ServerLockerResponse>(data, _jsonOptions);
                    if (lockerResponse == null)
                    {
                        _logger.LogWarning("锁柜数据解析失败，尝试直接解析数组格式");
                        // 尝试直接解析数组格式
                        lockersData = JsonSerializer.Deserialize<ServerLockerData[]>(data, _jsonOptions);
                        if (lockersData == null)
                        {
                            _logger.LogError("锁柜数据两种解析方式都失败");
                            return false;
                        }
                    }
                    else
                    {
                        lockersData = lockerResponse.Data;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "锁柜数据JSON格式错误");
                    return false;
                }

                if (lockersData == null || lockersData.Length == 0)
                {
                    _logger.LogWarning("锁柜数据为空数组");
                    return true; // 空数据也视为成功
                }

                _logger.LogInformation("收到 {LockerCount} 个锁柜数据", lockersData.Length);

                // 转换服务器数据到本地模型
                var localLockers = new List<Locker>();
                int conversionSuccessCount = 0;
                int conversionErrorCount = 0;

                foreach (var serverLocker in lockersData)
                {
                    try
                    {
                        var localLocker = ConvertServerLockerToLocalLocker(serverLocker);
                        if (localLocker != null && localLocker.LockerId > 0)
                        {
                            localLockers.Add(localLocker);
                            conversionSuccessCount++;
                            _logger.LogDebug("成功转换锁柜: {LockerId} - {LockerName}",
                                localLocker.LockerId, localLocker.LockerName);
                        }
                        else
                        {
                            conversionErrorCount++;
                            _logger.LogWarning("锁柜转换失败，ID为空或转换结果为null: {ServerLocker}",
                                JsonSerializer.Serialize(serverLocker));
                        }
                    }
                    catch (Exception ex)
                    {
                        conversionErrorCount++;
                        _logger.LogError(ex, "转换锁柜数据时发生异常: {ServerLocker}",
                            JsonSerializer.Serialize(serverLocker));
                    }
                }

                _logger.LogInformation("锁柜数据转换完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项",
                    conversionSuccessCount, conversionErrorCount);

                if (localLockers.Count == 0)
                {
                    _logger.LogError("所有锁柜数据转换失败，无法继续同步");
                    return false;
                }

                // 执行数据覆盖操作
                return await PerformDataOverwriteAsync(
                    "锁柜",
                    DeleteAllLockersAsync,
                    SaveLockerToDatabaseAsync,
                    localLockers
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理锁柜数据失败");
                return false;
            }
        }

        #endregion

        #region 处理用户锁柜关联数据
        /// <summary>
        /// 处理用户锁柜关联数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<bool> ProcessUserLockersDataAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始处理用户锁柜关联数据");

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("用户锁柜关联数据为空");
                    return false;
                }

                ServerUserLockerResponse serverUserLockerResponse = null;
                ServerUserLockerData[] userLockersData = null;

                try
                {
                    serverUserLockerResponse = JsonSerializer.Deserialize<ServerUserLockerResponse>(data, _jsonOptions);
                    if (serverUserLockerResponse == null)
                    {
                        _logger.LogWarning("用户锁柜关联数据解析失败，尝试直接解析数组格式");
                        // 尝试直接解析数组格式
                        userLockersData = JsonSerializer.Deserialize<ServerUserLockerData[]>(data, _jsonOptions);
                        if (userLockersData == null)
                        {
                            _logger.LogError("用户锁柜关联数据两种解析方式都失败");
                            return false;
                        }
                    }
                    else
                    {
                        userLockersData = serverUserLockerResponse.Data.ToArray();
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "用户锁柜关联数据JSON格式错误");
                    return false;
                }

                if (userLockersData == null || userLockersData.Length == 0)
                {
                    _logger.LogWarning("用户锁柜关联数据为空数组");
                    return true; // 空数据也视为成功
                }

                _logger.LogInformation("收到 {UserLockerCount} 个用户锁柜关联数据", userLockersData.Length);

                // 转换服务器数据到本地模型
                var localUserLockers = new List<UserLocker>();
                int conversionSuccessCount = 0;
                int conversionErrorCount = 0;

                foreach (var serverUserLocker in userLockersData)
                {
                    try
                    {
                        var localUserLocker = ConvertServerUserLockerToLocalUserLocker(serverUserLocker);
                        if (localUserLocker != null && localUserLocker.UserId > 0 && localUserLocker.LockerId > 0)
                        {
                            localUserLockers.Add(localUserLocker);
                            conversionSuccessCount++;
                            _logger.LogDebug("成功转换用户锁柜关联: UserId={UserId}, LockerId={LockerId}",
                                localUserLocker.UserId, localUserLocker.LockerId);
                        }
                        else
                        {
                            conversionErrorCount++;
                            _logger.LogWarning("用户锁柜关联转换失败，关键字段为空: {ServerUserLocker}",
                                JsonSerializer.Serialize(serverUserLocker));
                        }
                    }
                    catch (Exception ex)
                    {
                        conversionErrorCount++;
                        _logger.LogError(ex, "转换用户锁柜关联数据时发生异常: {ServerUserLocker}",
                            JsonSerializer.Serialize(serverUserLocker));
                    }
                }

                _logger.LogInformation("用户锁柜关联数据转换完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项",
                    conversionSuccessCount, conversionErrorCount);

                if (localUserLockers.Count == 0)
                {
                    _logger.LogError("所有用户锁柜关联数据转换失败，无法继续同步");
                    return false;
                }

                // 执行数据覆盖操作
                return await PerformDataOverwriteAsync(
                    "用户锁柜关联",
                    DeleteAllUserLockersAsync,
                    SaveUserLockerToDatabaseAsync,
                    localUserLockers
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理用户锁柜关联数据失败");
                return false;
            }
        }

        #endregion

        #region 执行角色数据覆盖操作
        /// <summary>
        /// 执行角色数据覆盖操作 - 确保超级管理员角色始终存在
        /// </summary>
        private async Task<bool> PerformRolesDataOverwriteAsync(List<Role> localRoles, ServerRoleData? serverSuperAdminRole)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("开始覆盖角色数据，共 {Count} 项，包含服务器超级管理员角色: {HasSuperAdmin}",
                    localRoles.Count, serverSuperAdminRole != null);

                // 步骤1: 删除除ID="1"外的所有角色
                _logger.LogInformation("正在删除除ID=1外的所有角色");
                bool deleteSuccess = await DeleteAllRolesExceptSuperAdminAsync();
                if (!deleteSuccess)
                {
                    _logger.LogError("删除非超级管理员角色失败");
                    await transaction.RollbackAsync();
                    return false;
                }
                _logger.LogInformation("成功删除除ID=1外的所有角色");

                // 步骤2: 添加新角色数据
                _logger.LogInformation("正在添加新的角色数据");
                int successCount = 0;
                int errorCount = 0;
                var errorItems = new List<string>();

                foreach (var role in localRoles)
                {
                    try
                    {
                        bool addSuccess = await SaveRoleToDatabaseAsync(role);
                        if (addSuccess)
                        {
                            successCount++;
                            _logger.LogDebug("成功添加角色数据项: {RoleName}", role.Name);
                        }
                        else
                        {
                            errorCount++;
                            errorItems.Add(role?.ToString() ?? "未知角色");
                            _logger.LogWarning("添加角色数据项失败: {Role}", role);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        errorItems.Add(role?.ToString() ?? "未知角色");
                        _logger.LogError(ex, "添加角色数据项时发生异常: {Role}", role);
                    }
                }

                // 步骤3: 确保超级管理员角色存在并更新
                _logger.LogInformation("开始确保超级管理员角色存在并更新");
                bool superAdminSuccess = await EnsureAndUpdateSuperAdminRoleAsync(serverSuperAdminRole);
                if (!superAdminSuccess)
                {
                    _logger.LogError("确保超级管理员角色存在失败");
                    await transaction.RollbackAsync();
                    return false;
                }

                if (errorCount > 0)
                {
                    _logger.LogWarning("角色数据覆盖部分失败：成功 {SuccessCount} 项，失败 {ErrorCount} 项。失败项: {ErrorItems}",
                        successCount, errorCount, string.Join("; ", errorItems));
                }

                // 提交事务
                await transaction.CommitAsync();

                _logger.LogInformation("角色数据覆盖完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项，超级管理员角色处理: {SuperAdminStatus}",
                    successCount, errorCount, superAdminSuccess ? "成功" : "失败");

                return errorCount == 0 && superAdminSuccess;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "覆盖角色数据时发生异常，已回滚事务");
                return false;
            }
        }
        #endregion

        #region 删除非ID="1"外的所有角色
        /// <summary>
        /// 删除非ID="1"外的所有角色
        /// </summary>
        private async Task<bool> DeleteAllRolesExceptSuperAdminAsync()
        {
            try
            {
                var rolesToDelete = _dbContext.Roles.Where(r => r.Id != 1).ToList();
                if (rolesToDelete.Count > 0)
                {
                    _dbContext.Roles.RemoveRange(rolesToDelete);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("删除 {Count} 个非超级管理员角色", rolesToDelete.Count);
                }
                else
                {
                    _logger.LogInformation("没有需要删除的非超级管理员角色");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除非超级管理员角色时发生异常");
                return false;
            }
        }
        #endregion

        #region 确保超级管理员角色存在
        /// <summary>
        /// 确保超级管理员角色存在（独立方法）
        /// </summary>
        private async Task<bool> EnsureSuperAdminRoleExistsAsync()
        {
            try
            {
                _logger.LogInformation("检查并确保超级管理员角色存在");

                var superAdminRole = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Id == 1);

                if (superAdminRole == null)
                {
                    _logger.LogInformation("创建默认超级管理员角色");
                    var defaultSuperAdmin = CreateDefaultSuperAdminRole();
                    await _dbContext.Roles.AddAsync(defaultSuperAdmin);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("默认超级管理员角色创建成功");
                }
                else
                {
                    _logger.LogInformation("超级管理员角色已存在: {RoleName}", superAdminRole.Name);

                    // 确保权限包含SUPER_ADMIN
                    EnsureSuperAdminPermission(superAdminRole);
                    _dbContext.Roles.Update(superAdminRole);
                    await _dbContext.SaveChangesAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确保超级管理员角色存在时发生异常");
                return false;
            }
        }
        #endregion

        #region 确保超级管理员角色存在并更新
        /// <summary>
        /// 确保超级管理员角色存在并更新
        /// </summary>
        private async Task<bool> EnsureAndUpdateSuperAdminRoleAsync(ServerRoleData? serverSuperAdminRole)
        {
            try
            {
                _logger.LogInformation("开始确保超级管理员角色存在并更新");

                // 查找本地超级管理员角色
                var localSuperAdmin = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == 1);

                if (localSuperAdmin == null)
                {
                    _logger.LogInformation("本地不存在超级管理员角色，创建默认超级管理员角色");
                    localSuperAdmin = CreateDefaultSuperAdminRole();
                    await _dbContext.Roles.AddAsync(localSuperAdmin);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation("找到本地超级管理员角色: {RoleName}", localSuperAdmin.Name);
                }

                // 如果服务器提供了超级管理员角色数据，则更新本地角色
                if (serverSuperAdminRole != null)
                {
                    _logger.LogInformation("使用服务器超级管理员角色数据更新本地角色");
                    UpdateLocalSuperAdminFromServer(localSuperAdmin, serverSuperAdminRole);
                }

                // 确保权限包含SUPER_ADMIN
                EnsureSuperAdminPermission(localSuperAdmin);

                _dbContext.Roles.Update(localSuperAdmin);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("超级管理员角色确保完成: {RoleId} - {RoleName}", localSuperAdmin.Id, localSuperAdmin.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确保超级管理员角色存在并更新时发生异常");
                return false;
            }
        }
        #endregion

        #region 创建默认超级管理员角色
        /// <summary>
        /// 创建默认超级管理员角色
        /// </summary>
        private Role CreateDefaultSuperAdminRole()
        {
            _logger.LogInformation("创建默认超级管理员角色");
            return new Role
            {
                Id = 1,
                Name = "超级管理员",
                Description = "拥有所有系统权限",
                Permissions = Role.GetAllPermissions(),
                IsBuiltIn = true,
                IsFromServer = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
        #endregion

        #region 从服务器数据更新本地超级管理员角色
        /// <summary>
        /// 从服务器数据更新本地超级管理员角色
        /// </summary>
        private void UpdateLocalSuperAdminFromServer(Role localSuperAdmin, ServerRoleData serverSuperAdminRole)
        {
            try
            {
                _logger.LogInformation("开始从服务器数据更新本地超级管理员角色");

                // 更新角色名称（如果服务器提供了）
                if (!string.IsNullOrWhiteSpace(serverSuperAdminRole.Name))
                {
                    localSuperAdmin.Name = serverSuperAdminRole.Name;
                    _logger.LogDebug("更新超级管理员角色名称: {RoleName}", serverSuperAdminRole.Name);
                }

                // 更新角色描述（如果服务器提供了）
                if (!string.IsNullOrWhiteSpace(serverSuperAdminRole.Description))
                {
                    localSuperAdmin.Description = serverSuperAdminRole.Description;
                    _logger.LogDebug("更新超级管理员角色描述: {Description}", serverSuperAdminRole.Description);
                }

                // 更新权限（如果服务器提供了）
                if (!string.IsNullOrWhiteSpace(serverSuperAdminRole.Permissions))
                {
                    try
                    {
                        var serverPermissions = JsonSerializer.Deserialize<List<string>>(serverSuperAdminRole.Permissions) ?? new List<string>();
                        localSuperAdmin.Permissions = serverPermissions;
                        _logger.LogDebug("更新超级管理员角色权限，权限数量: {PermissionCount}", serverPermissions.Count);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "服务器超级管理员角色权限数据格式错误，使用默认权限");
                    }
                }

                localSuperAdmin.UpdatedAt = DateTime.Now;
                _logger.LogInformation("本地超级管理员角色更新完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从服务器数据更新本地超级管理员角色时发生异常");
                throw;
            }
        }
        #endregion

        #region 确保权限包含SUPER_ADMIN
        /// <summary>
        /// 确保权限包含SUPER_ADMIN
        /// </summary>
        private void EnsureSuperAdminPermission(Role superAdminRole)
        {
            try
            {
                _logger.LogInformation("确保超级管理员角色包含SUPER_ADMIN权限");

                if (superAdminRole.Permissions == null)
                {
                    superAdminRole.Permissions = new List<string>();
                }

                if (!superAdminRole.Permissions.Contains(Role.PermissionConstants.SUPER_ADMIN))
                {
                    superAdminRole.Permissions.Add(Role.PermissionConstants.SUPER_ADMIN);
                    superAdminRole.UpdatedAt = DateTime.Now;
                    _logger.LogInformation("为超级管理员角色添加SUPER_ADMIN权限");
                }
                else
                {
                    _logger.LogDebug("超级管理员角色已包含SUPER_ADMIN权限");
                }

                _logger.LogInformation("超级管理员角色权限确保完成，当前权限数量: {PermissionCount}", superAdminRole.Permissions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确保超级管理员权限时发生异常");
                throw;
            }
        }

        #endregion

        #region 执行用户数据覆盖操作
        /// <summary>
        /// 执行用户数据覆盖操作 - 确保admin用户始终存在
        /// </summary>
        private async Task<bool> PerformUsersDataOverwriteAsync(List<User> localUsers, ServerUserData? serverAdminUser)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("开始覆盖用户数据，共 {Count} 项，包含服务器admin用户: {HasAdminUser}",
                    localUsers.Count, serverAdminUser != null);

                // 步骤1: 删除除admin外的所有用户
                _logger.LogInformation("正在删除除admin外的所有用户");
                bool deleteSuccess = await DeleteAllUsersExceptAdminAsync();
                if (!deleteSuccess)
                {
                    _logger.LogError("删除非admin用户失败");
                    await transaction.RollbackAsync();
                    return false;
                }
                _logger.LogInformation("成功删除除admin外的所有用户");

                // 步骤2: 添加新用户数据
                _logger.LogInformation("正在添加新的用户数据");
                int successCount = 0;
                int errorCount = 0;
                var errorItems = new List<string>();

                foreach (var user in localUsers)
                {
                    try
                    {
                        bool addSuccess = await AddUserToDatabaseAsync(user);
                        if (addSuccess)
                        {
                            successCount++;
                            _logger.LogDebug("成功添加用户数据项: {UserName}", user.Name);
                        }
                        else
                        {
                            errorCount++;
                            errorItems.Add(user?.ToString() ?? "未知用户");
                            _logger.LogWarning("添加用户数据项失败: {User}", user);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        errorItems.Add(user?.ToString() ?? "未知用户");
                        _logger.LogError(ex, "添加用户数据项时发生异常: {User}", user);
                    }
                }

                // 步骤3: 确保admin用户存在并更新
                _logger.LogInformation("开始确保admin用户存在并更新");
                bool adminSuccess = await EnsureAndUpdateAdminUserAsync(serverAdminUser);
                if (!adminSuccess)
                {
                    _logger.LogError("确保admin用户存在失败");
                    await transaction.RollbackAsync();
                    return false;
                }

                if (errorCount > 0)
                {
                    _logger.LogWarning("用户数据覆盖部分失败：成功 {SuccessCount} 项，失败 {ErrorCount} 项。失败项: {ErrorItems}",
                        successCount, errorCount, string.Join("; ", errorItems));
                    await transaction.RollbackAsync();
                    return false;
                }

                // 提交事务
                await transaction.CommitAsync();

                _logger.LogInformation("用户数据覆盖完成：成功 {SuccessCount} 项，失败 {ErrorCount} 项，admin用户处理: {AdminStatus}",
                    successCount, errorCount, adminSuccess ? "成功" : "失败");

                return errorCount == 0 && adminSuccess;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "覆盖用户数据时发生异常，已回滚事务");
                return false;
            }
        }
        #endregion

        #region 删除除admin用户外的所有用户
        /// <summary>
        /// 删除除admin用户外的所有用户
        /// </summary>
        private async Task<bool> DeleteAllUsersExceptAdminAsync()
        {
            try
            {
                var usersToDelete = _dbContext.Users.Where(u => u.Name.ToLower() != "admin").ToList();
                if (usersToDelete.Count > 0)
                {
                    _dbContext.Users.RemoveRange(usersToDelete);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("删除 {Count} 个非admin用户", usersToDelete.Count);
                }
                else
                {
                    _logger.LogInformation("没有需要删除的非admin用户");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除非admin用户时发生异常");
                return false;
            }
        }
        #endregion

        #region 确保admin用户存在并更新
        /// <summary>
        /// 确保admin用户存在并更新
        /// </summary>
        private async Task<bool> EnsureAndUpdateAdminUserAsync(ServerUserData? serverAdminUser)
        {
            try
            {
                _logger.LogInformation("开始确保admin用户存在并更新");

                // 查找本地admin用户
                var localAdmin = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Name.ToLower() == "admin");

                if (localAdmin == null)
                {
                    _logger.LogInformation("本地不存在admin用户，创建默认admin用户");
                    localAdmin = CreateDefaultAdminUser();
                    await _dbContext.Users.AddAsync(localAdmin);
                }
                else
                {
                    _logger.LogInformation("找到本地admin用户，ID: {AdminId}", localAdmin.Id);
                }

                // 如果服务器提供了admin用户数据，则更新本地admin用户
                if (serverAdminUser != null)
                {
                    _logger.LogInformation("使用服务器admin用户数据更新本地admin用户");
                    UpdateLocalAdminFromServer(localAdmin, serverAdminUser);
                }

                // 确保admin用户的ID为"1"，并且状态为激活
                localAdmin.Id = 1;
                localAdmin.IsActive = true;
                localAdmin.UpdatedAt = DateTime.Now;

                _dbContext.Users.Update(localAdmin);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("admin用户确保完成: {AdminId} - {AdminName}", localAdmin.Id, localAdmin.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确保admin用户存在并更新时发生异常");
                return false;
            }
        }
        #endregion

        #region 从服务器数据更新本地admin用户
        /// <summary>
        /// 从服务器数据更新本地admin用户
        /// </summary>
        private void UpdateLocalAdminFromServer(User localAdmin, ServerUserData serverAdminUser)
        {
            try
            {
                _logger.LogInformation("开始从服务器数据更新本地admin用户");

                // 更新头像（如果服务器提供了）
                if (!string.IsNullOrWhiteSpace(serverAdminUser.Avatar))
                {
                    localAdmin.Avatar = serverAdminUser.Avatar;
                    _logger.LogDebug("更新admin用户头像");
                }

                // 更新角色ID（如果服务器提供了）
                if (serverAdminUser.RoleId > 0)
                {
                    localAdmin.RoleId = serverAdminUser.RoleId;
                    _logger.LogDebug("更新admin用户角色ID: {RoleId}", serverAdminUser.RoleId);
                }

                // 更新最后人脸更新时间（如果服务器提供了）
                if (serverAdminUser.LastFaceUpdate.HasValue && serverAdminUser.LastFaceUpdate.Value != default(DateTime))
                {
                    localAdmin.LastFaceUpdate = serverAdminUser.LastFaceUpdate;
                    _logger.LogDebug("更新admin用户最后人脸更新时间: {LastFaceUpdate}", serverAdminUser.LastFaceUpdate);
                }

                // 更新其他基本信息
                if (!string.IsNullOrWhiteSpace(serverAdminUser.UserNumber))
                {
                    localAdmin.UserNumber = serverAdminUser.UserNumber;
                }

                if (!string.IsNullOrWhiteSpace(serverAdminUser.Name))
                {
                    localAdmin.Name = serverAdminUser.Name;
                }

                localAdmin.UpdatedAt = DateTime.Now;
                _logger.LogInformation("本地admin用户更新完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从服务器数据更新本地admin用户时发生异常");
                throw;
            }
        }
        #endregion

        #region 创建默认admin用户
        /// <summary>
        /// 创建默认admin用户
        /// </summary>
        private User CreateDefaultAdminUser()
        {
            _logger.LogInformation("创建默认admin用户");
            var defaultUsers = User.CreateDefaultUsers();
            var adminUser = defaultUsers.FirstOrDefault(u => u.Name.ToLower() == "admin");

            if (adminUser == null)
            {
                _logger.LogWarning("默认用户列表中未找到admin用户，创建新的admin用户");
                adminUser = new User
                {
                    Id = 1,
                    Name = "admin",
                    UserNumber = "超级管理员",
                    Password = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92", // 123456 的 SHA256
                    Department = "系统管理部",
                    RoleId = 1,
                    AssignedLockers = [],
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    Remarks = "系统默认超级管理员账户，拥有所有权限"
                };
            }

            return adminUser;
        }
        #endregion

        #region 确保admin用户存在
        /// <summary>
        /// 确保admin用户存在（独立方法）
        /// </summary>
        private async Task<bool> EnsureAdminUserExistsAsync()
        {
            try
            {
                _logger.LogInformation("检查并确保admin用户存在");

                var adminUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Name.ToLower() == "admin");

                if (adminUser == null)
                {
                    _logger.LogInformation("创建默认admin用户");
                    var defaultAdmin = CreateDefaultAdminUser();
                    await _dbContext.Users.AddAsync(defaultAdmin);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("默认admin用户创建成功");
                }
                else
                {
                    _logger.LogInformation("admin用户已存在，ID: {AdminId}", adminUser.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确保admin用户存在时发生异常");
                return false;
            }
        }

        #endregion

        #region 更新Admin用户的密码
        public async Task<bool> UpdateAdminPasswordAsync(string newPassword)
        {
            var superAdmin = await _dbContext.Users.FindAsync("1");
            if (superAdmin == null)
            {
                _logger.LogError("无法找到ID为1的超级管理员用户");
                return false;
            }
            superAdmin.Password = newPassword;
            _dbContext.Users.Update(superAdmin);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("超级管理员密码已更新");
            return true;
        }
        #endregion

        #region 全量同步方法（服务器发起）

        public async Task<bool> SyncRolesAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始全量同步角色列表");
                return await ProcessDataAsync("roles", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量同步角色列表失败");
                return false;
            }
        }

        public async Task<bool> SyncUsersAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始全量同步用户列表");
                return await ProcessDataAsync("users", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量同步用户列表失败");
                return false;
            }
        }

        public async Task<bool> SyncLockersAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始全量同步锁柜列表");
                return await ProcessDataAsync("lockers", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量同步锁柜列表失败");
                return false;
            }
        }

        public async Task<bool> SyncUserLockersAsync(string data)
        {
            try
            {
                _logger.LogInformation("开始全量同步用户锁柜关联列表");
                return await ProcessDataAsync("userlockers", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量同步用户锁柜关联列表失败");
                return false;
            }
        }

        #endregion

        #region 创建或更新用户（服务器指令）
        /// <summary>
        /// 创建或更新用户（服务器指令）
        /// </summary>
        /// <param name="data">用户数据JSON</param>
        /// <returns>返回操作结果：(是否成功, 是否新用户, 用户ID)</returns>
        public async Task<(bool success, bool isNewUser, long userId)> CreateOrUpdateUserAsync(string data)
        {
            bool isNewUser = false;
            long userId = 0;
            bool hasAvatar = false;

            try
            {
                _logger.LogInformation("开始处理创建或更新用户指令");

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("创建或更新用户数据为空");
                    return (false, false, 0);
                }

                // 解析服务器消息
                using JsonDocument doc = JsonDocument.Parse(data);
                JsonElement root = doc.RootElement;

                // 获取data字段 - 应该是一个对象
                if (!root.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogError("创建或更新用户指令数据格式错误，data字段不是对象或不存在");
                    return (false, false, 0);
                }

                // 解析单个用户数据
                ServerUserData serverUser;
                try
                {
                    serverUser = JsonSerializer.Deserialize<ServerUserData>(dataElement.GetRawText(), _jsonOptions);
                    if (serverUser == null)
                    {
                        _logger.LogWarning("用户数据解析失败，结果为null");
                        return (false, false, 0);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "用户数据JSON格式错误");
                    return (false, false, 0);
                }

                if (serverUser.Id == 0)
                {
                    _logger.LogWarning("用户数据缺少ID字段，跳过处理");
                    return (false, false, 0);
                }

                userId = serverUser.Id;
                _logger.LogInformation("解析到用户数据: {UserName} (ID: {UserId})", serverUser.Name, userId);

                // 转换服务器数据到本地模型
                var localUser = ConvertServerUserToLocalUser(serverUser);
                if (localUser == null || localUser.Id == 0)
                {
                    _logger.LogWarning("用户转换失败，ID为空或转换结果为null: {ServerUser}",
                        JsonSerializer.Serialize(serverUser));
                    return (false, false, 0);
                }

                hasAvatar = !string.IsNullOrEmpty(localUser.Avatar);
                var existingUser = await _dbContext.Users.FindAsync(localUser.Id);

                if (existingUser == null)
                {
                    // 创建新用户
                    // 为新增用户设置默认密码
                    if (string.IsNullOrEmpty(localUser.Password))
                    {
                        localUser.Password = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92"; // 123456 的 SHA256
                    }

                    await _dbContext.Users.AddAsync(localUser);
                    isNewUser = true;
                    _logger.LogInformation("创建新用户: {UserId} - {UserName}", localUser.Id, localUser.Name);
                }
                else
                {
                    // 更新现有用户（保留原有密码和人脸特征数据）
                    var originalPassword = existingUser.Password;
                    var originalFaceFeatureData = existingUser.FaceFeatureData;
                    var originalFaceConfidence = existingUser.FaceConfidence;
                    var originalFaceFeatureVersion = existingUser.FaceFeatureVersion;
                    var originalLastFaceUpdate = existingUser.LastFaceUpdate;

                    // 更新除密码和人脸特征外的其他字段
                    existingUser.UserNumber = localUser.UserNumber;
                    existingUser.Name = localUser.Name;
                    existingUser.IdNumber = localUser.IdNumber;
                    existingUser.RoleId = localUser.RoleId;
                    existingUser.AssignedLockers = localUser.AssignedLockers;
                    existingUser.Avatar = localUser.Avatar;
                    existingUser.Department = localUser.Department;
                    existingUser.Remarks = localUser.Remarks;
                    existingUser.IsActive = localUser.IsActive;
                    existingUser.UpdatedAt = DateTime.Now;

                    // 保留原有密码
                    existingUser.Password = originalPassword;

                    // 如果服务器提供了新的人脸特征数据，则更新
                    if (serverUser.FaceConfidence > 0)
                    {
                        existingUser.FaceConfidence = localUser.FaceConfidence;
                        existingUser.FaceFeatureVersion = localUser.FaceFeatureVersion;
                        existingUser.LastFaceUpdate = localUser.LastFaceUpdate;
                        // 注意：这里无法直接设置FaceFeatureData，因为服务器不传输这个字段
                        // 但如果有头像，我们会在下面生成人脸特征
                    }

                    _dbContext.Users.Update(existingUser);
                    _logger.LogInformation("更新现有用户: {UserId} - {UserName}", localUser.Id, localUser.Name);
                }

                // 保存用户信息到数据库
                await _dbContext.SaveChangesAsync();

                // 无论新老用户，只要有头像就尝试生成人脸特征（异步进行，不影响主流程）
                if (hasAvatar)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _logger.LogInformation("开始为用户生成人脸特征: {UserId}", userId);

                            // 在独立作用域中生成人脸特征
                            using var scope = _serviceProvider.CreateScope();
                            var scopedServices = scope.ServiceProvider;
                            var dbContext = scopedServices.GetRequiredService<FaceLockerDbContext>();
                            var baiduFaceService = scopedServices.GetRequiredService<BaiduFaceService>();

                            var userForFace = await dbContext.Users.FindAsync(userId);
                            if (userForFace != null)
                            {
                                baiduFaceService.InitializeSDK();
                                bool result = await baiduFaceService.GenerateFaceFeatureFromAvatarAsync(userForFace);

                                if (result)
                                {
                                    _logger.LogInformation("成功为用户 {UserId} 生成人脸特征", userId);
                                }
                                else
                                {
                                    _logger.LogWarning("为用户 {UserId} 生成人脸特征失败", userId);
                                    // 这里可以记录失败，但不影响主流程
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "生成人脸特征失败，用户ID：{UserId}", userId);
                            // 捕获所有异常，确保不影响到主流程
                        }
                    });
                }

                return (true, isNewUser, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理创建或更新用户指令时发生异常");
                return (false, false, userId);
            }
        }
        #endregion

        #region 处理用户柜格分配指令（不发送响应）
        /// <summary>
        /// 处理用户柜格分配指令（不发送响应）
        /// </summary>
        /// <param name="data">分配数据JSON</param>
        /// <param name="sendResponse">是否发送响应</param>
        /// <returns>处理是否成功</returns>
        public async Task<bool> ProcessUserLockerAssignmentCommandAsync(string data, bool sendResponse = true)
        {
            try
            {
                _logger.LogInformation("开始处理用户柜格分配指令，发送响应：{SendResponse}", sendResponse);

                if (string.IsNullOrWhiteSpace(data))
                {
                    _logger.LogWarning("用户柜格分配数据为空");
                    return false;
                }

                // 解析服务器消息
                ServerUserLockerAssignmentRequest request = null;
                try
                {
                    request = JsonSerializer.Deserialize<ServerUserLockerAssignmentRequest>(data, _jsonOptions);
                    if (request == null)
                    {
                        _logger.LogError("用户柜格分配请求数据解析失败");
                        return false;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "用户柜格分配数据JSON格式错误");
                    return false;
                }

                if (request.Data == null)
                {
                    _logger.LogWarning("用户柜格分配数据为空");
                    return true; // 空数据也视为成功
                }

                var assignment = request.Data;

                var userId = assignment.UserId;
                var lockerId = assignment.LockerId;
                int type = assignment.Type;

                _logger.LogInformation("收到用户柜格分配指令: Type={Type}, UserId={UserId}, LockerId={LockerId}",
                    type, userId, lockerId);

                // 验证必要字段
                if (userId == 0 || lockerId == 0)
                {
                    _logger.LogWarning("用户柜格分配数据缺少UserId或LockerId字段: UserId={UserId}, LockerId={LockerId}",
                        userId, lockerId);
                    return false;
                }

                // 验证操作类型
                if (type < 0 || type > 1)
                {
                    _logger.LogWarning("无效的操作类型: {Type}，只能为0(绑定)或1(解绑)", type);
                    return false;
                }

                // 检查用户和柜格是否存在
                var userExists = await _dbContext.Users.AnyAsync(u => u.Id == userId);
                var lockerExists = await _dbContext.Lockers.AnyAsync(l => l.LockerId == lockerId);

                if (!userExists || !lockerExists)
                {
                    _logger.LogWarning("用户或柜格不存在: UserId={UserId} (Exists={UserExists}), LockerId={LockerId} (Exists={LockerExists})",
                        userId, userExists, lockerId, lockerExists);
                    return false;
                }

                bool success = false;
                string operationName = type == 0 ? "绑定" : "解绑";

                // 处理服务器发送的空时间值
                DateTime? assignedAt = null;
                if (assignment.AssignedAt.HasValue && assignment.AssignedAt.Value != DateTime.MinValue)
                {
                    assignedAt = assignment.AssignedAt.Value;
                }

                DateTime? expiresAt = null;
                if (assignment.ExpiresAt.HasValue && assignment.ExpiresAt.Value != DateTime.MinValue)
                {
                    expiresAt = assignment.ExpiresAt.Value;
                }

                // 根据操作类型执行绑定或解绑
                if (type == 0)
                {
                    // 绑定操作
                    success = await BindUserToLockerAsync(userId, lockerId, assignedAt, expiresAt, assignment.IsActive);
                    if (success)
                    {
                        _logger.LogInformation("成功绑定用户 {UserId} 到柜格 {LockerId}",
                            userId, lockerId);
                    }
                }
                else if (type == 1)
                {
                    // 解绑操作
                    success = await UnbindUserFromLockerAsync(userId, lockerId);
                    if (success)
                    {
                        _logger.LogInformation("成功解绑用户 {UserId} 从柜格 {LockerId}",
                            userId, lockerId);
                    }
                }

                if (success)
                {
                    // 更新用户的AssignedLockers列表
                    await UpdateUserAssignedLockersAsync(userId);
                }

                // 只有在指定发送响应时才发送
                if (sendResponse)
                {
                    // 发送响应，包含与请求相同的type值
                    var responseData = new
                    {
                        status = success ? "success" : "error",
                        type = type,
                        message = success ? $"用户与柜格{operationName}完成" : $"用户与柜格{operationName}失败",
                        added = success ? (type == 0 ? 1 : 0) : 0,
                        updated = 0,
                        deleted = success ? (type == 1 ? 1 : 0) : 0,
                        syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };

                    await _networkService.SendProtocolMessageAsync("user_locker_assignment_response", responseData);
                }

                _logger.LogInformation("用户柜格分配指令处理完成：{Success}", success ? "成功" : "失败");
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理用户柜格分配指令失败");

                // 只有在指定发送响应时才发送错误响应
                if (sendResponse)
                {
                    // 发送错误响应，包含type值（如果可能的话）
                    try
                    {
                        // 尝试从错误数据中解析type
                        int errorType = 0;
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(data);
                            if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) &&
                                dataElement.TryGetProperty("type", out JsonElement typeElement))
                            {
                                errorType = typeElement.GetInt32();
                            }
                        }
                        catch
                        {
                            // 如果无法解析，使用默认值0
                            errorType = 0;
                        }

                        var errorResponse = new
                        {
                            status = "error",
                            type = errorType,
                            message = $"处理用户柜格分配指令时发生异常：{ex.Message}",
                            added = 0,
                            updated = 0,
                            deleted = 0,
                            syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff")
                        };

                        await _networkService.SendProtocolMessageAsync("user_locker_assignment_response", errorResponse);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "发送错误响应时发生异常");
                    }
                }

                return false;
            }
        }
        #endregion

        #region 绑定用户到柜格
        /// <summary>
        /// 绑定用户到柜格
        /// </summary>
        private async Task<bool> BindUserToLockerAsync(long userId, long lockerId, DateTime? assignedAt, DateTime? expiresAt, bool isActive)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 生成唯一的UserLockerId
                string userLockerId = $"{userId}_{lockerId}";

                // 检查是否已存在绑定关系
                var existingAssignment = await _dbContext.UserLockers.FindAsync(userLockerId);

                if (existingAssignment != null)
                {
                    // 已存在绑定关系，更新它
                    existingAssignment.AssignedAt = assignedAt ?? DateTime.Now;
                    existingAssignment.ExpiresAt = expiresAt;
                    existingAssignment.IsActive = isActive;
                    existingAssignment.StoredTime = DateTime.Now;

                    _dbContext.UserLockers.Update(existingAssignment);
                    _logger.LogDebug("更新已有的用户柜格绑定关系: {UserLockerId}", userLockerId);
                }
                else
                {
                    // 创建新的绑定关系
                    var userLocker = new UserLocker
                    {
                        UserId = userId,
                        LockerId = lockerId,
                        AssignmentStatus = AssignmentStatus.Pending,
                        StorageStatus = StorageStatus.Unused,
                        AssignedAt = assignedAt ?? DateTime.Now,
                        ExpiresAt = expiresAt,
                        IsActive = isActive,
                        CreatedAt = DateTime.Now,
                        StoredTime = DateTime.Now
                    };

                    await _dbContext.UserLockers.AddAsync(userLocker);
                    _logger.LogDebug("创建新的用户柜格绑定关系: {UserLockerId}", userLockerId);
                }

                // 更新柜格状态为已分配
                var assignedLocker = await _dbContext.Lockers.FindAsync(lockerId);
                if (assignedLocker != null)
                {
                    assignedLocker.Status = LockerStatus.ScreenOccupied;
                    assignedLocker.UpdatedAt = DateTime.Now;
                    _dbContext.Lockers.Update(assignedLocker);
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "绑定用户到柜格失败: UserId={UserId}, LockerId={LockerId}",
                    userId, lockerId);
                return false;
            }
        }
        #endregion

        #region 解绑用户从柜格
        /// <summary>
        /// 解绑用户从柜格
        /// </summary>
        private async Task<bool> UnbindUserFromLockerAsync(long userId, long lockerId)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 查找绑定关系
                string userLockerId = $"{userId}_{lockerId}";
                var existingAssignment = await _dbContext.UserLockers.FindAsync(userLockerId);

                // 提前获取柜格对象，避免重复声明
                var lockerToUpdate = await _dbContext.Lockers.FindAsync(lockerId);

                if (existingAssignment == null)
                {
                    // 如果绑定关系不存在，视为成功（可能已经解绑了）
                    _logger.LogWarning("用户柜格绑定关系不存在，可能已经解绑: {UserLockerId}", userLockerId);

                    // 但需要确保柜格状态正确
                    if (lockerToUpdate != null && lockerToUpdate.Status != LockerStatus.Available)
                    {
                        lockerToUpdate.Status = LockerStatus.Available;
                        lockerToUpdate.UpdatedAt = DateTime.Now;
                        _dbContext.Lockers.Update(lockerToUpdate);
                        await _dbContext.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                    return true;
                }

                // 删除绑定关系
                _dbContext.UserLockers.Remove(existingAssignment);

                // 更新柜格状态为可用
                if (lockerToUpdate != null)
                {
                    lockerToUpdate.Status = LockerStatus.Available;
                    lockerToUpdate.UpdatedAt = DateTime.Now;
                    _dbContext.Lockers.Update(lockerToUpdate);
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogDebug("成功解绑用户柜格关系: {UserLockerId}", userLockerId);
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "解绑用户从柜格失败: UserId={UserId}, LockerId={LockerId}",
                    userId, lockerId);
                return false;
            }
        }
        #endregion

        #region 更新单个用户的AssignedLockers列表
        /// <summary>
        /// 更新单个用户的AssignedLockers列表
        /// </summary>
        private async Task UpdateUserAssignedLockersAsync(long userId)
        {
            try
            {
                _logger.LogInformation("开始更新用户 {UserId} 的AssignedLockers列表", userId);

                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("用户 {UserId} 不存在，无法更新AssignedLockers列表", userId);
                    return;
                }

                // 获取用户当前的所有有效绑定关系
                var userLockers = await _dbContext.UserLockers
                    .Where(ul => ul.UserId == userId && ul.IsActive)
                    .ToListAsync();

                // 更新用户的AssignedLockers列表
                var lockerIds = userLockers
                    .Select(ul => ul.LockerId)
                    .Distinct()
                    .ToList();

                user.AssignedLockers = lockerIds;
                user.UpdatedAt = DateTime.Now;

                _dbContext.Users.Update(user);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("更新用户 {UserId} 的AssignedLockers列表完成，共 {Count} 个柜格",
                    userId, lockerIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户 {UserId} 的AssignedLockers列表失败", userId);
            }
        }
        #endregion

        #region 处理服务器数据响应

        /// <summary>
        /// 处理服务器数据响应
        /// </summary>
        /// <param name="dataType">数据类型</param>
        /// <param name="data">数据内容</param>
        /// <param name="requestId">请求ID</param>
        /// <returns>处理结果</returns>
        private async Task<bool> HandleDataResponseAsync(string dataType, string data, string? requestId = null)
        {
            try
            {
                _logger.LogInformation("开始处理服务器 {DataType} 数据响应", dataType);

                // 处理数据
                bool processSuccess = await ProcessDataAsync(dataType, data);

                // 发送同步完成确认
                if (processSuccess)
                {
                    // 构建完整的同步响应数据
                    var syncResponseData = new
                    {
                        status = "success",
                        message = GetSyncSuccessMessage(dataType),
                        syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        added = 0,    // 可以根据实际同步情况统计数量
                        updated = 0,  // 可以根据实际同步情况统计数量  
                        deleted = 0   // 可以根据实际同步情况统计数量
                    };

                    // 获取对应的反馈消息类型
                    string feedbackMessageType = GetSyncFeedbackMessageType(dataType);

                    if (!string.IsNullOrEmpty(feedbackMessageType))
                    {
                        await _networkService.SendProtocolMessageAsync(feedbackMessageType, syncResponseData);
                        _logger.LogInformation("已发送 {DataType} 完整同步完成确认，消息类型：{MessageType}", dataType, feedbackMessageType);
                    }
                    else
                    {
                        _logger.LogWarning("未知的数据类型：{DataType}，无法发送同步反馈", dataType);
                    }
                }
                else
                {
                    _logger.LogError("{DataType} 数据处理失败，不发送同步完成确认", dataType);

                    // 处理失败时也发送错误响应
                    var errorResponseData = new
                    {
                        status = "error",
                        message = $"{GetDataTypeDisplayName(dataType)}同步失败",
                        syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        added = 0,
                        updated = 0,
                        deleted = 0
                    };

                    string feedbackMessageType = GetSyncFeedbackMessageType(dataType);
                    if (!string.IsNullOrEmpty(feedbackMessageType))
                    {
                        await _networkService.SendProtocolMessageAsync(feedbackMessageType, errorResponseData); _logger.LogInformation("已发送 {DataType} 同步失败确认", dataType);
                    }
                }

                return processSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理服务器 {DataType} 数据响应时发生异常", dataType);
                return false;
            }
        }
        #endregion

        #region 根据数据类型获取同步成功消息
        /// <summary>
        /// 根据数据类型获取同步成功消息
        /// </summary>
        private string GetSyncSuccessMessage(string dataType)
        {
            return dataType.ToLower() switch
            {
                "roles" => "角色列表同步完成",
                "users" => "用户列表同步完成",
                "lockers" => "锁柜列表同步完成",
                "userlockers" => "用户锁柜分配同步完成",
                "boards" => "锁控板数据同步完成",
                "createandupdate_user" => "用户信息同步完成",
                "user_locker_assignment" => "用户与柜格分配完成",
                _ => $"{dataType}数据同步完成"
            };
        }
        #endregion

        #region 根据数据类型获取显示名称
        /// <summary>
        /// 根据数据类型获取显示名称
        /// </summary>
        private string GetDataTypeDisplayName(string dataType)
        {
            return dataType.ToLower() switch
            {
                "roles" => "角色列表",
                "users" => "用户列表",
                "lockers" => "锁柜列表",
                "userlockers" => "用户锁柜分配",
                "boards" => "锁控板数据",
                "createandupdate_user" => "用户信息",
                "user_locker_assignment" => "用户柜格分配",
                _ => dataType
            };
        }
        #endregion

        #region 根据数据类型获取同步反馈消息类型
        /// <summary>
        /// 根据数据类型获取同步反馈消息类型
        /// </summary>
        private string GetSyncFeedbackMessageType(string dataType)
        {
            return dataType.ToLower() switch
            {
                "roles" => "sync_roles_response",
                "users" => "sync_users_response",
                "lockers" => "sync_lockers_response",
                "userlockers" => "sync_user_lockers_response",
                "boards" => "sync_boards_response",
                "createandupdate_user" => "createAndUpdate_user_response",
                "user_locker_assignment" => "user_locker_assignment_response",
                _ => $"sync_{dataType}_response"
            };
        }
        #endregion

        #region 同步访问日志到服务器
        /// <summary>
        /// 同步访问日志到服务器
        /// </summary>
        /// <returns>同步是否成功</returns>
        public async Task<bool> SyncAccessLogsAsync()
        {
            _logger.LogInformation("开始同步访问日志");

            // 检查网络连接状态
            if (!_networkService.IsConnected)
            {
                _logger.LogWarning("网络未连接，无法同步访问日志");
                return false;
            }

            List<AccessLog> unsyncedLogs = [];
            try
            {
                // 获取未上传的访问日志
                unsyncedLogs = _dbContext.AccessLogs.Where(log => !log.IsUploaded).Take(100).ToList();
                _logger.LogInformation("查询到 {LogCount} 条未同步的访问日志", unsyncedLogs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询未上传访问日志时发生异常");
                return false;
            }

            if (unsyncedLogs.Count == 0)
            {
                _logger.LogInformation("没有需要同步的访问日志");
                return true;
            }

            _logger.LogInformation("找到 {LogCount} 条未同步的访问日志", unsyncedLogs.Count);

            // 构建上传数据
            var uploadData = new
            {
                lastUploadTime = _lastSyncTime?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                logs = unsyncedLogs.Select(log => new
                {
                    id = log.Id,
                    userId = log.UserId,
                    userName = log.UserName,
                    lockerId = log.LockerId,
                    lockerName = log.LockerName,
                    action = log.Action,
                    result = log.Result,
                    details = log.Details,
                    timestamp = log.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }).ToArray()
            };

            _logger.LogInformation("构建访问日志上传请求消息");

            // 构建上传请求消息
            var message = new
            {
                version = "1.0",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                deviceName = _configuration["Server:DeviceName"] ?? "UnknownDevice",
                messageType = "upload_access_logs",
                data = uploadData
            };

            _logger.LogInformation("序列化访问日志上传消息");

            // 序列化消息
            string jsonData;
            try
            {
                jsonData = JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "序列化访问日志上传消息失败");
                return false;
            }

            _logger.LogInformation("发送访问日志上传请求到服务器");

            // 发送上传请求
            bool sendSuccess = await _networkService.SendDataAsync(jsonData);
            if (sendSuccess)
            {
                try
                {
                    // 标记日志为已上传
                    foreach (var log in unsyncedLogs)
                    {
                        log.IsUploaded = true;
                    }
                    await _dbContext.SaveChangesAsync();

                    _lastSyncTime = DateTime.UtcNow;
                    _logger.LogInformation("访问日志同步完成，成功上传 {LogCount} 条日志", unsyncedLogs.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新访问日志上传状态失败");
                    return false;
                }
            }
            else
            {
                _logger.LogWarning("访问日志上传失败");
                return false;
            }
        }

        #endregion

        #region 根据锁柜关联id获取用户的锁柜

        public async Task<Locker> GetUserLocker(long lockerId)
        {
            try
            {
                _logger.LogDebug("根据锁柜ID获取锁柜信息: {LockerId}", lockerId);

                var locker = await _dbContext.Lockers.FindAsync(lockerId);
                if (locker != null)
                {
                    _logger.LogDebug("成功找到锁柜: {LockerId} - {LockerName}", locker.LockerId, locker.LockerName);
                    return locker;
                }
                else
                {
                    _logger.LogWarning("未找到锁柜: {LockerId}", lockerId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取锁柜信息时发生异常: {LockerId}", lockerId);
                return null;
            }
        }
        #endregion

        #region 私有方法 - 数据转换和数据库操作

        private Role ConvertServerRoleToLocalRole(ServerRoleData serverRole)
        {
            if (serverRole == null)
            {
                _logger.LogWarning("服务器角色数据为空");
                return null;
            }

            // 验证必要字段
            if (serverRole.Id == 0)
            {
                _logger.LogWarning("服务器角色数据缺少ID字段");
                return null;
            }

            List<string> permissions = new List<string>();
            if (!string.IsNullOrWhiteSpace(serverRole.Permissions))
            {
                try
                {
                    permissions = JsonSerializer.Deserialize<List<string>>(serverRole.Permissions) ?? new List<string>();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "角色权限数据格式错误，角色ID：{RoleId}，权限数据：{Permissions}",
                        serverRole.Id, serverRole.Permissions);
                    permissions = new List<string>();
                }
            }

            // 处理DateTime字段 - 使用默认值检查而不是??操作符
            DateTime createdAt = GetSafeDateTime(serverRole.CreatedAt, "角色创建时间", serverRole.Id);
            DateTime updatedAt = GetSafeDateTime(serverRole.UpdatedAt, "角色更新时间", serverRole.Id);

            return new Role
            {
                Name = serverRole.Name ?? "未知角色",
                Description = serverRole.Description ?? "",
                Permissions = permissions,
                IsFromServer = true,
                IsBuiltIn = false,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        private User ConvertServerUserToLocalUser(ServerUserData serverUser)
        {
            if (serverUser == null)
            {
                _logger.LogWarning("服务器用户数据为空");
                return null;
            }

            // 验证必要字段
            if (serverUser.Id == 0)
            {
                _logger.LogWarning("服务器用户数据缺少ID字段");
                return null;
            }

            // 处理DateTime字段
            DateTime createdAt = GetSafeDateTime(serverUser.CreatedAt, "用户创建时间", serverUser.Id);
            DateTime updatedAt = GetSafeDateTime(serverUser.UpdatedAt, "用户更新时间", serverUser.Id);
            DateTime? lastFaceUpdate = serverUser.LastFaceUpdate.HasValue && serverUser.LastFaceUpdate.Value != default(DateTime) ? serverUser.LastFaceUpdate.Value : null;

            // 处理值类型字段
            float faceConfidence = GetSafeFloat(serverUser.FaceConfidence, "人脸置信度", serverUser.Id, 0.0f);
            int faceFeatureVersion = GetSafeInt(serverUser.FaceFeatureVersion, "人脸特征版本", serverUser.Id, 1);
            bool isActive = serverUser.IsActive;

            return new User
            {
                UserNumber = serverUser.UserNumber ?? "",
                Name = serverUser.Name ?? "未知用户",
                IdNumber = serverUser.IdNumber ?? "",
                RoleId = serverUser.RoleId,
                AssignedLockers = serverUser.AssignedLockers ?? [],
                Avatar = serverUser.Avatar,
                FaceConfidence = faceConfidence,
                FaceFeatureVersion = faceFeatureVersion,
                LastFaceUpdate = lastFaceUpdate,
                IsActive = isActive,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                Department = serverUser.Department ?? "",
                Remarks = serverUser.Remarks ?? ""
            };
        }

        private Locker ConvertServerLockerToLocalLocker(ServerLockerData serverLocker)
        {
            if (serverLocker == null)
            {
                _logger.LogWarning("服务器锁柜数据为空");
                return null;
            }

            // 验证必要字段
            if (serverLocker.LockerId == 0)
            {
                _logger.LogWarning("服务器锁柜数据缺少LockerId字段");
                return null;
            }

            // 处理DateTime字段
            DateTime createdAt = GetSafeDateTime(serverLocker.CreatedAt, "锁柜创建时间", serverLocker.LockerId);
            DateTime updatedAt = GetSafeDateTime(serverLocker.UpdatedAt, "锁柜更新时间", serverLocker.LockerId);
            DateTime? lastOpened = serverLocker.LastOpened.HasValue && serverLocker.LastOpened.Value != default(DateTime)
                ? serverLocker.LastOpened.Value
                : null;

            // 处理值类型字段
            int boardAddress = GetSafeInt(serverLocker.BoardAddress, "板地址", serverLocker.LockerId, 0);
            int channelNumber = GetSafeInt(serverLocker.ChannelNumber, "通道号", serverLocker.LockerId, 0);
            LockerStatus status = GetSafeLockerStatus(serverLocker.Status, "锁柜状态", serverLocker.LockerId);
            bool isOpened = serverLocker.IsOpened; // 直接使用服务器值
            bool isAvailable = serverLocker.IsAvailable; // 直接使用服务器值

            return new Locker
            {
                LockerName = serverLocker.LockerName ?? "未知锁柜",
                LockerNumber = serverLocker.LockerNumber ?? "",
                BoardAddress = boardAddress,
                ChannelNumber = channelNumber,
                Status = status,
                IsOpened = isOpened,
                LastOpened = lastOpened,
                IsAvailable = isAvailable,
                Location = serverLocker.Location ?? "",
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        private UserLocker ConvertServerUserLockerToLocalUserLocker(ServerUserLockerData serverUserLocker)
        {
            if (serverUserLocker == null)
            {
                _logger.LogWarning("服务器用户锁柜关联数据为空");
                return null;
            }

            // 验证必要字段
            if (serverUserLocker.UserId == 0 || serverUserLocker.LockerId == 0)
            {
                _logger.LogWarning("服务器用户锁柜关联数据缺少UserId或LockerId字段: UserId={UserId}, LockerId={LockerId}",
                    serverUserLocker.UserId, serverUserLocker.LockerId);
                return null;
            }

            // 处理DateTime字段
            DateTime assignedAt = GetSafeDateTime(serverUserLocker.AssignedAt, "用户锁柜分配时间", serverUserLocker.Id);
            DateTime createdAt = GetSafeDateTime(serverUserLocker.CreatedAt, "用户锁柜创建时间", serverUserLocker.Id);
            DateTime updatedAt = GetSafeDateTime(serverUserLocker.UpdatedAt, "用户锁柜更新时间", serverUserLocker.Id);
            DateTime? expiresAt = serverUserLocker.ExpiresAt.HasValue && serverUserLocker.ExpiresAt.Value != default(DateTime)
                ? serverUserLocker.ExpiresAt.Value
                : null;

            // 处理值类型字段
            bool isActive = serverUserLocker.IsActive; // 直接使用服务器值

            return new UserLocker
            {
                UserId = serverUserLocker.UserId,
                LockerId = serverUserLocker.LockerId,
                StorageStatus = StorageStatus.Unused,
                AssignmentStatus = AssignmentStatus.Pending,
                AssignedAt = assignedAt,
                ExpiresAt = expiresAt,
                IsActive = isActive,
                CreatedAt = createdAt,
                StoredTime = updatedAt
            };
        }

        /// <summary>
        /// 安全获取DateTime值，如果为默认值则使用当前时间
        /// </summary>
        private DateTime GetSafeDateTime(DateTime dateTime, string fieldName, long entityId)
        {
            if (dateTime == default)
            {
                return DateTime.Now;
            }
            return dateTime;
        }

        /// <summary>
        /// 安全获取float值，如果为默认值则使用指定默认值
        /// </summary>
        private float GetSafeFloat(float value, string fieldName, long entityId, float defaultValue)
        {
            if (value == default)
            {
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// 安全获取int值，如果为默认值则使用指定默认值
        /// </summary>
        private int GetSafeInt(int value, string fieldName, long entityId, int defaultValue)
        {
            if (value == default)
            {
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// 安全获取锁柜状态值
        /// </summary>
        private LockerStatus GetSafeLockerStatus(int statusValue, string fieldName, long entityId)
        {
            if (Enum.IsDefined(typeof(LockerStatus), statusValue))
            {
                return (LockerStatus)statusValue;
            }
            else
            {
                return LockerStatus.Available;
            }
        }

        #region 保存角色数据到本地数据库
        /// <summary>
        /// 保存角色数据到本地数据库
        /// </summary>
        /// <param name="role"></param>
        /// <returns></returns>
        private async Task<bool> SaveRoleToDatabaseAsync(Role role)
        {
            try
            {
                if (role == null)
                {
                    _logger.LogWarning("保存角色到数据库失败：角色对象为空");
                    return false;
                }

                var existingRole = await _dbContext.Roles.FindAsync(role.Id);
                if (existingRole != null)
                {
                    _dbContext.Entry(existingRole).CurrentValues.SetValues(role);
                    _logger.LogDebug("更新现有角色：{RoleName} (ID: {RoleId})", role.Name, role.Id);
                }
                else
                {
                    await _dbContext.Roles.AddAsync(role);
                    _logger.LogDebug("添加新角色：{RoleName} (ID: {RoleId})", role.Name, role.Id);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogDebug("成功保存角色到数据库：{RoleId}", role.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存角色到数据库失败，角色ID：{RoleId}", role?.Id);
                return false;
            }
        }
        #endregion

        private async Task<bool> AddUserToDatabaseAsync(User user)
        {
            try
            {
                if (user == null)
                {
                    _logger.LogWarning("保存用户到数据库失败：用户对象为空");
                    return false;
                }

                var existingUser = await _dbContext.Users.FindAsync(user.Id);
                if (existingUser != null)
                {
                    _dbContext.Entry(existingUser).CurrentValues.SetValues(user);
                    _logger.LogDebug("更新现有用户：{UserName} (ID: {UserId})", user.Name, user.Id);
                }
                else
                {
                    await _dbContext.Users.AddAsync(user);
                    _logger.LogDebug("添加新用户：{UserName} (ID: {UserId})", user.Name, user.Id);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogDebug("成功保存用户到数据库：{UserId}", user.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存用户到数据库失败，用户ID：{UserId}", user?.Id);
                return false;
            }
        }

        private async Task<bool> SaveLockerToDatabaseAsync(Locker locker)
        {
            try
            {
                if (locker == null)
                {
                    _logger.LogWarning("保存锁柜到数据库失败：锁柜对象为空");
                    return false;
                }

                var existingLocker = await _dbContext.Lockers.FindAsync(locker.LockerId);
                if (existingLocker != null)
                {
                    _dbContext.Entry(existingLocker).CurrentValues.SetValues(locker);
                    _logger.LogDebug("更新现有锁柜：{LockerName} (ID: {LockerId})", locker.LockerName, locker.LockerId);
                }
                else
                {
                    await _dbContext.Lockers.AddAsync(locker);
                    _logger.LogDebug("添加新锁柜：{LockerName} (ID: {LockerId})", locker.LockerName, locker.LockerId);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogDebug("成功保存锁柜到数据库：{LockerId}", locker.LockerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存锁柜到数据库失败，锁柜ID：{LockerId}", locker?.LockerId);
                return false;
            }
        }

        private async Task<bool> SaveUserLockerToDatabaseAsync(UserLocker userLocker)
        {
            try
            {
                if (userLocker == null)
                {
                    _logger.LogWarning("保存用户锁柜关联到数据库失败：关联对象为空");
                    return false;
                }

                var existingUserLocker = await _dbContext.UserLockers.FindAsync(userLocker.UserLockerId);
                if (existingUserLocker != null)
                {
                    _dbContext.Entry(existingUserLocker).CurrentValues.SetValues(userLocker);
                    _logger.LogDebug("更新现有用户锁柜关联：{UserLockerId}", userLocker.UserLockerId);
                }
                else
                {
                    await _dbContext.UserLockers.AddAsync(userLocker);
                    _logger.LogDebug("添加新用户锁柜关联：{UserLockerId}", userLocker.UserLockerId);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogDebug("成功保存用户锁柜关联到数据库：{UserLockerId}", userLocker.UserLockerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存用户锁柜关联到数据库失败，关联ID：{UserLockerId}", userLocker?.UserLockerId);
                return false;
            }
        }

        private async Task<bool> DeleteAllLockersAsync()
        {
            try
            {
                // 先删除用户锁柜关联记录
                var userLockers = _dbContext.UserLockers.ToList();
                if (userLockers.Count > 0)
                {
                    _dbContext.UserLockers.RemoveRange(userLockers);
                    _logger.LogInformation("删除 {Count} 个用户锁柜关联记录", userLockers.Count);
                }

                // 删除所有锁柜
                var allLockers = _dbContext.Lockers.ToList();
                if (allLockers.Count > 0)
                {
                    _dbContext.Lockers.RemoveRange(allLockers);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("删除 {Count} 个锁柜", allLockers.Count);
                }
                else
                {
                    _logger.LogInformation("没有需要删除的锁柜");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除锁柜时发生异常");
                return false;
            }
        }

        private async Task<bool> DeleteAllUserLockersAsync()
        {
            try
            {
                var allUserLockers = _dbContext.UserLockers.ToList();
                if (allUserLockers.Count > 0)
                {
                    _dbContext.UserLockers.RemoveRange(allUserLockers);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("删除 {Count} 个用户锁柜关联", allUserLockers.Count);
                }
                else
                {
                    _logger.LogInformation("没有需要删除的用户锁柜关联");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户锁柜关联时发生异常");
                return false;
            }
        }

        #endregion

        #region 私有方法 - 事件处理

        private async void OnNetworkDataReceived(object? sender, DataReceivedEventArgs e)
        {
            try
            {
                if (e.DataType == "protocol" && !string.IsNullOrEmpty(e.MessageType))
                {
                    _logger.LogDebug("收到协议数据，消息类型：{MessageType}，数据长度：{DataLength}", e.MessageType, e.DataLength);

                    // 根据消息类型处理数据
                    switch (e.MessageType)
                    {
                        case "sync_boards":
                            await HandleDataResponseAsync("boards", e.Data);
                            break;
                        case "sync_roles":
                            await HandleDataResponseAsync("roles", e.Data);
                            break;
                        case "sync_users":
                            await HandleDataResponseAsync("users", e.Data);
                            break;
                        case "sync_lockers":
                            await HandleDataResponseAsync("lockers", e.Data);
                            break;
                        case "sync_user_lockers":
                            await HandleDataResponseAsync("userlockers", e.Data);
                            break;
                        default:
                            _logger.LogDebug("忽略未处理的消息类型：{MessageType}", e.MessageType);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理网络数据接收事件时发生异常");
            }
        }

        private void OnNetworkConnectionStatusChanged(object? sender, ConnectionStatusChangedEventArgs e)
        {
            _logger.LogInformation("网络连接状态变更：{Status}，已连接：{IsConnected}", e.Status, e.IsConnected);

            if (e.IsConnected)
            {
                // 连接建立后自动开始同步访问日志
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    if (!_isSyncing)
                    {
                        _logger.LogInformation("网络连接建立，开始自动同步访问日志");
                        await SyncAccessLogsAsync();
                    }
                });
            }
            else
            {
                // 连接断开时取消所有待处理的请求
                lock (_pendingRequestsLock)
                {
                    foreach (var request in _pendingRequests.Values)
                    {
                        request.TrySetCanceled();
                    }
                    _pendingRequests.Clear();
                    _logger.LogInformation("网络连接断开，已取消所有待处理的同步请求");
                }
            }
        }

        #endregion
    }
}