using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Tmds.DBus.Protocol;

namespace FaceLocker.Models;

/// <summary>
/// 角色实体模型
/// </summary>
[Table("Roles")]
public class Role
{
    #region 主键和标识属性

    /// <summary>
    /// 角色ID（主键）- 自增长长整型，唯一标识角色
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("Id")]
    public long Id { get; set; }

    #endregion

    #region 基本信息属性

    /// <summary>
    /// 角色名称
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [StringLength(500)]
    [Column("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// 权限列表（JSON格式存储）
    /// </summary>
    [Column("Permissions", TypeName = "TEXT")]
    public List<string> Permissions { get; set; } = new();

    #endregion

    #region 状态属性

    /// <summary>
    /// 是否来自服务器（用于区分本地角色和服务器同步角色）
    /// </summary>
    [Required]
    [Column("IsFromServer")]
    public bool IsFromServer { get; set; } = false;

    /// <summary>
    /// 是否为系统内置角色（内置角色不可删除）
    /// </summary>
    [Required]
    [Column("IsBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;

    #endregion

    #region 时间属性

    /// <summary>
    /// 创建时间
    /// </summary>
    [Required]
    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    [Required]
    [Column("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    #endregion

    #region 导航属性

    /// <summary>
    /// 导航属性：拥有此角色的用户
    /// </summary>
    [NotMapped]
    public virtual ICollection<User> Users { get; set; } = new List<User>();

    #endregion

    #region NotMapped 计算属性

    /// <summary>
    /// 获取角色来源文本
    /// </summary>
    [NotMapped]
    public string SourceText => IsFromServer ? "服务器" : "本地";

    /// <summary>
    /// 获取角色类型文本
    /// </summary>
    [NotMapped]
    public string TypeText => IsBuiltIn ? "内置" : "自定义";

    /// <summary>
    /// 检查角色是否可以编辑（本地非内置角色可编辑）
    /// </summary>
    [NotMapped]
    public bool CanEdit => !IsFromServer && !IsBuiltIn;

    /// <summary>
    /// 检查角色是否可以删除（本地非内置角色可删除）
    /// </summary>
    [NotMapped]
    public bool CanDelete => !IsFromServer && !IsBuiltIn;

    /// <summary>
    /// 获取权限数量
    /// </summary>
    [NotMapped]
    public int PermissionCount => Permissions?.Count ?? 0;

    /// <summary>
    /// 获取权限摘要文本
    /// </summary>
    [NotMapped]
    public string PermissionSummary
    {
        get
        {
            if (Permissions == null || Permissions.Count == 0)
                return "无权限";

            if (Permissions.Count <= 3)
                return string.Join(", ", Permissions.Select(GetPermissionDisplayName));

            return $"{string.Join(", ", Permissions.Take(3).Select(GetPermissionDisplayName))} 等{Permissions.Count}项";
        }
    }

    #endregion

    #region 静态常量和辅助方法

    /// <summary>
    /// 预定义权限常量
    /// </summary>
    public static class PermissionConstants
    {
        public const string SUPER_ADMIN = "SUPER_ADMIN";                     // 超级管理员
        public const string USE_CABINET = "USE_CABINET";                    // 使用柜子
        public const string MANAGE_USER = "MANAGE_USER";                    // 管理用户
        public const string MANAGE_CABINET = "MANAGE_CABINET";              // 管理柜子
        public const string MANAGE_ROLE = "MANAGE_ROLE";                    // 管理角色
        public const string VIEW_LOG = "VIEW_LOG";                          // 查看日志
        public const string SYSTEM_SETTING = "SYSTEM_SETTING";              // 系统设置
        public const string DATA_SYNC = "DATA_SYNC";                        // 数据同步
        public const string EMERGENCY_OPEN = "EMERGENCY_OPEN";              // 紧急开锁
        public const string FACE_RECOGNITION = "FACE_RECOGNITION";          // 人脸识别
        public const string CABINET_CONTROL = "CABINET_CONTROL";            // 柜子控制
    }

    /// <summary>
    /// 验证角色名称格式
    /// </summary>
    /// <param name="name">角色名称</param>
    /// <returns>是否有效</returns>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // 角色名称长度：1-100位
        if (name.Trim().Length < 1 || name.Trim().Length > 100)
            return false;

        return true;
    }

    /// <summary>
    /// 检查角色是否有指定权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    /// <returns>是否有权限</returns>
    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return false;

        return Permissions?.Contains(permission) == true;
    }

    /// <summary>
    /// 添加权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    public void AddPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return;

        if (Permissions == null)
            Permissions = new List<string>();

        if (!Permissions.Contains(permission))
        {
            Permissions.Add(permission);
            UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 移除权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    public void RemovePermission(string permission)
    {
        if (Permissions?.Remove(permission) == true)
        {
            UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 清空所有权限
    /// </summary>
    public void ClearPermissions()
    {
        if (Permissions?.Count > 0)
        {
            Permissions.Clear();
            UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 设置权限列表
    /// </summary>
    /// <param name="permissions">权限列表</param>
    public void SetPermissions(List<string> permissions)
    {
        Permissions = permissions?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 更新角色信息
    /// </summary>
    /// <param name="name">角色名称</param>
    /// <param name="description">角色描述</param>
    /// <param name="permissions">权限列表</param>
    public void UpdateInfo(string name, string? description, List<string>? permissions)
    {
        Name = name?.Trim() ?? string.Empty;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SetPermissions(permissions);
    }

    /// <summary>
    /// 获取权限显示名称
    /// </summary>
    /// <param name="permission">权限名称</param>
    /// <returns>显示名称</returns>
    public static string GetPermissionDisplayName(string permission)
    {
        return permission switch
        {
            PermissionConstants.SUPER_ADMIN => "超级管理员",
            PermissionConstants.USE_CABINET => "使用柜子",
            PermissionConstants.MANAGE_USER => "管理用户",
            PermissionConstants.MANAGE_CABINET => "管理柜子",
            PermissionConstants.MANAGE_ROLE => "管理角色",
            PermissionConstants.VIEW_LOG => "查看日志",
            PermissionConstants.SYSTEM_SETTING => "系统设置",
            PermissionConstants.DATA_SYNC => "数据同步",
            PermissionConstants.EMERGENCY_OPEN => "紧急开锁",
            PermissionConstants.FACE_RECOGNITION => "人脸识别",
            PermissionConstants.CABINET_CONTROL => "柜子控制",
            _ => permission
        };
    }

    /// <summary>
    /// 获取所有可用权限
    /// </summary>
    /// <returns>权限列表</returns>
    public static List<string> GetAllPermissions()
    {
        return new List<string>
        {
            PermissionConstants.SUPER_ADMIN,
            PermissionConstants.USE_CABINET,
            PermissionConstants.MANAGE_USER,
            PermissionConstants.MANAGE_CABINET,
            PermissionConstants.MANAGE_ROLE,
            PermissionConstants.VIEW_LOG,
            PermissionConstants.SYSTEM_SETTING,
            PermissionConstants.DATA_SYNC,
            PermissionConstants.EMERGENCY_OPEN,
            PermissionConstants.FACE_RECOGNITION,
            PermissionConstants.CABINET_CONTROL
        };
    }

    /// <summary>
    /// 获取权限分组
    /// </summary>
    /// <returns>权限分组字典</returns>
    public static Dictionary<string, List<string>> GetPermissionGroups()
    {
        return new Dictionary<string, List<string>>
        {
            ["基础权限"] = new List<string>
            {
                PermissionConstants.USE_CABINET,
                PermissionConstants.FACE_RECOGNITION
            },
            ["管理权限"] = new List<string>
            {
                PermissionConstants.MANAGE_USER,
                PermissionConstants.MANAGE_CABINET,
                PermissionConstants.MANAGE_ROLE
            },
            ["系统权限"] = new List<string>
            {
                PermissionConstants.SYSTEM_SETTING,
                PermissionConstants.DATA_SYNC,
                PermissionConstants.VIEW_LOG
            },
            ["高级权限"] = new List<string>
            {
                PermissionConstants.EMERGENCY_OPEN,
                PermissionConstants.CABINET_CONTROL
            }
        };
    }

    /// <summary>
    /// 创建默认角色
    /// </summary>
    /// <returns>默认角色列表</returns>
    public static List<Role> CreateDefaultRoles()
    {
        return new List<Role>
        {
            new Role
            {
                Name = "超级管理员",
                Description = "拥有所有系统权限",
                Permissions = GetAllPermissions(),
                IsBuiltIn = true,
                IsFromServer = false
            },
            new Role
            {
                Name = "监管人",
                Description = "只能使用分配的柜子进行人脸识别开锁",
                Permissions = new List<string>
                {
                    PermissionConstants.USE_CABINET,
                    PermissionConstants.FACE_RECOGNITION
                },
                IsBuiltIn = true,
                IsFromServer = false
            },
            new Role
            {
                Name = "被监管人",
                Description = "只能使用分配的柜子进行人脸识别开锁",
                Permissions = new List<string>
                {
                    PermissionConstants.USE_CABINET,
                    PermissionConstants.FACE_RECOGNITION
                },
                IsBuiltIn = true,
                IsFromServer = false
            },
            new Role
            {
                Name="用户",
                Description="",
                Permissions=new List<string>
                {
                    PermissionConstants.USE_CABINET,
                     PermissionConstants.FACE_RECOGNITION
                },
                IsBuiltIn=true,
                IsFromServer = false
            }
        };
    }

    /// <summary>
    /// 克隆角色对象（用于编辑时的备份）
    /// </summary>
    /// <returns>角色对象副本</returns>
    public Role Clone()
    {
        return new Role
        {
            // Id 不复制，因为它是自增主键
            Name = this.Name,
            Description = this.Description,
            Permissions = new List<string>(this.Permissions ?? new List<string>()),
            IsFromServer = this.IsFromServer,
            IsBuiltIn = this.IsBuiltIn,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt
        };
    }

    /// <summary>
    /// 重写ToString方法
    /// </summary>
    /// <returns>角色字符串表示</returns>
    public override string ToString()
    {
        return $"Role[{Id}]: {Name} - {TypeText} ({SourceText}) - {PermissionCount}项权限";
    }

    /// <summary>
    /// 重写Equals方法
    /// </summary>
    /// <param name="obj">比较对象</param>
    /// <returns>是否相等</returns>
    public override bool Equals(object? obj)
    {
        if (obj is Role other)
        {
            return Id == other.Id;
        }
        return false;
    }

    /// <summary>
    /// 重写GetHashCode方法
    /// </summary>
    /// <returns>哈希码</returns>
    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    #endregion
}