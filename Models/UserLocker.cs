using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FaceLocker.Models;

/// <summary>
/// 用户储物格分配实体类
/// </summary>
[Table("UserLockers")]
public class UserLocker
{
    #region 主键和标识属性

    /// <summary>
    /// 分配ID（主键）- 自增长长整型，唯一标识分配记录
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("UserLockerId")]
    public long UserLockerId { get; set; }

    #endregion

    #region 关联属性

    /// <summary>
    /// 用户ID（外键）
    /// </summary>
    [Required]
    [Column("UserId")]
    public long UserId { get; set; }

    /// <summary>
    /// 储物格ID（外键）
    /// </summary>
    [Required]
    [Column("LockerId")]
    public long LockerId { get; set; }

    #endregion

    #region 时间属性

    /// <summary>
    /// 分配时间
    /// </summary>
    [Column("AssignedAt")]
    public DateTime AssignedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 过期时间
    /// </summary>
    [Column("ExpiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Required]
    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 存放时间
    /// </summary>
    [Column("StoredTime")]
    public DateTime StoredTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 取出时间
    /// </summary>
    [Column("RetrievedTime")]
    public DateTime? RetrievedTime { get; set; }

    #endregion

    #region 状态属性

    /// <summary>
    /// 是否激活
    /// </summary>
    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 储物格使用状态
    /// </summary>
    [Column("StorageStatus")]
    public StorageStatus StorageStatus { get; set; } = StorageStatus.Unused;

    /// <summary>
    /// 分配状态
    /// </summary>
    [Column("AssignmentStatus")]
    public AssignmentStatus AssignmentStatus { get; set; } = AssignmentStatus.Pending;

    #endregion

    #region 导航属性

    /// <summary>
    /// 关联的用户
    /// </summary>
    [NotMapped]
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// 关联的储物格
    /// </summary>
    [NotMapped]
    public virtual Locker Locker { get; set; } = null!;

    #endregion

    #region 辅助方法

    /// <summary>
    /// 标记为已分配
    /// </summary>
    public void MarkAsAssigned()
    {
        AssignmentStatus = AssignmentStatus.Assigned;
        AssignedAt = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 标记为已释放
    /// </summary>
    public void MarkAsReleased()
    {
        AssignmentStatus = AssignmentStatus.Released;
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 标记为已存放
    /// </summary>
    public void MarkAsStored()
    {
        StorageStatus = StorageStatus.Stored;
        StoredTime = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 标记为已取出
    /// </summary>
    public void MarkAsRetrieved()
    {
        StorageStatus = StorageStatus.Retrieved;
        RetrievedTime = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    /// <returns>是否已过期</returns>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && ExpiresAt.Value < DateTime.Now;
    }

    /// <summary>
    /// 检查是否可分配
    /// </summary>
    /// <returns>是否可分配</returns>
    public bool CanAssign()
    {
        return IsActive && AssignmentStatus == AssignmentStatus.Pending;
    }

    /// <summary>
    /// 检查是否可存放
    /// </summary>
    /// <returns>是否可存放</returns>
    public bool CanStore()
    {
        return IsActive && AssignmentStatus == AssignmentStatus.Assigned && StorageStatus == StorageStatus.Unused;
    }

    /// <summary>
    /// 检查是否可取出
    /// </summary>
    /// <returns>是否可取出</returns>
    public bool CanRetrieve()
    {
        return IsActive && AssignmentStatus == AssignmentStatus.Assigned && StorageStatus == StorageStatus.Stored;
    }

    /// <summary>
    /// 更新时间戳字段
    /// </summary>
    [NotMapped]
    private DateTime UpdatedAt { get; set; } = DateTime.Now;

    #endregion
}

/// <summary>
/// 存取状态（用于记录柜格或订单的当前存储状态）
/// </summary>
public enum StorageStatus
{
    /// <summary>
    /// 未使用（尚未存入物品）
    /// </summary>
    Unused = 0,

    /// <summary>
    /// 已存
    /// </summary>
    Stored = 1,

    /// <summary>
    /// 已取
    /// </summary>
    Retrieved = 2,
}

/// <summary>
/// 分配状态（用于记录柜格或订单的当前分配状态）
/// </summary>
public enum AssignmentStatus
{
    /// <summary>
    /// 待分配（已计划分配，但尚未正式指派）
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已分配
    /// </summary>
    Assigned = 1,

    /// <summary>
    /// 已释放
    /// </summary>
    Released = 2,
}
