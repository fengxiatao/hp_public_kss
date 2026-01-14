using FaceLocker.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 访问日志服务接口
    /// 提供访问日志的记录、查询、统计和数据同步等功能
    /// </summary>
    public interface IAccessLogService
    {
        #region 公共属性
        /// <summary>
        /// 获取访问日志的查询接口（用于直接执行LINQ查询）
        /// </summary>
        IQueryable<AccessLog> AccessLogs { get; }
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
        Task<bool> LogAccessAsync(long userId, string userName, long lockerId, string lockerName, AccessAction action, AccessResult result, string? details = null);

        /// <summary>
        /// 记录访问日志
        /// </summary>
        /// <param name="accessLog">访问日志对象</param>
        /// <returns>是否记录成功</returns>
        Task<bool> LogAccessAsync(AccessLog accessLog);
        #endregion

        #region 日志查询方法
        /// <summary>
        /// 根据ID获取访问日志
        /// </summary>
        /// <param name="id">日志ID</param>
        /// <returns>访问日志对象</returns>
        Task<AccessLog?> GetAccessLogByIdAsync(string id);

        /// <summary>
        /// 获取所有访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        Task<List<AccessLog>> GetAllAccessLogsAsync();

        /// <summary>
        /// 分页查询访问日志
        /// </summary>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="userId">用户ID筛选</param>
        /// <param name="keywords">关键字</param>
        /// <param name="action">操作类型筛选</param>
        /// <param name="result">操作结果筛选</param>
        /// <param name="startDate">开始日期筛选</param>
        /// <param name="endDate">结束日期筛选</param>
        /// <returns>访问日志列表和总数</returns>
        Task<(List<AccessLog> AccessLogs, int TotalCount)> GetAccessLogsPagedAsync(int pageIndex, int pageSize, long userId = 0, string keywords = null, AccessAction? action = null, AccessResult? result = null, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 获取今日访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        Task<List<AccessLog>> GetTodayAccessLogsAsync();

        /// <summary>
        /// 获取待上传的访问日志
        /// </summary>
        /// <returns>访问日志列表</returns>
        Task<List<AccessLog>> GetPendingUploadAccessLogsAsync();
        #endregion

        #region 日志管理方法
        /// <summary>
        /// 标记日志为已上传
        /// </summary>
        /// <param name="logId">日志ID</param>
        /// <returns>是否标记成功</returns>
        Task<bool> MarkAsUploadedAsync(string logId);

        /// <summary>
        /// 批量标记日志为已上传
        /// </summary>
        /// <param name="logIds">日志ID列表</param>
        /// <returns>是否标记成功</returns>
        Task<bool> MarkAsUploadedAsync(List<string> logIds);

        /// <summary>
        /// 清理过期的访问日志
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>清理的日志数量</returns>
        Task<int> CleanupExpiredAccessLogsAsync(int daysToKeep);
        #endregion

        #region 统计分析方法
        /// <summary>
        /// 获取最活跃用户
        /// </summary>
        /// <param name="topCount">前N个</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>用户ID和访问次数字典</returns>
        Task<Dictionary<string, int>> GetMostActiveUsersAsync(int topCount = 10, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 获取最常用柜子
        /// </summary>
        /// <param name="topCount">前N个</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>柜子ID和访问次数字典</returns>
        Task<Dictionary<string, int>> GetMostUsedLockersAsync(int topCount = 10, DateTime? startDate = null, DateTime? endDate = null);
        #endregion
    }
}
