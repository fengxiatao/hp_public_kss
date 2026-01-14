using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FaceLocker.Models;

/// <summary>
/// 用户实体模型 - 表示系统用户信息（FaceFeatureData 改为 byte[] 存 BLOB）
/// </summary>
[Table("Users")]
public class User
{
    #region 主键和标识属性

    /// <summary>
    /// 用户ID（主键）- 自增长长整型，唯一标识用户
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("Id")]
    public long Id { get; set; }

    [NotMapped]
    public string UserId => Id.ToString();

    #endregion

    #region 基本信息属性
    /// <summary>
    /// 用户编号（唯一标识）- 用于系统内部识别
    /// </summary>
    [Required, StringLength(50)]
    [Column("UserNumber")]
    public string UserNumber { get; set; } = "";

    /// <summary>
    /// 用户姓名 - 用户的真实姓名
    /// </summary>
    [StringLength(100)]
    [Column("Name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 身份证号码
    /// </summary>
    [StringLength(100)]
    [Column("IdNumber")]
    public string IdNumber { get; set; } = "";

    /// <summary>
    /// 手机号码 - 用于系统内部识别
    /// </summary>
    [StringLength(100)]
    [Column("PhoneNumber")]
    public string PhoneNumber { get; set; } = "";

    [NotMapped]
    public string UserName => Name;

    /// <summary>
    /// 用户密码 - 存储SHA256哈希值（十六进制字符串）
    /// </summary>
    [Required, StringLength(64)]
    [Column("Password")]
    public string Password { get; set; } = "";

    [StringLength(100)]
    [Column("Department")]
    public string? Department { get; set; }

    [StringLength(500)]
    [Column("Remarks")]
    public string? Remarks { get; set; }

    #endregion

    #region 权限和角色属性
    /// <summary>
    /// 角色ID - 关联角色表
    /// </summary>
    [Required]
    [Column("RoleId")]
    public long RoleId { get; set; } = 0;

    /// <summary>
    /// 分配的柜子ID列表（JSON序列化存储到TEXT）
    /// 这里保持原样；生产上建议用中间表替代
    /// </summary>
    [Column("AssignedLockers", TypeName = "TEXT")]
    public List<long> AssignedLockers
    {
        get => _assignedLockers;
        set => _assignedLockers = value ?? [];
    }


    private List<long> _assignedLockers = [];

    #endregion

    #region 人脸识别相关属性（已改为 byte[]）

    /// <summary>
    /// 用户头像（Base64字符串，TEXT）
    /// </summary>
    [Column("Avatar", TypeName = "TEXT")]
    public string? Avatar { get; set; }

    /// <summary>
    /// 人脸识别特征数据（128 维 float 的原始字节，共 512 字节）
    /// 存为 BLOB，避免 Base64 截断/换行/不可见字符等问题
    /// </summary>
    [Column("FaceFeatureData", TypeName = "BLOB")]
    public byte[]? FaceFeatureData { get; set; }

    /// <summary>人脸特征质量评分（注册质量，用于显示/参考）</summary>
    [Column("FaceConfidence")]
    public float FaceConfidence { get; set; } = 0.0f;

    /// <summary>特征版本（用于模型/算法版本管理）</summary>
    [Column("FaceFeatureVersion")]
    public int FaceFeatureVersion { get; set; } = 1;

    /// <summary>最后人脸更新时间</summary>
    [Column("LastFaceUpdate")]
    public DateTime? LastFaceUpdate { get; set; }

    #endregion

    #region 状态和时间戳属性
    /// <summary>
    /// 是否激活 - 控制用户账户是否可用
    /// </summary>
    [Required]
    [Column("IsActive")]
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// 创建时间 - 记录用户创建时间
    /// </summary>
    [Required]
    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>
    /// 更新时间 - 记录用户最后更新时间
    /// </summary>
    [Required]
    [Column("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    #endregion

    #region NotMapped 计算属性
    /// <summary>
    /// 是否允许删除 - 所有用户都允许删除
    /// </summary>
    [NotMapped]
    public bool CanDelete => true;
    /// <summary>
    /// 获取用户头像路径（如果没有头像则返回默认头像）
    /// </summary>
    [NotMapped]
    public string AvatarPath => string.IsNullOrWhiteSpace(Avatar) ? "avares://FaceLocker/Assets/default-avatar.png" : Avatar;

    /// <summary>
    /// 检查是否有人脸特征数据
    /// </summary>
    [NotMapped]
    public bool HasFaceFeature => FaceFeatureData != null && FaceFeatureData.Length > 0;

    /// <summary>
    /// 获取人脸特征数据的大小（字节数）
    /// </summary>
    [NotMapped]
    public int FaceFeatureSize => FaceFeatureData?.Length ?? 0;

    /// <summary>
    /// 获取人脸特征信息的显示文本
    /// </summary>
    [NotMapped]
    public string FaceFeatureInfo
    {
        get
        {
            if (!HasFaceFeature) return "未录入";
            var size = FaceFeatureSize;
            var version = FaceFeatureVersion;
            var date = LastFaceUpdate?.ToString("yyyy-MM-dd") ?? "未知";
            return $"已录入 (v{version}, {size}字节, {date})";
        }
    }

    #endregion

    #region 导航属性
    [NotMapped]
    public virtual ICollection<UserLocker> UserLockers { get; set; } = new List<UserLocker>();
    [NotMapped]
    public virtual ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();

    #endregion

    #region 公共方法（已改为接收 byte[]）

    /// <summary>
    /// 更新人脸特征数据
    /// </summary>
    /// <param name="featureData">人脸特征数据（Base64格式）</param>
    /// <param name="confidence">置信度</param>
    /// <param name="avatar">头像数据（Base64格式，可选）</param>
    public void UpdateFaceFeature(byte[] featureBytes, float confidence, string? avatarBase64 = null)
    {
        // featureBytes 必须是 512 字节（128*4）
        if (featureBytes == null || featureBytes.Length != 512)
            throw new ArgumentException("featureBytes 长度必须为 512 字节（128 个 float）", nameof(featureBytes));

        FaceFeatureData = featureBytes;
        FaceConfidence = confidence;
        if (!string.IsNullOrWhiteSpace(avatarBase64))
            Avatar = avatarBase64;

        FaceFeatureVersion++;
        LastFaceUpdate = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 清除人脸特征数据
    /// </summary>
    public void ClearFaceFeature()
    {
        FaceFeatureData = null;
        FaceConfidence = 0.0f;
        LastFaceUpdate = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 验证人脸特征数据格式
    /// </summary>
    public bool ValidateFaceFeature()
    {
        return FaceFeatureData != null && FaceFeatureData.Length == 512;
    }

    #endregion

    #region 静态方法（默认用户）

    public static List<User> CreateDefaultUsers()
    {
        var defaultUsers = new List<User>();

        var superAdmin = new User
        {
            Id = 1,
            Name = "admin",
            UserNumber = "超级管理员",
            Password = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92", // 123456 的 SHA256
            Department = "系统管理部",
            RoleId = 1,
            AssignedLockers = [],
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Remarks = "系统默认超级管理员账户，拥有所有权限",
            // FaceFeatureData 初始为空；首次录入后写入
        };
        defaultUsers.Add(superAdmin);

        return defaultUsers;
    }

    #endregion
}
