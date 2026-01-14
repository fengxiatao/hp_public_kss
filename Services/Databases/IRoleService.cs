using FaceLocker.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 角色管理服务接口
    /// 提供角色的增删改查、权限管理和角色分配等功能
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// 获取所有角色列表
        /// </summary>
        /// <returns>角色列表</returns>
        Task<List<Role>> GetAllRolesAsync();

        /// <summary>
        /// 根据ID获取角色
        /// </summary>
        /// <param name="id">角色ID</param>
        /// <returns>角色对象</returns>
        Task<Role> GetRoleByIdAsync(long id);

        /// <summary>
        /// 根据名称获取角色
        /// </summary>
        /// <param name="name">角色名称</param>
        /// <returns>角色对象</returns>
        Task<Role> GetRoleByNameAsync(string name);

        /// <summary>
        /// 添加新角色
        /// </summary>
        /// <param name="role">角色对象</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddRoleAsync(Role role);

        /// <summary>
        /// 更新角色信息
        /// </summary>
        /// <param name="role">角色对象</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateRoleAsync(Role role);

        /// <summary>
        /// 删除角色
        /// </summary>
        /// <param name="id">角色ID</param>
        /// <returns>是否删除成功</returns>
        Task<bool> DeleteRoleAsync(long id);

        /// <summary>
        /// 检查角色名称是否已存在
        /// </summary>
        /// <param name="name">角色名称</param>
        /// <param name="excludeId">排除的角色ID</param>
        /// <returns>是否存在同名角色</returns>
        Task<bool> RoleNameExistsAsync(string name, long? excludeId = null);

        /// <summary>
        /// 获取所有可用权限列表
        /// </summary>
        /// <returns>权限名称列表</returns>
        Task<List<string>> GetAllPermissionsAsync();

        /// <summary>
        /// 获取权限分组信息
        /// </summary>
        /// <returns>权限分组字典</returns>
        Task<Dictionary<string, List<string>>> GetPermissionGroupsAsync();

        /// <summary>
        /// 检查角色是否有指定权限
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <param name="permission">权限名称</param>
        /// <returns>是否有权限</returns>
        Task<bool> HasPermissionAsync(long roleId, string permission);

        /// <summary>
        /// 为角色添加权限
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <param name="permission">权限名称</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddPermissionAsync(long roleId, string permission);

        /// <summary>
        /// 从角色移除权限
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <param name="permission">权限名称</param>
        /// <returns>是否移除成功</returns>
        Task<bool> RemovePermissionAsync(long roleId, string permission);

        /// <summary>
        /// 获取使用指定角色的用户数量
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <returns>用户数量</returns>
        Task<int> GetUserCountByRoleAsync(long roleId);

        /// <summary>
        /// 验证角色是否可以编辑
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <returns>是否可以编辑</returns>
        Task<bool> CanEditRoleAsync(long roleId);

        /// <summary>
        /// 验证角色是否可以删除
        /// </summary>
        /// <param name="roleId">角色ID</param>
        /// <returns>是否可以删除</returns>
        Task<bool> CanDeleteRoleAsync(long roleId);

        /// <summary>
        /// 获取权限显示名称
        /// </summary>
        /// <param name="permission">权限标识符</param>
        /// <returns>权限显示名称</returns>
        Task<string> GetPermissionDisplayNameAsync(string permission);
    }
}