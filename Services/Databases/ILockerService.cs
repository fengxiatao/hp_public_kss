using FaceLocker.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 柜子管理服务接口
    /// 提供柜子的增删改查、状态管理和数据同步等功能
    /// </summary>
    public interface ILockerService
    {
        /// <summary>
        /// 添加新柜子
        /// </summary>
        /// <param name="locker">柜子对象</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddLockerAsync(Locker locker);

        /// <summary>
        /// 更新柜子信息
        /// </summary>
        /// <param name="locker">柜子对象</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateLockerAsync(Locker locker);

        /// <summary>
        /// 删除柜子
        /// </summary>
        /// <param name="lockerId">柜子ID</param>
        /// <returns>是否删除成功</returns>
        Task<bool> DeleteLockerAsync(long lockerId);

        /// <summary>
        /// 获取柜子
        /// </summary>
        /// <param name="lockerId"></param>
        /// <returns></returns>
        Task<Locker?> GetLockerAsync(long lockerId);

        /// <summary>
        /// 根据板号和通道号获取柜子
        /// </summary>
        /// <param name="boardAddress">板地址</param>
        /// <param name="channelNumber">通道号</param>
        /// <returns>柜子对象</returns>
        Task<Locker?> GetLockerByAddressAndChannelAsync(int boardAddress, int channelNumber);

        /// <summary>
        /// 获取所有柜子列表
        /// </summary>
        /// <returns>柜子列表</returns>
        Task<List<Locker>> GetLockersAsync();

        /// <summary>
        /// 获取可用的柜子
        /// </summary>
        Task<Locker?> GetAvailableLockerAsync();

        /// <summary>
        /// 分配柜子给用户
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="lockerId"></param>
        /// <returns></returns>
        Task<(bool, UserLocker)> AssignLockerToUserAsync(long userId, long lockerId);

        /// <summary>
        /// 获取用户已分配的柜格
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<List<UserLocker>> GetUserLockersAsync(long userId);
        /// <summary>
        /// 更新用户已分配的柜格状态
        /// </summary>
        /// <param name="userLockerId"></param>
        /// <param name="storageStatus"></param>
        /// <returns></returns>
        Task<bool> UpdateUserLockerStatusAsync(long userLockerId, StorageStatus storageStatus);
    }
}