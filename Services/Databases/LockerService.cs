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
    /// 柜子服务实现类
    /// 提供柜子管理的具体实现，包括柜子CRUD操作、状态管理和数据同步等
    /// </summary>
    public class LockerService : ILockerService
    {
        private readonly FaceLockerDbContext _dbContext;
        private readonly ILogger<LockerService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="logger">日志服务</param>
        public LockerService(
            FaceLockerDbContext dbContext,
            ILogger<LockerService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        #region 添加柜子
        /// <summary>
        /// 添加柜子
        /// </summary>
        /// <param name="locker">柜子对象</param>
        /// <returns>是否添加成功</returns>
        public async Task<bool> AddLockerAsync(Locker locker)
        {
            try
            {
                _logger.LogInformation("开始添加柜子: LockerName={LockerName}, LockerNumber={LockerNumber}",
                    locker.LockerName, locker.LockerNumber);

                if (locker == null)
                {
                    _logger.LogWarning("添加柜子失败: 柜子信息不能为空");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(locker.LockerNumber))
                {
                    _logger.LogWarning("添加柜子失败: 柜子编号不能为空");
                    return false;
                }

                // 检查柜子编号是否已存在
                var existingLocker = await _dbContext.Lockers
                    .FirstOrDefaultAsync(c => c.LockerNumber == locker.LockerNumber);
                if (existingLocker != null)
                {
                    _logger.LogWarning("添加柜子失败: 柜子编号 {LockerNumber} 已存在", locker.LockerNumber);
                    return false;
                }

                // 检查控制板地址和通道是否冲突
                if (locker.BoardAddress > 0 && locker.ChannelNumber > 0)
                {
                    var conflictLocker = await _dbContext.Lockers
                        .FirstOrDefaultAsync(c =>
                            c.BoardAddress == locker.ChannelNumber &&
                            c.ChannelNumber == locker.ChannelNumber);
                    if (conflictLocker != null)
                    {
                        _logger.LogWarning("添加柜子失败: 控制板地址 {BoardAddress} 通道 {ChannelNumber} 已被柜子 {LockerNumber} 使用",
                            locker.BoardAddress, locker.ChannelNumber, conflictLocker.LockerNumber);
                        return false;
                    }
                }

                locker.CreatedAt = DateTime.Now;
                locker.UpdatedAt = DateTime.Now;

                _dbContext.Lockers.Add(locker);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功添加柜子: LockerId={LockerId}, LockerName={LockerName}",
                    locker.LockerId, locker.LockerName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加柜子失败: LockerName={LockerName}", locker?.LockerName);
                return false;
            }
        }
        #endregion

        #region 更新柜子
        /// <summary>
        /// 更新柜子信息
        /// </summary>
        /// <param name="locker">柜子对象</param>
        /// <returns>是否更新成功</returns>
        public async Task<bool> UpdateLockerAsync(Locker locker)
        {
            try
            {
                _logger.LogInformation("开始更新柜子: LockerId={LockerId}", locker.LockerId);

                var existingLocker = await _dbContext.Lockers
                    .FirstOrDefaultAsync(c => c.LockerId == locker.LockerId);
                if (existingLocker == null)
                {
                    _logger.LogWarning("更新柜子失败: 柜子ID {LockerId} 不存在", locker.LockerId);
                    return false;
                }

                // 检查柜子编号是否与其他柜子冲突
                if (existingLocker.LockerNumber != locker.LockerNumber)
                {
                    var duplicateLocker = await _dbContext.Lockers.FirstOrDefaultAsync(c => c.LockerNumber == locker.LockerNumber && c.LockerId != locker.LockerId);
                    if (duplicateLocker != null)
                    {
                        _logger.LogWarning("更新柜子失败: 柜子编号 {LockerNumber} 已被其他柜子使用", locker.LockerNumber);
                        return false;
                    }
                }

                // 检查控制板地址和通道是否冲突
                if (locker.BoardAddress > 0 && locker.ChannelNumber > 0)
                {
                    var conflictLocker = await _dbContext.Lockers
                        .FirstOrDefaultAsync(c =>
                            c.BoardAddress == locker.BoardAddress &&
                            c.ChannelNumber == locker.ChannelNumber &&
                            c.LockerId != locker.LockerId);
                    if (conflictLocker != null)
                    {
                        _logger.LogWarning("更新柜子失败: 控制板地址 {BoardAddress} 通道 {ChannelNumber} 已被柜子 {LockerNumber} 使用",
                            locker.BoardAddress, locker.ChannelNumber, conflictLocker.LockerNumber);
                        return false;
                    }
                }

                // 更新柜子信息
                existingLocker.LockerNumber = locker.LockerNumber;
                existingLocker.LockerName = locker.LockerName;
                existingLocker.Location = locker.Location;
                existingLocker.BoardAddress = locker.BoardAddress;
                existingLocker.ChannelNumber = locker.ChannelNumber;
                existingLocker.IsAvailable = locker.IsAvailable;
                existingLocker.Remarks = locker.Remarks;
                existingLocker.UpdatedAt = DateTime.Now;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功更新柜子: LockerId={LockerId}, LockerName={LockerName}",
                    locker.LockerId, locker.LockerName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新柜子失败: LockerId={LockerId}", locker.LockerId);
                return false;
            }
        }
        #endregion

        #region 删除柜子
        /// <summary>
        /// 删除柜子
        /// </summary>
        /// <param name="lockerId">柜子ID</param>
        /// <returns>是否删除成功</returns>
        public async Task<bool> DeleteLockerAsync(long lockerId)
        {
            try
            {
                _logger.LogInformation("开始删除柜子: LockerId={LockerId}", lockerId);

                var locker = await _dbContext.Lockers
                    .FirstOrDefaultAsync(c => c.LockerId == lockerId);

                if (locker == null)
                {
                    _logger.LogWarning("删除柜子失败: 柜子ID {LockerId} 不存在", lockerId);
                    return false;
                }

                // 检查是否有用户分配了该柜子
                var userLockers = await _dbContext.Users
                    .Where(u => u.UserLockers.Count > 0 && u.UserLockers.Select(o => o.LockerId).Contains(lockerId))
                    .ToListAsync();

                if (userLockers.Any())
                {
                    var userNames = string.Join(", ", userLockers.Select(u => u.Name));
                    _logger.LogWarning("删除柜子失败: 以下用户仍被分配了该柜子: {UserNames}", userNames);
                    return false;
                }

                _dbContext.Lockers.Remove(locker);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("成功删除柜子: LockerId={LockerId}, LockerName={LockerName}",
                    lockerId, locker.LockerName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除柜子失败: LockerId={LockerId}", lockerId);
                return false;
            }
        }
        #endregion

        #region 获取柜子
        /// <summary>
        /// 获取柜子
        /// </summary>
        /// <param name="lockerId"></param>
        /// <returns></returns>
        public async Task<Locker?> GetLockerAsync(long lockerId)
        {
            try
            {
                _logger.LogDebug("开始获取柜子信息: LockerId={LockerId}", lockerId);

                var locker = await _dbContext.Lockers.FirstOrDefaultAsync(c => c.LockerId == lockerId);

                if (locker == null)
                {
                    _logger.LogWarning("未找到柜子: LockerId={LockerId}", lockerId);
                    return null;
                }
                return locker;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取柜子信息时发生异常: LockerId={LockerId}", lockerId);
                return null;
            }
        }
        #endregion

        #region 根据板号和通道号获取柜子
        /// <summary>
        /// 根据板号和通道号获取柜子
        /// </summary>
        /// <param name="boardAddress">板地址</param>
        /// <param name="channelNumber">通道号</param>
        /// <returns>柜子对象</returns>
        public async Task<Locker?> GetLockerByAddressAndChannelAsync(int boardAddress, int channelNumber)
        {
            try
            {
                _logger.LogDebug("根据板号和通道号获取柜子: BoardAddress={BoardAddress}, ChannelNumber={ChannelNumber}",
                    boardAddress, channelNumber);

                var locker = await _dbContext.Lockers.FirstOrDefaultAsync(o => o.BoardAddress == boardAddress && o.ChannelNumber == channelNumber);

                if (locker == null)
                {
                    _logger.LogDebug("未找到柜子: BoardAddress={BoardAddress}, ChannelNumber={ChannelNumber}",
                        boardAddress, channelNumber);
                }

                return locker;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取柜子失败: BoardAddress={BoardAddress}, ChannelNumber={ChannelNumber}",
                    boardAddress, channelNumber);
                return null;
            }
        }
        #endregion

        #region 获取柜子列表
        /// <summary>
        /// 获取柜子列表
        /// </summary>
        /// <returns>柜子列表</returns>
        public async Task<List<Locker>> GetLockersAsync()
        {
            try
            {
                _logger.LogDebug("从数据库开始获取柜子列表");

                var lockers = await _dbContext.Lockers
                    .OrderBy(c => c.LockerNumber)
                    .ToListAsync();

                _logger.LogDebug("从数据库成功获取柜子列表: 共 {Count} 个柜子", lockers.Count);
                return lockers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从数据库获取柜子列表失败");
                return new List<Locker>();
            }
        }
        #endregion

        #region 获取一个可用柜格
        /// <summary>
        /// 获取一个可用柜格
        /// </summary>
        /// <returns></returns>
        public async Task<Locker?> GetAvailableLockerAsync()
        {
            //获取可用柜格Id去重列表，
            var lockerIds = _dbContext.UserLockers.Where(o => (o.AssignmentStatus == AssignmentStatus.Assigned) && o.AssignedAt < DateTime.Now.AddMinutes(1)).Select(o => o.LockerId).Distinct().ToList();

            //关联可用柜格Id获取可用柜格
            var locker = await _dbContext.Lockers.FirstOrDefaultAsync(o => !lockerIds.Contains(o.LockerId) && o.IsAvailable);


            return locker;
        }


        #endregion

        #region 分配柜格给用户
        /// <summary>
        /// 分配柜格给用户
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="lockerId"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public Task<(bool, UserLocker)> AssignLockerToUserAsync(long userId, long lockerId)
        {
            if (userId == 0 || lockerId == 0)
            {
                _logger.LogWarning("分配柜格给用户失败: 用户ID {UserId} 柜子ID {LockerId} 为空", userId, lockerId);
                return Task.FromResult((false, new UserLocker()));
            }
            try
            {
                var userLocker = new UserLocker
                {
                    UserId = userId,
                    LockerId = lockerId,
                    AssignmentStatus = AssignmentStatus.Assigned,
                    StorageStatus = StorageStatus.Unused,
                    AssignedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddDays(30),
                    StoredTime = DateTime.Now,
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                };
                _dbContext.UserLockers.Add(userLocker);
                _dbContext.SaveChanges();
                _logger.LogInformation("成功分配柜格给用户: UserId={UserId}, LockerId={LockerId}", userId, lockerId);
                return Task.FromResult((true, userLocker));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分配柜格给用户失败: UserId={UserId}, LockerId={LockerId}", userId, lockerId);
                return Task.FromResult((false, new UserLocker()));
            }
        }
        #endregion

        #region 获取用户已分配的柜格
        /// <summary>
        /// 获取用户已分配的柜格
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<List<UserLocker>> GetUserLockersAsync(long userId)
        {
            var userLockers = new List<UserLocker>();
            try
            {
                _logger.LogDebug("获取用户已分配的柜格: UserId={UserId}", userId);

                userLockers = await _dbContext.UserLockers.Where(o => o.UserId == userId && o.AssignmentStatus == AssignmentStatus.Assigned && o.ExpiresAt > DateTime.Now).ToListAsync();

                _logger.LogDebug("成功获取用户已分配的柜格: 共 {Count} 个柜子", userLockers.Count);
                return userLockers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户已分配的柜格失败: UserId={UserId}", userId);
                return userLockers;
            }
        }
        #endregion

        #region 更新用户已分配的柜格的状态
        /// <summary>
        /// 更新用户已分配的柜格状态
        /// </summary>
        /// <param name="userLockerId"></param>
        /// <param name="storageStatus"></param>
        /// <returns></returns>
        public async Task<bool> UpdateUserLockerStatusAsync(long userLockerId, StorageStatus storageStatus)
        {
            try
            {
                _logger.LogDebug("更新用户已分配的柜格状态: UserLockerId={userLockerId}, StorageStatus={StorageStatus}", userLockerId, storageStatus);

                var userLocker = await _dbContext.UserLockers.FirstOrDefaultAsync(o => o.UserLockerId == userLockerId);
                if (userLocker == null)
                {
                    _logger.LogWarning("未找到用户已分配的柜格: UserLockerId={userLockerId}", userLockerId);
                    return false;
                }

                if (storageStatus == StorageStatus.Stored)
                {
                    userLocker.AssignmentStatus = AssignmentStatus.Assigned;
                    userLocker.StorageStatus = StorageStatus.Stored;
                    userLocker.StoredTime = DateTime.Now;
                }
                if (storageStatus == StorageStatus.Retrieved)
                {
                    userLocker.AssignmentStatus = AssignmentStatus.Released;
                    userLocker.StorageStatus = StorageStatus.Retrieved;
                    userLocker.RetrievedTime = DateTime.Now;
                }

                _dbContext.UserLockers.Update(userLocker);
                await _dbContext.SaveChangesAsync();
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户已分配的柜格状态失败: UserLockerId={userLockerId}, StorageStatus={StorageStatus}", userLockerId, storageStatus);
                return false;
            }
        }
        #endregion
    }
}