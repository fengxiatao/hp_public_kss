using FaceLocker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 用户服务实现
    /// </summary>
    public class UserService : IUserService
    {
        private readonly ILogger<UserService> _logger;
        private readonly FaceLockerDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志服务</param>
        /// <param name="dbContext">数据库上下文</param>
        public UserService(ILogger<UserService> logger, FaceLockerDbContext dbContext, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
        }

        #region 添加用户
        /// <summary>
        /// 添加用户
        /// </summary>
        public async Task<bool> AddUserAsync(User user)
        {
            try
            {
                _logger.LogInformation("开始添加用户: {UserName}", user.Name);

                if (user == null)
                {
                    _logger.LogWarning("用户对象为空");
                    throw new ArgumentNullException(nameof(user));
                }

                // 检查用户编号是否已存在
                var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserNumber == user.UserNumber);

                if (existingUser != null)
                {
                    _logger.LogWarning("用户编号已存在: {UserNumber}", user.UserNumber);
                    return false;
                }

                // 验证用户数据
                if (!ValidateUser(user))
                {
                    _logger.LogWarning("用户数据验证失败");
                    return false;
                }

                // 设置创建时间
                user.CreatedAt = DateTime.Now;
                user.UpdatedAt = DateTime.Now;

                // 添加到数据库
                await _dbContext.Users.AddAsync(user);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功添加用户: {UserName} (ID: {UserId}, 编号: {UserNumber})",
                    user.Name, user.Id, user.UserNumber);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加用户失败: {UserName}", user?.Name);
                return false;
            }
        }
        #endregion

        #region 添加新用户(人脸拍照用户)
        /// <summary>
        /// 添加新用户(人脸拍照用户)
        /// </summary>
        public async Task<bool> AddFaceImageUserAsync(User user)
        {
            try
            {
                _logger.LogInformation("开始添加人脸拍照用户: {UserName}", user.Name);

                // 验证用户数据
                if (!ValidateUser(user))
                {
                    _logger.LogWarning("用户数据验证失败");
                    return false;
                }

                // 添加用户
                return await AddUserAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加人脸拍照用户失败: {UserName}", user?.Name);
                return false;
            }
        }
        #endregion

        #region 独立生成用户人脸特征数据
        public async Task GenerateUserFaceFeatureDataAsync(string userId)
        {
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var baiduFaceService = scopedServices.GetRequiredService<BaiduFaceService>();

            var userForFace = await _dbContext.Users.FindAsync(userId);
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
                }
            }
        }
        #endregion

        #region 更新用户信息
        /// <summary>
        /// 更新用户信息
        /// </summary>
        public async Task<bool> UpdateUserAsync(User user)
        {
            try
            {
                _logger.LogInformation("开始更新用户: {UserName} (ID: {UserId})", user.Name, user.Id);

                if (user == null)
                {
                    _logger.LogWarning("用户对象为空");
                    throw new ArgumentNullException(nameof(user));
                }

                // 验证用户数据
                if (!ValidateUser(user))
                {
                    _logger.LogWarning("用户数据验证失败");
                    return false;
                }

                // 更新修改时间
                user.UpdatedAt = DateTime.Now;

                // 更新数据库
                _dbContext.Users.Update(user);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功更新用户: {UserName} (ID: {UserId})", user.Name, user.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败: {UserName} (ID: {UserId})", user.Name, user.Id);
                return false;
            }
        }
        #endregion

        #region 删除用户
        /// <summary>
        /// 删除用户
        /// </summary>
        public async Task<bool> DeleteUserAsync(long userId)
        {
            try
            {
                _logger.LogInformation("开始删除用户: ID={UserId}", userId);

                if (userId < 1)
                {
                    _logger.LogWarning("用户ID为空");
                    throw new ArgumentException("用户ID不能为空", nameof(userId));
                }

                // 从数据库查找用户
                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning("未找到用户: ID={UserId}", userId);
                    return false;
                }

                // 从数据库删除
                _dbContext.Users.Remove(user);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功删除用户: {UserName} (ID: {UserId})", user.Name, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败: ID={UserId}", userId);
                return false;
            }
        }
        #endregion

        #region 获取所有用户
        /// <summary>
        /// 获取所有用户
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                _logger.LogDebug("开始获取所有用户");

                var users = await _dbContext.Users.OrderBy(u => u.Name).ToListAsync();

                _logger.LogDebug("成功获取了 {Count} 个用户", users.Count);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有用户失败");
                return new List<User>();
            }
        }
        #endregion

        #region 获取用户最大Id
        public async Task<long> GetMaxUserIdAsync()
        {
            try
            {
                _logger.LogDebug("开始获取最大用户ID");

                var maxId = await _dbContext.Users.MaxAsync(u => u.Id);

                _logger.LogDebug("成功获取最大用户ID: {MaxId}", maxId);
                return maxId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最大用户ID失败");
                return 0;
            }
        }
        #endregion

        #region 根据用户ID获取用户信息
        /// <summary>
        /// 根据用户ID获取用户信息
        /// </summary>
        public async Task<User?> GetUserByIdAsync(long userId)
        {
            try
            {
                _logger.LogDebug("根据ID获取用户: {UserId}", userId);

                if (userId < 1)
                {
                    _logger.LogWarning("用户ID为空");
                    return null;
                }

                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogDebug("未找到用户: ID={UserId}", userId);
                }
                else
                {
                    _logger.LogDebug("成功获取用户: {UserName} (ID: {UserId})", user.Name, user.Id);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取用户失败: {UserId}", userId);
                return null;
            }
        }
        #endregion

        #region 根据用户编号获取用户信息
        /// <summary>
        /// 根据用户编号获取用户信息
        /// </summary>
        public async Task<User?> GetUserByNumberAsync(string userNumber)
        {
            try
            {
                _logger.LogDebug("根据用户编号获取用户: {UserNumber}", userNumber);

                if (string.IsNullOrEmpty(userNumber))
                {
                    _logger.LogWarning("用户编号为空");
                    return null;
                }

                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.UserNumber == userNumber);

                if (user == null)
                {
                    _logger.LogDebug("未找到用户: 编号={UserNumber}", userNumber);
                }
                else
                {
                    _logger.LogDebug("成功获取用户: {UserName} (编号: {UserNumber})", user.Name, user.UserNumber);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据用户编号获取用户失败: {UserNumber}", userNumber);
                return null;
            }
        }
        #endregion

        #region 根据人脸数据获取用户信息
        /// <summary>
        /// 根据人脸数据获取用户信息
        /// </summary>
        public async Task<User?> GetUserByFaceDataAsync(string faceData)
        {
            try
            {
                _logger.LogDebug("根据人脸数据获取用户");

                if (string.IsNullOrEmpty(faceData))
                {
                    _logger.LogWarning("人脸数据为空");
                    return null;
                }

                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.FaceFeatureData != null &&
                                             u.FaceFeatureData.Length > 0);

                if (user == null)
                {
                    _logger.LogDebug("未找到匹配人脸数据的用户");
                }
                else
                {
                    _logger.LogDebug("成功根据人脸数据获取用户: {UserName}", user.Name);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据人脸数据获取用户失败");
                return null;
            }
        }
        #endregion

        #region 根据用户名获取用户信息
        /// <summary>
        /// 根据用户名获取用户信息
        /// </summary>
        public async Task<User?> GetUserByNameAsync(string name)
        {
            try
            {
                _logger.LogDebug("根据用户名获取用户: {UserName}", name);

                if (string.IsNullOrEmpty(name))
                {
                    _logger.LogWarning("用户名为空");
                    return null;
                }

                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Name == name);

                if (user == null)
                {
                    _logger.LogDebug("未找到用户: 姓名={UserName}", name);
                }
                else
                {
                    _logger.LogDebug("成功获取用户: {UserName} (ID: {UserId})", user.Name, user.Id);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据用户名获取用户失败: {Name}", name);
                return null;
            }
        }
        #endregion

        #region 根据关键字搜索用户
        /// <summary>
        /// 根据关键字搜索用户
        /// </summary>
        public async Task<List<User>> SearchUsersAsync(string keyword)
        {
            try
            {
                _logger.LogDebug("搜索用户: 关键字={Keyword}", keyword);

                if (string.IsNullOrEmpty(keyword))
                {
                    _logger.LogDebug("搜索关键字为空，返回所有用户");
                    return await GetAllUsersAsync();
                }

                var users = await _dbContext.Users
                    .Where(u => u.Name.Contains(keyword) ||
                               u.UserNumber.Contains(keyword) ||
                               u.Department.Contains(keyword) ||
                               u.Remarks.Contains(keyword))
                    .OrderBy(u => u.Name)
                    .ToListAsync();

                _logger.LogDebug("搜索关键字 '{Keyword}' 找到 {Count} 个用户", keyword, users.Count);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索用户失败: {Keyword}", keyword);
                return new List<User>();
            }
        }
        #endregion

        #region 分页获取用户列表
        /// <summary>
        /// 分页获取用户列表
        /// </summary>
        public async Task<(List<User> Users, int TotalCount)> GetUsersPagedAsync(int pageIndex, int pageSize, string searchKeyword = null, long roleId = 0, bool? isActive = null)
        {
            try
            {
                _logger.LogDebug("分页获取用户: 页码={PageIndex}, 页大小={PageSize}, 搜索关键字={SearchKeyword}, 角色ID={RoleId}, 活跃状态={IsActive}",
                    pageIndex, pageSize, searchKeyword, roleId, isActive);

                var query = _dbContext.Users.AsQueryable();

                // 应用搜索条件
                if (!string.IsNullOrEmpty(searchKeyword))
                {
                    query = query.Where(u => u.Name.Contains(searchKeyword) ||
                                           u.UserNumber.Contains(searchKeyword) ||
                                           u.Department.Contains(searchKeyword));
                }

                // 应用角色筛选
                if (roleId > 0)
                {
                    query = query.Where(u => u.RoleId == roleId);
                }

                // 应用活跃状态筛选
                if (isActive.HasValue)
                {
                    query = query.Where(u => u.IsActive == isActive.Value);
                }

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页获取数据
                var users = await query
                    .OrderBy(u => u.Name)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                _logger.LogDebug("分页获取用户成功: 页码 {PageIndex}, 页大小 {PageSize}, 总数 {TotalCount}, 返回 {UserCount} 个用户",
                    pageIndex, pageSize, totalCount, users.Count);
                return (users, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页获取用户失败");
                return (new List<User>(), 0);
            }
        }
        #endregion

        #region 根据柜子编号获取用户列表
        /// <summary>
        /// 根据柜子编号获取拥有该柜子权限的所有用户列表
        /// </summary>
        public async Task<List<User>> GetUsersByLockerNumberAsync(string lockerNumber)
        {
            try
            {
                _logger.LogDebug("根据柜子编号获取用户: LockerNumber={LockerNumber}", lockerNumber);

                if (string.IsNullOrEmpty(lockerNumber))
                {
                    _logger.LogWarning("柜子编号为空");
                    return new List<User>();
                }

                // 通过 UserLockers 表关联查询
                var users = await _dbContext.UserLockers
                    .Where(ul => ul.Locker.LockerNumber == lockerNumber && ul.IsActive)
                    .Select(ul => ul.User)
                    .OrderBy(u => u.Name)
                    .ToListAsync();

                _logger.LogDebug("获取柜子 {LockerNumber} 的 {Count} 个授权用户", lockerNumber, users.Count);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据柜子编号获取用户失败: {LockerNumber}", lockerNumber);
                return new List<User>();
            }
        }
        #endregion

        #region 为用户分配柜子
        /// <summary>
        /// 为用户分配柜子
        /// </summary>
        public async Task<bool> AssignLockersToUserAsync(long userId, List<long> lockerIds)
        {
            try
            {
                _logger.LogInformation("为用户分配柜子权限: UserId={UserId}, 柜子数量={LockerCount}", userId, lockerIds?.Count);

                if (userId < 1)
                {
                    _logger.LogWarning("用户ID为空");
                    throw new ArgumentException("用户ID不能为空", nameof(userId));
                }

                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("未找到用户: ID={UserId}", userId);
                    return false;
                }

                // 先移除用户现有的所有柜子权限
                var existingUserLockers = _dbContext.UserLockers
                    .Where(ul => ul.UserId == userId);
                _dbContext.UserLockers.RemoveRange(existingUserLockers);

                // 添加新的柜子权限
                foreach (var lockerId in lockerIds)
                {
                    var locker = await _dbContext.Lockers.FindAsync(lockerId);
                    if (locker != null)
                    {
                        var userLocker = new UserLocker
                        {
                            UserId = userId,
                            LockerId = lockerId,
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            StoredTime = DateTime.Now
                        };
                        await _dbContext.UserLockers.AddAsync(userLocker);
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功为用户 {UserName} 分配 {Count} 个柜子权限", user.Name, lockerIds.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为用户分配柜子权限失败: UserId={UserId}", userId);
                return false;
            }
        }
        #endregion

        #region 设置用户角色
        /// <summary>
        /// 设置用户角色
        /// </summary>
        public async Task<bool> SetUserRoleAsync(long userId, long roleId)
        {
            try
            {
                _logger.LogInformation("设置用户角色: UserId={UserId}, RoleId={RoleId}", userId, roleId);

                if (userId < 1)
                {
                    _logger.LogWarning("用户ID为空");
                    throw new ArgumentException("用户ID不能为空", nameof(userId));
                }

                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("未找到用户: ID={UserId}", userId);
                    return false;
                }

                var role = await _dbContext.Roles.FindAsync(roleId);
                if (role == null)
                {
                    _logger.LogWarning("未找到角色: ID={RoleId}", roleId);
                    return false;
                }

                user.RoleId = roleId;
                user.UpdatedAt = DateTime.Now;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功设置用户 {UserName} 的角色为 {RoleName}", user.Name, role.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置用户角色失败: UserId={UserId}, RoleId={RoleId}", userId, roleId);
                return false;
            }
        }
        #endregion

        #region 检查用户是否有指定柜子的访问权限
        /// <summary>
        /// 检查用户是否有指定柜子的访问权限
        /// </summary>
        public async Task<bool> CheckUserLockerPermissionAsync(long userId, long lockerId)
        {
            try
            {
                _logger.LogDebug("检查用户柜子权限: UserId={UserId}, LockerId={LockerId}", userId, lockerId);

                if (userId == 0 || lockerId == 0)
                {
                    _logger.LogWarning("用户ID或柜子ID为空");
                    return false;
                }

                var hasPermission = await _dbContext.UserLockers.AnyAsync(ul => ul.UserId == userId && ul.LockerId == lockerId && ul.IsActive);

                _logger.LogDebug("用户 {UserId} 对柜子 {LockerId} 的权限: {HasPermission}", userId, lockerId, hasPermission);
                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户柜子权限失败: UserId={UserId}, LockerId={LockerId}", userId, lockerId);
                return false;
            }
        }
        #endregion

        #region 辅助方法

        /// <summary>
        /// 验证用户数据
        /// </summary>
        private bool ValidateUser(User user)
        {
            if (user == null)
            {
                _logger.LogWarning("用户对象为空");
                return false;
            }

            if (string.IsNullOrEmpty(user.Name))
            {
                _logger.LogWarning("用户姓名为空");
                return false;
            }

            if (string.IsNullOrEmpty(user.UserNumber))
            {
                _logger.LogWarning("用户编号为空");
                return false;
            }

            _logger.LogDebug("用户数据验证通过: {UserName} (编号: {UserNumber})", user.Name, user.UserNumber);
            return true;
        }

        #endregion
    }
}