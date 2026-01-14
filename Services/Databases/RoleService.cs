using FaceLocker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 角色服务实现类
    /// 提供角色管理的具体实现，包括角色CRUD操作、权限管理和角色分配等
    /// </summary>
    public class RoleService : IRoleService
    {
        private readonly FaceLockerDbContext _dbContext;
        private readonly ILogger<RoleService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="logger">日志服务</param>
        public RoleService(
            FaceLockerDbContext dbContext,
            ILogger<RoleService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        #region 角色CRUD操作

        /// <summary>
        /// 获取所有角色列表
        /// </summary>
        public async Task<List<Role>> GetAllRolesAsync()
        {
            try
            {
                _logger.LogInformation("开始获取所有角色列表");

                var roles = await _dbContext.Roles
                    .OrderBy(r => r.Name)
                    .ToListAsync();

                _logger.LogInformation("成功获取所有角色列表，共 {RoleCount} 个角色", roles.Count);
                return roles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表失败");
                return new List<Role>();
            }
        }

        /// <summary>
        /// 根据ID获取角色
        /// </summary>
        public async Task<Role> GetRoleByIdAsync(long id)
        {
            try
            {
                _logger.LogInformation("根据ID获取角色: {RoleId}", id);

                if (id == 0)
                {
                    _logger.LogWarning("角色ID为空");
                    return new Role();
                }

                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (role == null)
                {
                    _logger.LogWarning("未找到ID为 {RoleId} 的角色", id);
                    return new Role();
                }

                _logger.LogInformation("成功获取角色: {RoleName} (ID: {RoleId})", role.Name, role.Id);
                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取角色失败: ID={RoleId}", id);
                return new Role();
            }
        }

        /// <summary>
        /// 根据名称获取角色
        /// </summary>
        public async Task<Role> GetRoleByNameAsync(string name)
        {
            try
            {
                _logger.LogInformation("根据名称获取角色: {RoleName}", name);

                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogWarning("角色名称为空");
                    return new Role();
                }

                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Name == name.Trim());

                if (role == null)
                {
                    _logger.LogWarning("未找到名称为 {RoleName} 的角色", name);
                    return new Role();
                }

                _logger.LogInformation("成功获取角色: {RoleName} (ID: {RoleId})", role.Name, role.Id);
                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据名称获取角色失败: Name={RoleName}", name);
                return new Role();
            }
        }

        /// <summary>
        /// 添加新角色
        /// </summary>
        public async Task<bool> AddRoleAsync(Role role)
        {
            try
            {
                _logger.LogInformation("开始添加新角色: {RoleName}", role.Name);

                // 验证角色名称
                if (!Role.IsValidName(role.Name))
                {
                    _logger.LogWarning("角色名称格式无效: {RoleName}", role.Name);
                    return false;
                }

                // 检查角色名称是否已存在
                if (await RoleNameExistsAsync(role.Name))
                {
                    _logger.LogWarning("角色名称已存在: {RoleName}", role.Name);
                    return false;
                }

                // 设置创建和更新时间
                role.CreatedAt = DateTime.Now;
                role.UpdatedAt = DateTime.Now;

                _dbContext.Roles.Add(role);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功添加角色: {RoleName} (ID: {RoleId})", role.Name, role.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加角色失败: Name={RoleName}", role.Name);
                return false;
            }
        }

        /// <summary>
        /// 更新角色信息
        /// </summary>
        public async Task<bool> UpdateRoleAsync(Role role)
        {
            try
            {
                _logger.LogInformation("开始更新角色: {RoleName} (ID: {RoleId})", role.Name, role.Id);

                // 验证角色名称
                if (!Role.IsValidName(role.Name))
                {
                    _logger.LogWarning("角色名称格式无效: {RoleName}", role.Name);
                    return false;
                }

                // 检查角色名称是否已存在（排除当前角色）
                if (await RoleNameExistsAsync(role.Name, role.Id))
                {
                    _logger.LogWarning("角色名称已存在: {RoleName}", role.Name);
                    return false;
                }

                // 检查角色是否可以编辑
                if (!await CanEditRoleAsync(role.Id))
                {
                    _logger.LogWarning("角色是系统内置角色，不可编辑: {RoleName}", role.Name);
                    return false;
                }

                // 更新角色信息
                var existingRole = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Id == role.Id);

                if (existingRole == null)
                {
                    _logger.LogWarning("角色不存在: ID={RoleId}", role.Id);
                    return false;
                }

                // 更新基本信息
                existingRole.Name = role.Name;
                existingRole.Description = role.Description;
                existingRole.Permissions = role.Permissions;
                existingRole.UpdatedAt = DateTime.Now;

                _dbContext.Roles.Update(existingRole);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功更新角色: {RoleName} (ID: {RoleId})", role.Name, role.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新角色失败: ID={RoleId}, Name={RoleName}", role.Id, role.Name);
                return false;
            }
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        public async Task<bool> DeleteRoleAsync(long id)
        {
            try
            {
                _logger.LogInformation("开始删除角色: ID={RoleId}", id);

                var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id);

                if (role == null)
                {
                    _logger.LogWarning("角色不存在: ID={RoleId}", id);
                    return false;
                }

                // 检查角色是否可以删除
                if (!await CanDeleteRoleAsync(id))
                {
                    _logger.LogWarning("角色是系统内置角色，不可删除: {RoleName}", role.Name);
                    return false;
                }

                // 检查是否有用户使用此角色
                int userCount = await GetUserCountByRoleAsync(id);
                if (userCount > 0)
                {
                    _logger.LogWarning("角色正在被用户使用，无法删除: {RoleName}, 用户数量: {UserCount}", role.Name, userCount);
                    return false;
                }

                _dbContext.Roles.Remove(role);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功删除角色: {RoleName} (ID: {RoleId})", role.Name, id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除角色失败: ID={RoleId}", id);
                return false;
            }
        }

        #endregion

        #region 角色验证方法

        /// <summary>
        /// 检查角色名称是否已存在
        /// </summary>
        public async Task<bool> RoleNameExistsAsync(string name, long? excludeId = null)
        {
            try
            {
                _logger.LogDebug("检查角色名称是否存在: {RoleName}, 排除ID: {ExcludeId}", name, excludeId);

                if (string.IsNullOrWhiteSpace(name))
                    return false;

                var query = _dbContext.Roles.Where(r => r.Name == name.Trim());

                if (excludeId != 0)
                {
                    query = query.Where(r => r.Id != excludeId);
                }

                bool exists = await query.AnyAsync();
                _logger.LogDebug("角色名称 {RoleName} 存在: {Exists}", name, exists);
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查角色名称是否存在失败: Name={RoleName}", name);
                return false;
            }
        }

        /// <summary>
        /// 验证角色是否可以编辑
        /// </summary>
        public async Task<bool> CanEditRoleAsync(long roleId)
        {
            try
            {
                _logger.LogDebug("验证角色可编辑性: RoleId={RoleId}", roleId);

                var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

                bool canEdit = role?.CanEdit ?? false;
                _logger.LogDebug("角色 {RoleId} 可编辑: {CanEdit}", roleId, canEdit);
                return canEdit;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色可编辑性失败: RoleId={RoleId}", roleId);
                return false;
            }
        }

        /// <summary>
        /// 验证角色是否可以删除
        /// </summary>
        public async Task<bool> CanDeleteRoleAsync(long roleId)
        {
            try
            {
                _logger.LogDebug("验证角色可删除性: RoleId={RoleId}", roleId);

                var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

                bool canDelete = role?.CanDelete ?? false;
                _logger.LogDebug("角色 {RoleId} 可删除: {CanDelete}", roleId, canDelete);
                return canDelete;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色可删除性失败: RoleId={RoleId}", roleId);
                return false;
            }
        }

        #endregion

        #region 权限管理方法

        /// <summary>
        /// 获取所有可用权限列表
        /// </summary>
        public Task<List<string>> GetAllPermissionsAsync()
        {
            _logger.LogDebug("获取所有可用权限列表");
            return Task.FromResult(Role.GetAllPermissions());
        }

        /// <summary>
        /// 获取权限分组信息
        /// </summary>
        public Task<Dictionary<string, List<string>>> GetPermissionGroupsAsync()
        {
            _logger.LogDebug("获取权限分组信息");
            return Task.FromResult(Role.GetPermissionGroups());
        }

        /// <summary>
        /// 检查角色是否有指定权限
        /// </summary>
        public async Task<bool> HasPermissionAsync(long roleId, string permission)
        {
            try
            {
                _logger.LogDebug("检查角色权限: RoleId={RoleId}, Permission={Permission}", roleId, permission);

                var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

                if (role == null)
                {
                    _logger.LogWarning("角色不存在: RoleId={RoleId}", roleId);
                    return false;
                }

                bool hasPermission = role.HasPermission(permission);
                _logger.LogDebug("角色 {RoleId} 有权限 {Permission}: {HasPermission}", roleId, permission, hasPermission);
                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查角色权限失败: RoleId={RoleId}, Permission={Permission}", roleId, permission);
                return false;
            }
        }

        /// <summary>
        /// 为角色添加权限
        /// </summary>
        public async Task<bool> AddPermissionAsync(long roleId, string permission)
        {
            try
            {
                _logger.LogInformation("为角色添加权限: RoleId={RoleId}, Permission={Permission}", roleId, permission);

                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Id == roleId);

                if (role == null)
                {
                    _logger.LogWarning("角色不存在: ID={RoleId}", roleId);
                    return false;
                }

                // 检查权限是否有效
                var allPermissions = await GetAllPermissionsAsync();
                if (!allPermissions.Contains(permission))
                {
                    _logger.LogWarning("权限无效: {Permission}", permission);
                    return false;
                }

                // 检查角色是否已有此权限
                if (role.HasPermission(permission))
                {
                    _logger.LogWarning("角色已有此权限: {RoleName}, {Permission}", role.Name, permission);
                    return true; // 不算失败，只是无需添加
                }

                role.AddPermission(permission);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功为角色添加权限: {RoleName}, {Permission}", role.Name, permission);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为角色添加权限失败: RoleId={RoleId}, Permission={Permission}", roleId, permission);
                return false;
            }
        }

        /// <summary>
        /// 从角色移除权限
        /// </summary>
        public async Task<bool> RemovePermissionAsync(long roleId, string permission)
        {
            try
            {
                _logger.LogInformation("从角色移除权限: RoleId={RoleId}, Permission={Permission}", roleId, permission);

                var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

                if (role == null)
                {
                    _logger.LogWarning("角色不存在: ID={RoleId}", roleId);
                    return false;
                }

                // 检查角色是否有此权限
                if (!role.HasPermission(permission))
                {
                    _logger.LogWarning("角色没有此权限: {RoleName}, {Permission}", role.Name, permission);
                    return true; // 不算失败，只是无需移除
                }

                role.RemovePermission(permission);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功从角色移除权限: {RoleName}, {Permission}", role.Name, permission);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从角色移除权限失败: RoleId={RoleId}, Permission={Permission}", roleId, permission);
                return false;
            }
        }

        /// <summary>
        /// 获取权限显示名称
        /// </summary>
        public Task<string> GetPermissionDisplayNameAsync(string permission)
        {
            _logger.LogDebug("获取权限显示名称: {Permission}", permission);
            return Task.FromResult(Role.GetPermissionDisplayName(permission));
        }

        #endregion

        #region 统计和初始化方法

        /// <summary>
        /// 获取使用指定角色的用户数量
        /// </summary>
        public async Task<int> GetUserCountByRoleAsync(long roleId)
        {
            try
            {
                _logger.LogDebug("获取角色用户数量: RoleId={RoleId}", roleId);

                var userCount = await _dbContext.Users.Where(u => u.RoleId == roleId).CountAsync();

                _logger.LogDebug("角色 {RoleId} 的用户数量: {UserCount}", roleId, userCount);
                return userCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色用户数量失败: RoleId={RoleId}", roleId);
                return 0;
            }
        }
        #endregion
    }
}