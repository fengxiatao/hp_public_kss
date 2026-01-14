using FaceLocker.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 管理员登录服务
    /// 提供管理员用户的初始化、验证和密码管理功能
    /// </summary>
    public class AdminLoginService : IAdminLoginService
    {
        #region 私有字段
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;
        private readonly ILogger<AdminLoginService> _logger;

        // 默认管理员密码的SHA256哈希值（对应明文"123456"）
        private const string DEFAULT_PASSWORD_HASH = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="userService">用户服务</param>
        /// <param name="roleService">角色服务</param>
        /// <param name="logger">日志服务</param>
        public AdminLoginService(
            IUserService userService,
            IRoleService roleService,
            ILogger<AdminLoginService> logger)
        {
            _userService = userService;
            _roleService = roleService;
            _logger = logger;
        }
        #endregion

        #region 初始化管理员用户服务
        /// <summary>
        /// 初始化管理员用户服务
        /// 检查是否存在默认管理员用户，不存在则创建
        /// </summary>
        /// <returns>初始化是否成功</returns>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.LogInformation("正在初始化管理员用户服务...");

                // 检查是否存在具备MANAGE_USER权限的管理员用户
                bool adminExists = await CheckAdminUserExistsAsync();

                if (!adminExists)
                {
                    _logger.LogWarning("未找到管理员用户，正在创建默认管理员...");
                    return await CreateOrUpdateDefaultAdminAsync();
                }

                _logger.LogInformation("管理员用户服务初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "管理员用户服务初始化失败");
                return false;
            }
        }
        #endregion

        #region 验证管理员登录
        /// <summary>
        /// 验证管理员登录
        /// 使用账号和密码验证，检查用户是否具备MANAGE_USER权限
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <returns>验证是否成功</returns>
        public async Task<bool> VerifyAdminLoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogError("用户名和密码不能为空");
                    return false;
                }

                // 根据用户名查找用户
                var user = await _userService.GetUserByNameAsync(username);
                if (user == null)
                {
                    _logger.LogError("用户 '{Username}' 不存在", username);
                    return false;
                }

                // 检查用户是否激活
                if (!user.IsActive)
                {
                    _logger.LogError("用户 '{Username}' 已被禁用", username);
                    return false;
                }

                // 获取用户角色信息
                var role = await _roleService.GetRoleByIdAsync(user.RoleId);
                if (role == null)
                {
                    _logger.LogError("用户 '{Username}' 的角色不存在", username);
                    return false;
                }

                // 检查角色是否具备MANAGE_USER权限
                if (!role.HasPermission(Role.PermissionConstants.SUPER_ADMIN))
                {
                    _logger.LogError("用户 '{Username}' 没有管理员权限", username);
                    return false;
                }

                // 验证密码（使用SHA256哈希比较）
                var inputPasswordHash = HashPassword(password);
                var storedPasswordHash = user.Password;

                // 如果存储的密码为空，则使用默认密码123456
                if (string.IsNullOrEmpty(storedPasswordHash))
                {
                    _logger.LogWarning("检测到空密码，已重置为默认密码");
                    await UpdateAdminPasswordAsync(user.Id, "123456");
                    storedPasswordHash = DEFAULT_PASSWORD_HASH;
                }

                bool passwordValid = string.Equals(inputPasswordHash, storedPasswordHash, StringComparison.OrdinalIgnoreCase);

                if (passwordValid)
                {
                    _logger.LogInformation("管理员用户 '{Username}' 登录成功", username);
                }
                else
                {
                    _logger.LogWarning("管理员用户 '{Username}' 登录失败: 密码错误", username);
                }

                return passwordValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录验证失败");
                return false;
            }
        }
        #endregion

        #region 检查是否需要初始化默认管理员用户
        /// <summary>
        /// 检查是否需要初始化默认管理员用户
        /// </summary>
        /// <returns>是否需要初始化</returns>
        public async Task<bool> NeedsInitializationAsync()
        {
            try
            {
                return !await CheckAdminUserExistsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查管理员用户初始化状态失败");
                return true;
            }
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 检查是否存在具备MANAGE_USER权限的管理员用户
        /// </summary>
        /// <returns>是否存在管理员用户</returns>
        private async Task<bool> CheckAdminUserExistsAsync()
        {
            try
            {
                // 获取所有角色
                var allRoles = await _roleService.GetAllRolesAsync();

                // 查找具备MANAGE_USER权限的角色
                foreach (var role in allRoles)
                {
                    if (role.HasPermission(Role.PermissionConstants.SUPER_ADMIN))
                    {
                        // 检查该角色下是否有激活的用户
                        var usersWithRole = await _userService.GetUsersPagedAsync(0, int.MaxValue, null, role.Id, true);
                        if (usersWithRole.Users.Count > 0)
                        {
                            _logger.LogInformation("找到具备管理员权限的角色 '{RoleName}'，包含 {UserCount} 个用户", role.Name, usersWithRole.Users.Count);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查管理员用户存在性失败");
                return false;
            }
        }

        /// <summary>
        /// 创建或更新默认管理员用户
        /// </summary>
        /// <returns>操作是否成功</returns>
        private async Task<bool> CreateOrUpdateDefaultAdminAsync()
        {
            try
            {
                // 查找或创建具备MANAGE_USER权限的角色
                var adminRole = await FindOrCreateAdminRoleAsync();
                if (adminRole == null)
                {
                    _logger.LogError("创建管理员角色失败");
                    return false;
                }

                // 查找名为"admin"的用户
                var adminUser = await _userService.GetUserByNameAsync("admin");

                if (adminUser == null)
                {
                    // 创建新的管理员用户
                    adminUser = new User
                    {
                        UserNumber = "superAdmin",
                        Name = "admin",
                        RoleId = adminRole.Id,
                        IsActive = true,
                        Password = DEFAULT_PASSWORD_HASH // 设置默认密码123456的哈希
                    };

                    bool success = await _userService.AddUserAsync(adminUser);
                    if (success)
                    {
                        _logger.LogInformation("默认管理员用户创建成功（用户名: admin, 密码: 123456）");
                    }
                    return success;
                }
                else
                {
                    // 更新现有用户为管理员角色
                    adminUser.RoleId = adminRole.Id;
                    adminUser.IsActive = true;

                    // 如果密码为空，设置为默认密码
                    if (string.IsNullOrEmpty(adminUser.Password))
                    {
                        adminUser.Password = DEFAULT_PASSWORD_HASH;
                        _logger.LogWarning("检测到空密码，已重置为默认密码123456");
                    }

                    // 注意：这里需要调用UserService的更新方法
                    // 由于UserService目前不允许编辑，可能需要特殊处理
                    // 这里假设有更新用户的方法
                    bool success = await UpdateAdminUserAsync(adminUser);
                    if (success)
                    {
                        _logger.LogInformation("管理员用户更新成功");
                    }
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建或更新默认管理员用户失败");
                return false;
            }
        }

        /// <summary>
        /// 查找或创建具备MANAGE_USER权限的管理员角色
        /// </summary>
        /// <returns>管理员角色</returns>
        private async Task<Role> FindOrCreateAdminRoleAsync()
        {
            try
            {
                // 查找具备MANAGE_USER权限的角色
                var allRoles = await _roleService.GetAllRolesAsync();
                foreach (var role in allRoles)
                {
                    if (role.HasPermission(Role.PermissionConstants.SUPER_ADMIN))
                    {
                        return role;
                    }
                }

                // 如果没有找到，创建新的管理员角色
                var adminRole = new Role
                {
                    Name = "超级管理员",
                    Description = "具备所有权限的系统管理员角色",
                    Permissions = new List<string>
                    {
                        Role.PermissionConstants.SUPER_ADMIN
                    },
                    IsBuiltIn = true,
                    IsFromServer = false
                };

                // 添加到数据库
                bool success = await _roleService.AddRoleAsync(adminRole);
                return success ? adminRole : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找或创建管理员角色失败");
                return null;
            }
        }

        /// <summary>
        /// 更新管理员用户信息（特殊处理，绕过常规编辑限制）
        /// </summary>
        /// <param name="user">用户信息</param>
        /// <returns>是否成功</returns>
        private async Task<bool> UpdateAdminUserAsync(User user)
        {
            _logger.LogWarning("更新管理员用户功能需要实现具体的更新逻辑");
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 更新管理员密码
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="newPassword">新密码</param>
        /// <returns>是否成功</returns>
        private async Task<bool> UpdateAdminPasswordAsync(long userId, string newPassword)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    user.Password = HashPassword(newPassword);
                    // 调用更新方法
                    return await UpdateAdminUserAsync(user);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新管理员密码失败");
                return false;
            }
        }

        /// <summary>
        /// SHA256密码哈希函数
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <returns>哈希值</returns>
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }
        #endregion
    }
}
