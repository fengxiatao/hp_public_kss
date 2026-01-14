using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FaceLocker.Models.Settings;

namespace FaceLocker.Models;

/// <summary>
/// 储物格实体类
/// </summary>
[Table("Lockers")]
public class Locker
{
    #region 主键和标识属性

    /// <summary>
    /// 储物格ID（主键）- 自增长长整型，唯一标识储物格
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("LockerId")]
    public long LockerId { get; set; }

    #endregion

    #region 基本信息属性

    /// <summary>
    /// 储物格名称
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("LockerName")]
    public string LockerName { get; set; } = string.Empty;

    /// <summary>
    /// 储物格编号
    /// </summary>
    [MaxLength(50)]
    [Column("LockerNumber")]
    public string LockerNumber { get; set; } = string.Empty;

    /// <summary>
    /// 控制板地址
    /// </summary>
    [Column("BoardAddress")]
    public int BoardAddress { get; set; }

    /// <summary>
    /// 通道编号
    /// </summary>
    [Column("ChannelNumber")]
    public int ChannelNumber { get; set; }

    #endregion

    #region 状态属性

    /// <summary>
    /// 储物格状态
    /// </summary>
    [Required]
    [Column("Status")]
    public LockerStatus Status { get; set; } = LockerStatus.Available;

    /// <summary>
    /// 是否已打开
    /// </summary>
    [Required]
    [Column("IsOpened")]
    public bool IsOpened { get; set; } = false;

    /// <summary>
    /// 是否可用
    /// </summary>
    [Column("IsAvailable")]
    public bool IsAvailable { get; set; } = true;

    #endregion

    #region 时间和位置属性

    /// <summary>
    /// 最后开启时间
    /// </summary>
    [Column("LastOpened")]
    public DateTime? LastOpened { get; set; }

    /// <summary>
    /// 储物格位置
    /// </summary>
    [MaxLength(100)]
    [Column("Location")]
    public string? Location { get; set; }

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

    #region NotMapped 计算属性

    /// <summary>
    /// 开始时间（兼容性属性，映射到LastOpened）
    /// </summary>
    [NotMapped]
    public DateTime? StartTime
    {
        get => LastOpened;
        set => LastOpened = value;
    }

    /// <summary>
    /// 备注
    /// </summary>
    [NotMapped]
    public string Remarks { get; set; } = string.Empty;

    /// <summary>
    /// 检查是否可以开锁
    /// </summary>
    [NotMapped]
    public bool CanOpen => IsAvailable && Status == LockerStatus.Available;

    #endregion

    #region 导航属性

    /// <summary>
    /// 用户储物格分配记录
    /// </summary>
    [NotMapped]
    public virtual ICollection<UserLocker> UserLockers { get; set; } = new List<UserLocker>();

    /// <summary>
    /// 访问日志
    /// </summary>
    [NotMapped]
    public virtual ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();

    #endregion

    #region 静态方法（创建默认储物格数据）

    /// <summary>
    /// 创建默认储物格数据 - 从appsettings.json读取配置
    /// 柜名：值为AppSetting.LockController.GroupName
    /// 主板Id列表：Boards数组，个数为数组的长度
    /// 主板Id: BoardAddress.Address（主板Id就是数组的索引号+1）
    /// 通道号: 每个主板有12个通道，从1到12
    /// 柜格名：如果有N个主板，柜格名从1开始到12*N，LockerName=$"{柜名}{i:000}"
    /// Location: Location=$"{柜名}"
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>默认储物格列表</returns>
    public static List<Locker> CreateDefaultLockers(ILogger<Locker>? logger = null)
    {
        var defaultLockers = new List<Locker>();

        try
        {
            // 检查App.Configuration是否已初始化
            if (App.Configuration == null)
            {
                logger?.LogWarning("App.Configuration未初始化，无法读取锁控制器配置，将使用默认配置生成12个储物格。");
                return CreateFallbackDefaultLockers(logger);
            }

            // 从配置中获取LockController设置
            var lockControllerSection = App.Configuration.GetSection("LockController");
            if (!lockControllerSection.Exists())
            {
                logger?.LogWarning("appsettings.json中未找到LockController配置节，将使用默认配置生成12个储物格。");
                return CreateFallbackDefaultLockers(logger);
            }

            // 绑定配置到LockControllerSettings对象
            var lockControllerSettings = new LockControllerSettings();
            lockControllerSection.Bind(lockControllerSettings);

            // 获取配置信息
            string groupName = lockControllerSettings.GroupName ?? "A";
            var boards = lockControllerSettings.Boards ?? new List<LockBoardSettings>();

            if (boards.Count == 0)
            {
                logger?.LogWarning("LockController配置中未找到任何主板数据(Boards数组为空)，将使用默认配置生成12个储物格。");
                return CreateFallbackDefaultLockers(logger);
            }

            int totalLockers = boards.Count * 12;
            logger?.LogInformation("开始创建默认储物格数据，配置来源：appsettings.json，组名：{GroupName}，主板数量：{BoardCount}，总储物格数量：{TotalLockers}",
                groupName, boards.Count, totalLockers);

            // 遍历所有主板
            for (int boardIndex = 0; boardIndex < boards.Count; boardIndex++)
            {
                var board = boards[boardIndex];
                int boardAddress = board.Address;

                logger?.LogDebug("正在处理主板，主板索引：{BoardIndex}，主板地址：{BoardAddress}",
                    boardIndex + 1, boardAddress);

                // 每个主板有12个通道
                for (int channel = 1; channel <= 12; channel++)
                {
                    // 计算全局索引（从1开始）
                    int globalIndex = boardIndex * 12 + channel;

                    // 生成储物格名称和编号（根据需求：LockerName=$"{柜名}{i:000}"）
                    string lockerName = $"{groupName}{globalIndex:000}";
                    string lockerNumber = lockerName;

                    var locker = new Locker
                    {
                        // LockerId 使用数据库自增，此处不赋值
                        LockerName = lockerName,
                        LockerNumber = lockerNumber,
                        BoardAddress = boardAddress,
                        ChannelNumber = channel,
                        Status = LockerStatus.Available,
                        IsAvailable = true,
                        Location = groupName,  // 根据需求：Location=$"{柜名}"
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    defaultLockers.Add(locker);

                    logger?.LogTrace("创建储物格成功，名称：{LockerName}，主板地址：{BoardAddress}，通道号：{ChannelNumber}，全局索引：{GlobalIndex}",
                        lockerName, boardAddress, channel, globalIndex);
                }
            }

            logger?.LogInformation("默认储物格数据创建完成，共创建 {Count} 个储物格，配置组名：{GroupName}。",
                defaultLockers.Count, groupName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "创建默认储物格数据时发生异常，将使用默认配置生成12个储物格。");
            return CreateFallbackDefaultLockers(logger);
        }

        return defaultLockers;
    }

    /// <summary>
    /// 创建回退的默认储物格数据（当配置读取失败时使用）
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <returns>默认储物格列表</returns>
    private static List<Locker> CreateFallbackDefaultLockers(ILogger<Locker>? logger)
    {
        var fallbackLockers = new List<Locker>();

        logger?.LogInformation("使用回退配置创建默认储物格数据，生成12个A区储物格。");

        // 创建A区的12个储物格（兼容原有逻辑）
        for (int i = 1; i <= 12; i++)
        {
            var locker = new Locker
            {
                LockerName = $"A{i:000}",
                LockerNumber = $"A{i:000}",
                BoardAddress = 1,
                ChannelNumber = i,
                Status = LockerStatus.Available,
                IsAvailable = true,
                Location = "A",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            fallbackLockers.Add(locker);

            logger?.LogTrace("创建回退储物格成功，名称：{LockerName}，主板地址：1，通道号：{ChannelNumber}",
                locker.LockerName, i);
        }

        logger?.LogInformation("回退储物格数据创建完成，共创建 {Count} 个储物格。", fallbackLockers.Count);
        return fallbackLockers;
    }

    #endregion
}

#region 储物格状态枚举
/// <summary>
/// 储物格状态枚举
/// </summary>
public enum LockerStatus
{
    /// <summary>
    /// 可用
    /// </summary>
    Available = 0,

    /// <summary>
    /// 已分配
    /// </summary>
    Assigned = 1,

    /// <summary>
    /// 禁用
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// 屏幕占用
    /// </summary>
    ScreenOccupied = 4,

    /// <summary>
    /// 故障
    /// </summary>
    Fault = 5,
}
#endregion
