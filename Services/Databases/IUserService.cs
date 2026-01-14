using FaceLocker.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 用户服务接口 - 定义用户管理、权限控制和数据同步等相关操作
    /// 注意：本接口已禁止"编辑/更新"用户信息，只允许添加和删除用户，所有用户均不可编辑
    /// </summary>
    public interface IUserService
    {

        #region 用户管理方法

        /// <summary>
        /// 添加新用户
        /// </summary>
        /// <param name="user">要添加的用户对象</param>
        /// <returns>添加操作是否成功</returns>
        Task<bool> AddUserAsync(User user);

        /// <summary>
        /// 添加新用户(人脸拍照用户)
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        Task<bool> AddFaceImageUserAsync(User user);

        /// <summary>
        /// 更新用户信息
        /// </summary>
        /// <param name="user">要更新的用户对象</param>
        /// <returns>更新操作是否成功</returns>
        Task<bool> UpdateUserAsync(User user);

        /// <summary>
        /// 删除用户
        /// </summary>
        /// <param name="userId">要删除的用户ID</param>
        /// <returns>删除操作是否成功</returns>
        Task<bool> DeleteUserAsync(long userId);

        /// <summary>
        /// 获取所有用户
        /// </summary>
        /// <returns>用户列表</returns>
        Task<List<User>> GetAllUsersAsync();

        /// <summary>
        /// 获取用户最大ID
        /// </summary>
        /// <returns></returns>
        Task<long> GetMaxUserIdAsync();

        /// <summary>
        /// 根据用户ID获取用户信息
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>对应的用户对象，如果不存在则返回null</returns>
        Task<User?> GetUserByIdAsync(long userId);

        /// <summary>
        /// 根据用户编号获取用户信息
        /// </summary>
        /// <param name="userNumber">用户编号</param>
        /// <returns>对应的用户对象，如果不存在则返回null</returns>
        Task<User?> GetUserByNumberAsync(string userNumber);

        /// <summary>
        /// 根据人脸数据获取用户信息
        /// </summary>
        /// <param name="faceData">人脸特征数据（Base64格式）</param>
        /// <returns>对应的用户对象，如果不存在则返回null</returns>
        Task<User?> GetUserByFaceDataAsync(string faceData);

        /// <summary>
        /// 根据用户名获取用户信息
        /// </summary>
        /// <param name="name">用户名</param>
        /// <returns>对应的用户对象，如果不存在则返回null</returns>
        Task<User?> GetUserByNameAsync(string name);

        /// <summary>
        /// 根据关键字搜索用户
        /// </summary>
        /// <param name="keyword">搜索关键字（可匹配姓名、编号、部门、备注）</param>
        /// <returns>匹配的用户列表</returns>
        Task<List<User>> SearchUsersAsync(string keyword);

        /// <summary>
        /// 分页获取用户列表
        /// </summary>
        /// <param name="pageIndex">页码（从0开始）</param>
        /// <param name="pageSize">每页记录数</param>
        /// <param name="searchKeyword">搜索关键字</param>
        /// <param name="roleId">角色ID筛选</param>
        /// <param name="isActive">激活状态筛选</param>
        /// <returns>包含用户列表和总记录数的元组</returns>
        Task<(List<User> Users, int TotalCount)> GetUsersPagedAsync(int pageIndex, int pageSize, string searchKeyword = null, long roleId = 0, bool? isActive = null);

        /// <summary>
        /// 根据柜子编号获取拥有该柜子权限的所有用户列表
        /// </summary>
        /// <param name="lockerNumber">柜子编号</param>
        /// <returns>拥有该柜子权限的用户列表</returns>
        Task<List<User>> GetUsersByLockerNumberAsync(string lockerNumber);

        #endregion

        #region 权限管理方法

        /// <summary>
        /// 为用户分配柜子权限
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="lockerIds">要分配的柜子ID列表</param>
        /// <returns>分配操作是否成功</returns>
        Task<bool> AssignLockersToUserAsync(long userId, List<long> lockerIds);

        /// <summary>
        /// 设置用户角色
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="roleId">角色ID</param>
        /// <returns>设置操作是否成功</returns>
        Task<bool> SetUserRoleAsync(long userId, long roleId);

        /// <summary>
        /// 检查用户是否有指定柜子的访问权限
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="lockerId">柜子ID</param>
        /// <returns>是否有访问权限</returns>
        Task<bool> CheckUserLockerPermissionAsync(long userId, long lockerId);

        #endregion
    }
}