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
    /// 访问日志服务实现
    /// 提供访问日志的记录、查询、统计和数据同步等功能
    /// </summary>
    public class AccessLogService : IAccessLogService
    {
        #region 私有字段
        private readonly FaceLockerDbContext _context;
        private readonly ILogger<AccessLogService> _logger;
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        public AccessLogService(FaceLockerDbContext context, ILogger<AccessLogService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region 公共属性
        /// <summary>
        /// 获取访问日志的查询接口（用于直接执行LINQ查询）
        /// </summary>
        public IQueryable<AccessLog> AccessLogs => _context.AccessLogs.AsQueryable();
        #endregion

        #region 日志记录方法
        /// <summary>
        /// 创建并记录自定义访问日志
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="userName">用户姓名</param>
        /// <param name="lockerId">柜子ID</param>
        /// <param name="lockerName">柜子名称</param>
        /// <param name="action">操作类型</param>
        /// <param name="result">操作结果</param>
        /// <param name="details">详细信息</param>
        /// <returns>是否记录成功</returns>
        public async Task<bool> LogAccessAsync(long userId, string userName, long lockerId, string lockerName, AccessAction action, AccessResult result, string? details = null)
        {
            try
            {
                _logger.LogDebug("开始记录自定义访问日志: UserId={UserId}, UserName={UserName}, LockerId={LockerId}, LockerName={LockerName}, Action={Action}, Result={Result}, Details={Details}",
                    userId, userName, lockerId, lockerName, action, result, details ?? "空");

                var accessLog = new AccessLog
                {
                    UserId = userId,
                    UserName = userName,
                    LockerId = lockerId,
                    LockerName = lockerName,
                    Action = action,
                    Result = result,
                    Details = details,
                    Timestamp = DateTime.Now,
                    IsUploaded = false
                };

                return await LogAccessAsync(accessLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录自定义访问日志失败: UserId={UserId}, Action={Action}, Result={Result}", userId, action, result);
                return false;
            }
        }

        /// <summary>
        /// 记录访问日志
        /// </summary>
        /// <param name="accessLog">访问日志对象</param>
        /// <returns>是否记录成功</returns>
        public async Task<bool> LogAccessAsync(AccessLog accessLog)
        {
            try
            {
                if (accessLog == null)
                {
                    _logger.LogWarning("记录访问日志失败: 访问日志对象为空");
                    return false;
                }

                _logger.LogDebug("开始记录访问日志: Summary={Summary}, UserId={UserId}, LockerId={LockerId}, Action={Action}, Result={Result}", accessLog.Summary, accessLog.UserId, accessLog.LockerId, accessLog.Action, accessLog.Result);

                _context.AccessLogs.Add(accessLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功记录访问日志: ID={LogId}, Summary={Summary}", accessLog.Id, accessLog.Summary);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录访问日志失败: Summary={Summary}", accessLog?.Summary);
                return false;
            }
        }
        #endregion

        #region 日志查询方法
        /// <summary>
        /// 根据ID获取访问日志
        /// </summary>
        /// <param name="id">日志ID</param>
        /// <returns>访问日志对象</returns>
        public async Task<AccessLog?> GetAccessLogByIdAsync(string id)
        {
            try
            {
                _logger.LogDebug("根据ID获取访问日志: ID={LogId}", id);

                if (!long.TryParse(id, out long parsedId))
                {
                    _logger.LogWarning("获取访问日志失败: 日志ID格式无效 ID={LogId}", id);
                    return null;
                }

                var log = await _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .FirstOrDefaultAsync(log => log.Id == parsedId);

                if (log == null)
                {
                    _logger.LogWarning("未找到访问日志: ID={LogId}", id);
                }
                else
                {
                    _logger.LogDebug("成功获取访问日志: ID={LogId}, Summary={Summary}", log.Id, log.Summary);
                }

                return log;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取访问日志失败: ID={LogId}", id);
                return null;
            }
        }

        /// <summary>
        /// 获取所有访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        public async Task<List<AccessLog>> GetAllAccessLogsAsync()
        {
            try
            {
                _logger.LogDebug("开始获取所有访问日志");

                var logs = await _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .OrderByDescending(log => log.Timestamp)
                    .ToListAsync();

                _logger.LogInformation("成功获取所有访问日志，共 {Count} 条记录", logs.Count);
                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有访问日志失败");
                return new List<AccessLog>();
            }
        }

        /// <summary>
        /// 分页查询访问日志
        /// </summary>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="userId">用户ID筛选</param>
        /// <param name="lockerId">柜子ID筛选</param>
        /// <param name="action">操作类型筛选</param>
        /// <param name="result">操作结果筛选</param>
        /// <param name="startDate">开始日期筛选</param>
        /// <param name="endDate">结束日期筛选</param>
        /// <returns>访问日志列表和总数</returns>
        public async Task<(List<AccessLog> AccessLogs, int TotalCount)> GetAccessLogsPagedAsync(int pageIndex, int pageSize, long userId = 0, string keywords = null, AccessAction? action = null, AccessResult? result = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("开始分页查询访问日志: PageIndex={PageIndex}, PageSize={PageSize}, UserId={UserId}, keywords={keywords}, Action={Action}, Result={Result}, StartDate={StartDate}, EndDate={EndDate}",
                    pageIndex, pageSize, userId, keywords, action?.ToString() ?? "空", result?.ToString() ?? "空", startDate?.ToString("yyyy-MM-dd") ?? "空", endDate?.ToString("yyyy-MM-dd") ?? "空");

                var query = _context.AccessLogs.AsQueryable();

                if (userId > 0)
                {
                    query = query.Where(log => log.UserId == userId);
                }

                if (!string.IsNullOrWhiteSpace(keywords))
                {
                    query = query.Where(log => log.LockerName.Contains(keywords));
                }

                if (action.HasValue)
                {
                    query = query.Where(log => log.Action == action.Value);
                }

                if (result.HasValue)
                {
                    query = query.Where(log => log.Result == result.Value);
                }

                if (startDate.HasValue)
                {
                    var startDateTime = startDate.Value.Date;
                    query = query.Where(log => log.Timestamp >= startDate.Value);
                    _logger.LogDebug("应用开始日期筛选: StartDate={StartDate}", startDate.Value.ToString("yyyy-MM-dd"));
                }

                if (endDate.HasValue)
                {
                    var endDateTime = endDate.Value.Date.AddDays(1).AddSeconds(-1);
                    query = query.Where(log => log.Timestamp <= endDate.Value);
                    _logger.LogDebug("应用结束日期筛选: EndDate={EndDate}", endDate.Value.ToString("yyyy-MM-dd"));
                }

                var totalCount = await query.CountAsync();
                _logger.LogDebug("分页查询总记录数: {TotalCount}", totalCount);

                // 计算跳过的记录数
                var skipCount = (pageIndex - 1) * pageSize;

                // 获取分页数据（不包含 Include，手动加载导航属性）
                var logs = await query
                    .OrderByDescending(log => log.Timestamp)
                    .Skip(skipCount)
                    .Take(pageSize)
                    .ToListAsync();

                // 手动加载导航属性
                await LoadNavigationPropertiesAsync(logs);


                return (logs, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页查询访问日志失败");
                return (new List<AccessLog>(), 0);
            }
        }

        /// <summary>
        /// 手动加载导航属性
        /// </summary>
        private async Task LoadNavigationPropertiesAsync(List<AccessLog> logs)
        {
            if (logs == null || logs.Count == 0)
                return;

            try
            {
                // 获取所有用户ID
                var userIds = logs.Where(log => log.UserId > 0).Select(log => log.UserId).Distinct().ToList();
                var users = userIds.Count > 0
                    ? await _context.Users.Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(u => u.Id, u => u)
                    : [];

                // 获取所有柜子ID
                var lockerIds = logs.Where(log => log.LockerId > 0).Select(log => log.LockerId).Distinct().ToList();
                var lockers = lockerIds.Count > 0
                    ? await _context.Lockers.Where(locker => lockerIds.Contains(locker.LockerId)).ToDictionaryAsync(l => l.LockerId, l => l)
                    : [];

                // 设置导航属性
                foreach (var log in logs)
                {
                    if (log.UserId > 0 && users.TryGetValue(log.UserId, out var user))
                    {
                        log.User = user;
                    }

                    if (log.LockerId > 0 && lockers.TryGetValue(log.LockerId, out var locker))
                    {
                        log.Locker = locker;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载导航属性时发生异常");
                // 这里不抛出异常，继续执行
            }
        }

        /// <summary>
        /// 获取用户的访问日志
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="limit">限制数量</param>
        /// <returns>访问日志列表</returns>
        public async Task<List<AccessLog>> GetUserAccessLogsAsync(string userId, int? limit = null)
        {
            try
            {
                _logger.LogDebug("开始获取用户访问日志: UserId={UserId}, Limit={Limit}", userId, limit?.ToString() ?? "空");

                if (!long.TryParse(userId, out long parsedUserId))
                {
                    _logger.LogWarning("获取用户访问日志失败: 用户ID格式无效 UserId={UserId}", userId);
                    return new List<AccessLog>();
                }

                IQueryable<AccessLog> query = _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .Where(log => log.UserId == parsedUserId)
                    .OrderByDescending(log => log.Timestamp);

                if (limit.HasValue)
                {
                    query = query.Take(limit.Value);
                    _logger.LogDebug("应用数量限制: Limit={Limit}", limit.Value);
                }

                var logs = await query.ToListAsync();
                _logger.LogInformation("成功获取用户访问日志: UserId={UserId}, 返回 {Count} 条记录", userId, logs.Count);
                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户访问日志失败: UserId={UserId}", userId);
                return new List<AccessLog>();
            }
        }

        /// <summary>
        /// 获取柜子的访问日志
        /// </summary>
        /// <param name="lockerId">柜子ID</param>
        /// <param name="limit">限制数量</param>
        /// <returns>访问日志列表</returns>
        public async Task<List<AccessLog>> GetLockerAccessLogsAsync(string lockerId, int? limit = null)
        {
            try
            {
                _logger.LogDebug("开始获取柜子访问日志: LockerId={LockerId}, Limit={Limit}", lockerId, limit?.ToString() ?? "空");

                if (!long.TryParse(lockerId, out long parsedLockerId))
                {
                    _logger.LogWarning("获取柜子访问日志失败: 柜子ID格式无效 LockerId={LockerId}", lockerId);
                    return new List<AccessLog>();
                }

                IQueryable<AccessLog> query = _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .Where(log => log.LockerId == parsedLockerId)
                    .OrderByDescending(log => log.Timestamp);

                if (limit.HasValue)
                {
                    query = query.Take(limit.Value);
                    _logger.LogDebug("应用数量限制: Limit={Limit}", limit.Value);
                }

                var logs = await query.ToListAsync();
                _logger.LogInformation("成功获取柜子访问日志: LockerId={LockerId}, 返回 {Count} 条记录", lockerId, logs.Count);
                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取柜子访问日志失败: lockerId={LockerId}", lockerId);
                return new List<AccessLog>();
            }
        }

        /// <summary>
        /// 获取今日访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        public async Task<List<AccessLog>> GetTodayAccessLogsAsync()
        {
            try
            {
                _logger.LogDebug("开始获取今日访问日志");

                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);

                var logs = await _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .Where(log => log.Timestamp >= today && log.Timestamp < tomorrow)
                    .OrderByDescending(log => log.Timestamp)
                    .ToListAsync();

                _logger.LogInformation("成功获取今日访问日志，共 {Count} 条记录", logs.Count);
                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取今日访问日志失败");
                return new List<AccessLog>();
            }
        }

        /// <summary>
        /// 获取待上传的访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        public async Task<List<AccessLog>> GetPendingUploadAccessLogsAsync()
        {
            try
            {
                _logger.LogDebug("开始获取待上传访问日志");

                var logs = await _context.AccessLogs
                    .Include(log => log.User)
                    .Include(log => log.Locker)
                    .Where(log => !log.IsUploaded)
                    .OrderBy(log => log.Timestamp)
                    .ToListAsync();

                _logger.LogInformation("成功获取待上传访问日志，共 {Count} 条记录", logs.Count);
                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取待上传访问日志失败");
                return new List<AccessLog>();
            }
        }
        #endregion

        #region 日志管理方法
        /// <summary>
        /// 标记日志为已上传
        /// </summary>
        /// <param name="logId">日志ID</param>
        /// <returns>是否标记成功</returns>
        public async Task<bool> MarkAsUploadedAsync(string logId)
        {
            try
            {
                _logger.LogDebug("开始标记日志为已上传: LogId={LogId}", logId);

                if (!long.TryParse(logId, out long parsedLogId))
                {
                    _logger.LogWarning("标记日志失败: 日志ID格式无效 LogId={LogId}", logId);
                    return false;
                }

                var log = await _context.AccessLogs.FindAsync(parsedLogId);
                if (log == null)
                {
                    _logger.LogWarning("标记日志失败: 未找到日志 ID={LogId}", logId);
                    return false;
                }

                log.MarkAsUploaded();
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功标记访问日志为已上传: ID={LogId}, UploadedAt={UploadedAt}", logId, log.UploadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "空");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "标记访问日志为已上传失败: ID={LogId}", logId);
                return false;
            }
        }

        /// <summary>
        /// 批量标记日志为已上传
        /// </summary>
        /// <param name="logIds">日志ID列表</param>
        /// <returns>是否标记成功</returns>
        public async Task<bool> MarkAsUploadedAsync(List<string> logIds)
        {
            try
            {
                _logger.LogDebug("开始批量标记日志为已上传: 共 {Count} 条日志", logIds.Count);

                if (logIds == null || !logIds.Any())
                {
                    _logger.LogWarning("批量标记日志失败: 日志ID列表为空");
                    return false;
                }

                var parsedLogIds = new List<long>();
                foreach (var logId in logIds)
                {
                    if (long.TryParse(logId, out long parsedLogId))
                    {
                        parsedLogIds.Add(parsedLogId);
                    }
                    else
                    {
                        _logger.LogWarning("批量标记日志失败: 日志ID格式无效 LogId={LogId}", logId);
                        return false;
                    }
                }

                var logs = await _context.AccessLogs
                    .Where(log => parsedLogIds.Contains(log.Id))
                    .ToListAsync();

                if (!logs.Any())
                {
                    _logger.LogWarning("批量标记日志失败: 未找到指定的日志");
                    return false;
                }

                foreach (var log in logs)
                {
                    log.MarkAsUploaded();
                    _logger.LogDebug("标记日志为已上传: LogId={LogId}", log.Id);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("成功批量标记 {Count} 条访问日志为已上传", logs.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量标记访问日志为已上传失败");
                return false;
            }
        }

        /// <summary>
        /// 清理过期的访问日志
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>清理的日志数量</returns>
        public async Task<int> CleanupExpiredAccessLogsAsync(int daysToKeep)
        {
            try
            {
                _logger.LogDebug("开始清理过期访问日志: DaysToKeep={DaysToKeep}", daysToKeep);

                if (daysToKeep <= 0)
                {
                    _logger.LogWarning("清理过期日志失败: 保留天数必须大于0");
                    return 0;
                }

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);

                var expiredLogs = await _context.AccessLogs
                    .Where(log => log.Timestamp < cutoffDate && log.IsUploaded)
                    .ToListAsync();

                if (expiredLogs.Any())
                {
                    _context.AccessLogs.RemoveRange(expiredLogs);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("成功清理 {Count} 条过期访问日志", expiredLogs.Count);
                    return expiredLogs.Count;
                }

                _logger.LogDebug("没有找到需要清理的过期访问日志");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期访问日志失败");
                return 0;
            }
        }
        #endregion

        #region 统计分析方法
        /// <summary>
        /// 获取用户访问统计
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>统计信息字典</returns>
        public async Task<Dictionary<string, int>> GetUserAccessStatisticsAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("开始获取用户访问统计: UserId={UserId}, StartDate={StartDate}, EndDate={EndDate}",
                    userId, startDate?.ToString("yyyy-MM-dd") ?? "空", endDate?.ToString("yyyy-MM-dd") ?? "空");

                if (!long.TryParse(userId, out long parsedUserId))
                {
                    _logger.LogWarning("获取用户访问统计失败: 用户ID格式无效 UserId={UserId}", userId);
                    return new Dictionary<string, int>();
                }

                var query = _context.AccessLogs.Where(log => log.UserId == parsedUserId);

                if (startDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp <= endDate.Value);
                }

                var stats = new Dictionary<string, int>
                {
                    ["Total"] = await query.CountAsync(),
                    ["Success"] = await query.CountAsync(log => log.Result == AccessResult.Success),
                    ["Failed"] = await query.CountAsync(log => log.Result == AccessResult.Failed),
                    ["Denied"] = await query.CountAsync(log => log.Result == AccessResult.Denied),
                    ["Store"] = await query.CountAsync(log => log.Action == AccessAction.Store),
                    ["Retrieve"] = await query.CountAsync(log => log.Action == AccessAction.Rerieve)
                };

                _logger.LogInformation("成功获取用户访问统计: UserId={UserId}, 总计 {Total} 条记录", userId, stats["Total"]);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户访问统计失败: UserId={UserId}", userId);
                return new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// 获取柜子访问统计
        /// </summary>
        /// <param name="lockerId">柜子ID</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>统计信息字典</returns>
        public async Task<Dictionary<string, int>> GetLockerAccessStatisticsAsync(string lockerId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("开始获取柜子访问统计: LockerId={LockerId}, StartDate={StartDate}, EndDate={EndDate}",
                    lockerId, startDate?.ToString("yyyy-MM-dd") ?? "空", endDate?.ToString("yyyy-MM-dd") ?? "空");

                if (!long.TryParse(lockerId, out long parsedLockerId))
                {
                    _logger.LogWarning("获取柜子访问统计失败: 柜子ID格式无效 LockerId={LockerId}", lockerId);
                    return new Dictionary<string, int>();
                }

                var query = _context.AccessLogs.Where(log => log.LockerId == parsedLockerId);

                if (startDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp <= endDate.Value);
                }

                var stats = new Dictionary<string, int>
                {
                    ["Total"] = await query.CountAsync(),
                    ["Success"] = await query.CountAsync(log => log.Result == AccessResult.Success),
                    ["Failed"] = await query.CountAsync(log => log.Result == AccessResult.Failed),
                    ["Denied"] = await query.CountAsync(log => log.Result == AccessResult.Denied),
                    ["Store"] = await query.CountAsync(log => log.Action == AccessAction.Store),
                    ["Retrieve"] = await query.CountAsync(log => log.Action == AccessAction.Rerieve)
                };

                _logger.LogInformation("成功获取柜子访问统计: LockerId={LockerId}, 总计 {Total} 条记录", lockerId, stats["Total"]);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取柜子访问统计失败: lockerId={LockerId}", lockerId);
                return new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// 获取最活跃用户
        /// </summary>
        /// <param name="topCount">前N个</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>用户ID和访问次数字典</returns>
        public async Task<Dictionary<string, int>> GetMostActiveUsersAsync(int topCount = 10, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("开始获取最活跃用户: TopCount={TopCount}, StartDate={StartDate}, EndDate={EndDate}",
                    topCount, startDate?.ToString("yyyy-MM-dd") ?? "空", endDate?.ToString("yyyy-MM-dd") ?? "空");

                var query = _context.AccessLogs.AsQueryable();

                if (startDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp <= endDate.Value);
                }

                var activeUsers = await query
                    .GroupBy(log => log.UserId)
                    .Select(g => new { UserId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(topCount)
                    .ToDictionaryAsync(x => x.UserId.ToString(), x => x.Count);

                _logger.LogInformation("成功获取最活跃用户: 返回 {Count} 个用户", activeUsers.Count);
                return activeUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最活跃用户失败");
                return new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// 获取最常用柜子
        /// </summary>
        /// <param name="topCount">前N个</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>柜子ID和使用次数字典</returns>
        public async Task<Dictionary<string, int>> GetMostUsedLockersAsync(int topCount = 10, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("开始获取最常用柜子: TopCount={TopCount}, StartDate={StartDate}, EndDate={EndDate}",
                    topCount, startDate?.ToString("yyyy-MM-dd") ?? "空", endDate?.ToString("yyyy-MM-dd") ?? "空");

                var query = _context.AccessLogs.AsQueryable();

                if (startDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(log => log.Timestamp <= endDate.Value);
                }

                var usedLockers = await query
                    .GroupBy(log => log.LockerId)
                    .Select(g => new { lockerId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(topCount)
                    .ToDictionaryAsync(x => x.lockerId.ToString(), x => x.Count);

                _logger.LogInformation("成功获取最常用柜子: 返回 {Count} 个柜子", usedLockers.Count);
                return usedLockers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最常用柜子失败");
                return new Dictionary<string, int>();
            }
        }
        #endregion
    }
}