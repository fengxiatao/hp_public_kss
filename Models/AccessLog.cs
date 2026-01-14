using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FaceLocker.Models
{
    /// <summary>
    /// 开锁日志实体模型
    /// </summary>
    [Table("AccessLogs")]
    public class AccessLog
    {
        #region 主键和标识属性

        /// <summary>
        /// 日志ID（主键）- 自增长长整型，唯一标识日志记录
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        #endregion

        #region 用户相关信息
        /// <summary>
        /// 用户ID（外键）
        /// </summary>
        [Required]
        [Column("UserId")]
        public long UserId { get; set; }

        /// <summary>
        /// 用户姓名（冗余存储，防止用户删除后日志丢失信息）
        /// </summary>
        [Required]
        [StringLength(100)]
        [Column("UserName")]
        public string UserName { get; set; } = "";

        #endregion

        #region 储物格相关信息

        /// <summary>
        /// 柜子ID
        /// </summary>
        [Required]
        [Column("LockerId")]
        public long LockerId { get; set; }

        /// <summary>
        /// 柜子名称（冗余存储，防止柜子删除后日志丢失信息）
        /// </summary>
        [Required]
        [StringLength(100)]
        [Column("LockerName")]
        public string LockerName { get; set; } = string.Empty;

        #endregion

        #region 操作信息
        /// <summary>
        /// 操作类型
        /// </summary>
        [Required]
        [Column("Action")]
        public AccessAction Action { get; set; }

        /// <summary>
        /// 操作结果
        /// </summary>
        [Required]
        [Column("Result")]
        public AccessResult Result { get; set; }

        /// <summary>
        /// 详细信息
        /// </summary>
        [StringLength(500)]
        [Column("Details")]
        public string? Details { get; set; }

        /// <summary>
        /// 操作时间
        /// </summary>
        [Required]
        [Column("Timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        #endregion

        #region 上传状态

        /// <summary>
        /// 是否已上传到服务器
        /// </summary>
        [Required]
        [Column("IsUploaded")]
        public bool IsUploaded { get; set; } = false;

        /// <summary>
        /// 上传时间
        /// </summary>
        [Column("UploadedAt")]
        public DateTime? UploadedAt { get; set; }

        #endregion

        #region 导航属性

        /// <summary>
        /// 导航属性：关联的用户
        /// </summary>
        [NotMapped]
        public virtual User? User { get; set; }

        /// <summary>
        /// 导航属性：关联的柜子
        /// </summary>
        [NotMapped]
        public virtual Locker? Locker { get; set; }

        #endregion

        #region NotMapped 计算属性

        /// <summary>
        /// 获取操作类型显示文本
        /// </summary>
        [NotMapped]
        public string ActionText => GetActionDisplayName(Action);

        /// <summary>
        /// 获取操作结果显示文本
        /// </summary>
        [NotMapped]
        public string ResultText => GetResultDisplayName(Result);

        /// <summary>
        /// 获取上传状态文本
        /// </summary>
        [NotMapped]
        public string UploadStatusText => IsUploaded ? "已上传" : "待上传";

        /// <summary>
        /// 检查是否需要上传
        /// </summary>
        [NotMapped]
        public bool NeedsUpload => !IsUploaded;

        /// <summary>
        /// 获取格式化的时间戳
        /// </summary>
        [NotMapped]
        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 获取操作摘要
        /// </summary>
        [NotMapped]
        public string Summary => $"{UserName} {ActionText} {LockerName} - {ResultText}";

        [NotMapped]
        public int RowIndex { get; set; }

        #endregion

        #region 辅助方法
        /// <summary>
        /// 获取操作类型显示名称
        /// </summary>
        /// <param name="action">操作类型</param>
        /// <returns>显示名称</returns>
        public static string GetActionDisplayName(AccessAction action)
        {
            return action switch
            {
                AccessAction.Default => "未知",
                AccessAction.Store => "存入",
                AccessAction.Rerieve => "取出",
                AccessAction.AdminOpenAll => "管理员清柜",
                AccessAction.AdminOpenLocker => "管理员打开",
                _ => "未知操作"
            };
        }

        /// <summary>
        /// 获取操作结果显示名称
        /// </summary>
        /// <param name="result">操作结果</param>
        /// <returns>显示名称</returns>
        public static string GetResultDisplayName(AccessResult result)
        {
            return result switch
            {
                AccessResult.Success => "成功",
                AccessResult.Failed => "失败",
                AccessResult.Denied => "拒绝",
                _ => "未知"
            };
        }

        /// <summary>
        /// 标记为已上传
        /// </summary>
        public void MarkAsUploaded()
        {
            IsUploaded = true;
            UploadedAt = DateTime.Now;
        }

        /// <summary>
        /// 检查日志是否过期（用于清理）
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>是否过期</returns>
        public bool IsExpired(int daysToKeep)
        {
            if (daysToKeep <= 0)
                return false;

            return (DateTime.Now - Timestamp).TotalDays > daysToKeep;
        }

        /// <summary>
        /// 检查是否可以清理（已上传且过期）
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>是否可以清理</returns>
        public bool CanBeCleanedUp(int daysToKeep)
        {
            return IsUploaded && IsExpired(daysToKeep);
        }

        /// <summary>
        /// 更新详细信息
        /// </summary>
        /// <param name="details">详细信息</param>
        public void UpdateDetails(string? details)
        {
            Details = details;
        }

        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>日志字符串表示</returns>
        public override string ToString()
        {
            return $"AccessLog[{Id}]: {Summary} - {FormattedTimestamp} ({UploadStatusText})";
        }

        #endregion
    }

    #region 操作类型枚举
    /// <summary>
    /// 操作类型枚举
    /// </summary>
    public enum AccessAction
    {
        /// <summary>
        /// 默认
        /// </summary>
        Default = 0,
        /// <summary>
        /// 存入
        /// </summary>
        Store = 1,
        /// <summary>
        /// 取出
        /// </summary>
        Rerieve = 2,
        /// <summary>
        /// 管理员清柜
        /// </summary>
        AdminOpenAll = 3,
        /// <summary>
        /// 管理员打开
        /// </summary>
        AdminOpenLocker = 4,
    }
    #endregion

    #region 访问结果枚举
    /// <summary>
    /// 访问结果枚举
    /// </summary>
    public enum AccessResult
    {
        /// <summary>
        /// 成功
        /// </summary>
        Success = 1,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 2,

        /// <summary>
        /// 拒绝访问
        /// </summary>
        Denied = 3
    }
    #endregion
}