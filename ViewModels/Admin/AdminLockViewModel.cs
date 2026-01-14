using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;
using FaceLocker.Models;
using FaceLocker.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace FaceLocker.ViewModels;

/// <summary>
/// 锁柜管理 ViewModel
/// </summary>
public class AdminLockViewModel : ViewModelBase, IDisposable
{
    #region 私有字段
    private readonly ILockerService _lockerService;
    private readonly ILockControlService _lockControlService;
    private readonly IAccessLogService _accessLogService;
    private readonly ILogger<AdminLockViewModel> _logger;
    private readonly IAppStateService _appStateService;
    private readonly Timer _statusUpdateTimer;
    #endregion

    #region 构造函数
    /// <summary>
    /// 构造函数
    /// </summary>
    public AdminLockViewModel(
        ILockerService lockerService,
        ILockControlService lockControlService,
        IAccessLogService accessLogService,
        ILogger<AdminLockViewModel> logger,
        IAppStateService appStateService)
    {
        _lockerService = lockerService;
        _lockControlService = lockControlService;
        _accessLogService = accessLogService;
        _logger = logger;
        _appStateService = appStateService;

        _logger.LogInformation("AdminLockViewModel 初始化开始");

        #region 获取柜组信息
        DeviceName = GetDeviceName();
        IPAddress = GetNetworkIP();
        GroupName = GetLockerGroupName();
        //获取柜组排序方向
        var direction = GetLockerSortDirection();
        LockerFlowDirection = direction == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        _logger.LogInformation($"设备信息初始化 - 设备名: {DeviceName}, IP: {IPAddress}, 柜组: {GroupName}, 方向：{(LockerFlowDirection == 0 ? "左" : "右")}");
        #endregion

        // 初始化命令
        InitializeCommands();

        // 异步初始化柜子数据
        _ = Task.Run(InitializeLockersAsync);

        // 设置定时器，每隔45秒更新状态
        _statusUpdateTimer = new Timer(45000);
        _logger.LogDebug("Timer触发，准备刷新...");
        _statusUpdateTimer.Elapsed += async (s, e) =>
        {
            _logger.LogDebug("已切换回UI线程，开始更新...");
            await UpdateLockersStatusAsync();
        };
        _statusUpdateTimer.AutoReset = true;
        _statusUpdateTimer.Start();

        _logger.LogInformation("AdminLockViewModel 初始化完成");
    }
    #endregion

    #region 属性定义
    private string _deviceName = string.Empty;
    /// <summary>
    /// 设备名称
    /// </summary>
    public string DeviceName
    {
        get => _deviceName;
        set => this.RaiseAndSetIfChanged(ref _deviceName, value);
    }

    private string _ipAddress = string.Empty;
    /// <summary>
    /// IP地址
    /// </summary>
    public string IPAddress
    {
        get => _ipAddress;
        set => this.RaiseAndSetIfChanged(ref _ipAddress, value);
    }

    private string _groupName = string.Empty;
    /// <summary>
    /// 柜组名称
    /// </summary>
    public string GroupName
    {
        get => _groupName;
        set => this.RaiseAndSetIfChanged(ref _groupName, value);
    }

    private FlowDirection _lockerFlowDirection = FlowDirection.LeftToRight;
    /// <summary>
    /// 柜子排序方向
    /// </summary>
    public FlowDirection LockerFlowDirection
    {
        get => _lockerFlowDirection;
        set => this.RaiseAndSetIfChanged(ref _lockerFlowDirection, value);
    }

    private AvaloniaList<LockerItem> _lockers = new AvaloniaList<LockerItem>();
    /// <summary>
    /// 柜子列表
    /// </summary>
    public AvaloniaList<LockerItem> Lockers
    {
        get => _lockers;
        set => this.RaiseAndSetIfChanged(ref _lockers, value);
    }

    private bool _canClearAll = true;
    /// <summary>
    /// 是否可以执行一键清柜操作
    /// </summary>
    public bool CanClearAll
    {
        get => _canClearAll;
        set => this.RaiseAndSetIfChanged(ref _canClearAll, value);
    }

    private bool _isOperating = false;
    /// <summary>
    /// 是否正在执行操作（用于防止重复操作）
    /// </summary>
    public bool IsOperating
    {
        get => _isOperating;
        set => this.RaiseAndSetIfChanged(ref _isOperating, value);
    }
    #endregion

    #region 命令定义
    /// <summary>
    /// 格子点击命令
    /// </summary>
    public ReactiveCommand<SmallLockerItem, Unit> OnLockerClickCommand { get; private set; } = null!;

    /// <summary>
    /// 一键清柜命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ClearAllCommand { get; private set; } = null!;

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        _logger.LogDebug("初始化 AdminLockViewModel 命令");

        // 格子点击命令 - 添加执行条件判断
        var canLockerClick = this.WhenAnyValue(x => x.IsOperating, operating => !operating);

        OnLockerClickCommand = ReactiveCommand.CreateFromTask<SmallLockerItem>(OnLockerClick, canLockerClick);

        // 一键清柜命令
        var canClearAll = this.WhenAnyValue(
            x => x.CanClearAll,
            x => x.IsOperating,
            (clear, operating) => clear && !operating); // 可清柜且不在操作中

        ClearAllCommand = ReactiveCommand.CreateFromTask(ClearAll, canClearAll);

        _logger.LogDebug("AdminLockViewModel 命令初始化完成");
    }
    #endregion

    #region 初始化柜子数据
    /// <summary>
    /// 初始化柜子数据
    /// </summary>
    private async Task InitializeLockersAsync()
    {
        _logger.LogInformation("开始初始化柜子数据");

        Lockers.Clear();

        // 从数据库获取所有柜子配置
        var dbLockers = await _lockerService.GetLockersAsync();
        _logger.LogInformation($"从数据库读取到 {dbLockers.Count} 个柜子配置");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Lockers.Clear();
            // 从数据库中初始化柜子状态
            for (int i = 1; i <= 5; i++)
            {
                var smallLockers = new List<SmallLockerItem>();
                for (int j = 1; j <= 12; j++)
                {
                    var dbLocker = dbLockers.FirstOrDefault(o => o.BoardAddress == i && o.ChannelNumber == j);
                    if (dbLocker != null)
                    {
                        smallLockers.Add(new SmallLockerItem
                        {
                            Number = dbLocker.LockerNumber,
                            Status = dbLocker.Status,
                            DisplayNumber = dbLocker.LockerName,
                            BoardAddress = dbLocker.BoardAddress,
                            ChannelNumber = dbLocker.ChannelNumber,
                            IsEnabled = dbLocker.Status == LockerStatus.Available && dbLocker.IsAvailable,
                            IsAvailable = dbLocker.IsAvailable,
                            IsOpened = dbLocker.IsOpened,
                            LockerName = dbLocker.LockerName,
                            LockerId = dbLocker.LockerId,
                            FullNumber = $"{dbLocker.BoardAddress}-{dbLocker.ChannelNumber}",
                            LastOpenedTime = dbLocker.LastOpened
                        });
                    }
                    else
                    {
                        smallLockers.Add(new SmallLockerItem
                        {
                            Number = j.ToString(),
                            Status = LockerStatus.Available,
                            DisplayNumber = "",
                            BoardAddress = i,
                            ChannelNumber = j,
                            IsEnabled = false, // 默认禁用，等待数据库确认
                            IsAvailable = false,
                            IsOpened = false,
                            LockerName = $"{i}{j}",
                        });
                    }
                }

                Lockers.Add(new LockerItem
                {
                    GroupName = $"", //暂时清空柜组名称
                    SmallLockers = smallLockers,
                });
            }

            _logger.LogInformation($"柜子数据初始化完成，共{Lockers.Count}个大柜子");
        });
    }
    #endregion

    #region 从硬件读取锁状态
    /// <summary>
    /// 从锁控板读取实际锁状态并立即更新UI
    /// </summary>
    private async Task UpdateLockersFromHardwareAsync()
    {
        _logger.LogDebug("开始从硬件读取锁状态");

        try
        {
            // 获取所有配置的板
            var boards = _lockControlService.GetConfiguredBoards();
            if (boards.Count == 0)
            {
                _logger.LogWarning("没有配置锁控板");
                return;
            }

            // 检查所有数据库中存在的可用柜子
            var lockersToCheck = new List<SmallLockerItem>();
            foreach (var lockerItem in Lockers)
            {
                foreach (var smallLocker in lockerItem.SmallLockers)
                {
                    // 检查有LockerId（数据库中存在）、可用的柜子
                    if (smallLocker.LockerId > 0 && smallLocker.IsAvailable && smallLocker.Status == LockerStatus.Available)
                    {
                        lockersToCheck.Add(smallLocker);
                    }
                }
            }

            _logger.LogDebug($"需要检查 {lockersToCheck.Count} 个可用柜子状态");

            // 按板地址分组柜子
            var lockersByBoard = lockersToCheck.GroupBy(l => l.BoardAddress).ToDictionary(g => g.Key, g => g.ToList());

            bool hasUpdates = false;

            // 对所有有可用柜子的板进行状态读取
            foreach (var boardEntry in lockersByBoard)
            {
                var boardAddr = boardEntry.Key;
                var boardLockers = boardEntry.Value;

                _logger.LogDebug($"检查板 {boardAddr}，有 {boardLockers.Count} 个可用柜子");

                // 检查这个板是否在配置中
                if (boards.Any(b => b.Address == boardAddr))
                {
                    try
                    {
                        // 逐个读取柜子状态
                        foreach (var smallLocker in boardLockers)
                        {
                            try
                            {
                                _logger.LogDebug($"读取板{boardAddr}通道{smallLocker.ChannelNumber}的单个状态");
                                var hardwareStatus = await _lockControlService.ReadOneStatusAsync(boardAddr, smallLocker.ChannelNumber);

                                if (hardwareStatus.HasValue)
                                {
                                    _logger.LogDebug($"柜子 {boardAddr}-{smallLocker.ChannelNumber} 硬件打开状态: {hardwareStatus.Value}, UI打开状态: {smallLocker.IsOpened}");

                                    // 情况1：硬件状态是打开的，但UI显示为关闭 - 立即更新UI
                                    if (hardwareStatus.Value && !smallLocker.IsOpened)
                                    {
                                        _logger.LogInformation($"检测到柜子 {boardAddr}-{smallLocker.ChannelNumber} 已手动打开，立即更新UI状态");

                                        // 立即更新UI状态
                                        smallLocker.IsOpened = true;
                                        smallLocker.IsEnabled = false; // 打开状态下按钮禁用
                                        smallLocker.LastOpenedTime = DateTime.Now;
                                        hasUpdates = true;

                                        // 更新数据库中的状态
                                        await UpdateLockerStatusInDatabase(smallLocker, true);
                                    }
                                    // 情况2：硬件状态是关闭的，但UI显示为打开 - 立即更新UI
                                    else if (!hardwareStatus.Value && smallLocker.IsOpened)
                                    {
                                        _logger.LogInformation($"检测到柜子 {boardAddr}-{smallLocker.ChannelNumber} 已关闭，立即更新UI状态");

                                        // 立即更新UI状态
                                        smallLocker.IsOpened = false;
                                        smallLocker.IsEnabled = true; // 恢复按钮可用性
                                        hasUpdates = true;

                                        // 更新数据库中的状态
                                        await UpdateLockerStatusInDatabase(smallLocker, false);
                                    }

                                    if (hasUpdates)
                                    {
                                        // 触发UI属性变更通知，确保界面立即刷新
                                        smallLocker.RaisePropertyChanged(nameof(smallLocker.DisplayColor));
                                        smallLocker.RaisePropertyChanged(nameof(smallLocker.IsOpened));
                                        smallLocker.RaisePropertyChanged(nameof(smallLocker.IsEnabled));
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning($"读取板{boardAddr}通道{smallLocker.ChannelNumber}状态失败，返回null");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"读取板{boardAddr}通道{smallLocker.ChannelNumber}状态时发生异常");
                            }

                            // 添加短暂延迟，避免连续读取
                            await Task.Delay(50);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"读取板 {boardAddr} 状态时发生异常");
                    }
                }
                else
                {
                    _logger.LogWarning($"板 {boardAddr} 不在配置列表中");
                }
            }

            if (hasUpdates)
            {
                _logger.LogInformation("硬件状态检查完成，UI已更新");
            }
            else
            {
                _logger.LogDebug("硬件状态检查完成，无状态变化");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从硬件读取锁状态失败");
        }
    }
    #endregion

    #region 小格子点击命令
    /// <summary>
    /// 格子点击命令处理
    /// </summary>
    private async Task OnLockerClick(SmallLockerItem lockerItem)
    {
        _logger.LogInformation($"开始处理格子点击: {lockerItem.FullNumber}, 状态: {lockerItem.Status}, 是否已打开: {lockerItem.IsOpened}");

        lockerItem.IsEnabled = false;
        IsOperating = true;

        try
        {
            _logger.LogInformation($"准备打开格子 {lockerItem.FullNumber}");

            // 先更新UI状态
            lockerItem.IsOpened = true;
            lockerItem.LastOpenedTime = DateTime.Now;

            _logger.LogInformation($"已更新格子 {lockerItem.FullNumber} 的UI状态为打开");

            // 物理开锁操作
            var result = await _lockControlService.OpenLockAsync(lockerItem.BoardAddress, lockerItem.ChannelNumber);

            if (result)
            {
                _logger.LogInformation($"打开格子 {lockerItem.FullNumber} 成功");

                // 立即验证单个通道状态
                _logger.LogDebug($"立即验证格子 {lockerItem.FullNumber} 的硬件状态");
                var verifyStatus = await _lockControlService.ReadOneStatusAsync(lockerItem.BoardAddress, lockerItem.ChannelNumber);

                if (verifyStatus.HasValue)
                {
                    _logger.LogInformation($"格子 {lockerItem.FullNumber} 验证状态: {verifyStatus.Value}");

                    // 如果验证状态为关闭，说明开锁后立即关闭了
                    if (!verifyStatus.Value)
                    {
                        _logger.LogInformation($"格子 {lockerItem.FullNumber} 开锁后状态为关闭，保持UI打开状态（可能是脉冲开锁）");
                        // 对于脉冲开锁，硬件状态会立即恢复关闭，但UI应该保持打开状态
                        // 不需要更新UI状态，保持打开
                    }
                }
                else
                {
                    lockerItem.IsOpened = false;
                    lockerItem.IsEnabled = true;
                    _logger.LogWarning($"格子 {lockerItem.FullNumber} 状态验证失败");
                }

                // 记录开锁日志
                await _accessLogService.LogAccessAsync(1, "超级管理员", lockerItem.LockerId, lockerItem.LockerName, AccessAction.AdminOpenLocker, (bool)verifyStatus ? AccessResult.Success : AccessResult.Failed);

                // 更新数据库状态
                await UpdateLockerStatusInDatabase(lockerItem, true);

                // 只更新当前格子的状态
                await UpdateSingleLockerStatusAsync(lockerItem);
            }
            else
            {
                _logger.LogError($"打开格子 {lockerItem.FullNumber} 失败");

                // 操作失败，恢复UI状态
                lockerItem.IsOpened = false;
                lockerItem.IsEnabled = true;

                // 更新数据库状态
                await UpdateLockerStatusInDatabase(lockerItem, false);

                // 只更新当前格子的状态
                await UpdateSingleLockerStatusAsync(lockerItem);

                _logger.LogInformation($"已恢复格子 {lockerItem.FullNumber} 的UI状态");

                // 记录失败日志
                await _accessLogService.LogAccessAsync(1, "超级管理员", lockerItem.LockerId, lockerItem.LockerName, AccessAction.AdminOpenLocker, AccessResult.Failed, $"开锁失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"打开格子 {lockerItem.FullNumber} 时发生错误");

            // 发生异常，恢复UI状态
            lockerItem.IsOpened = false;
            lockerItem.IsEnabled = true;

            _logger.LogInformation($"发生异常，已恢复格子 {lockerItem.FullNumber} 的UI状态");

            // 记录异常日志
            await _accessLogService.LogAccessAsync(1, "超级管理员", lockerItem.LockerId, lockerItem.LockerName, AccessAction.AdminOpenLocker, AccessResult.Failed, $"开锁异常");
        }
        finally
        {
            IsOperating = false;
            if (!lockerItem.IsOpened) lockerItem.IsEnabled = true;
            _logger.LogInformation($"格子 {lockerItem.FullNumber} 点击处理完成，IsOperating已重置={IsOperating}");

            // 只在失败情况下可能需要更新单个状态
            if (!lockerItem.IsOpened)
            {
                await UpdateSingleLockerStatusAsync(lockerItem);
            }
        }
    }
    #endregion

    #region 单个格子状态更新
    /// <summary>
    /// 更新单个格子的状态（从硬件读取）
    /// </summary>
    private async Task UpdateSingleLockerStatusAsync(SmallLockerItem lockerItem)
    {
        _logger.LogDebug($"开始更新单个格子状态: {lockerItem.FullNumber}");

        try
        {
            // 从硬件读取当前格子的状态
            var hardwareStatus = await _lockControlService.ReadOneStatusAsync(lockerItem.BoardAddress, lockerItem.ChannelNumber);

            if (hardwareStatus.HasValue)
            {
                _logger.LogDebug($"格子 {lockerItem.FullNumber} 硬件状态: {hardwareStatus.Value}, UI状态: {lockerItem.IsOpened}");

                bool hasUpdate = false;

                // 情况1：硬件状态是打开的，但UI显示为关闭 - 立即更新UI
                if (hardwareStatus.Value && !lockerItem.IsOpened)
                {
                    _logger.LogInformation($"检测到格子 {lockerItem.FullNumber} 已手动打开，立即更新UI状态");
                    lockerItem.IsOpened = true;
                    lockerItem.IsEnabled = false; // 打开状态下按钮禁用
                    lockerItem.LastOpenedTime = DateTime.Now;
                    hasUpdate = true;

                    // 更新数据库中的状态
                    await UpdateLockerStatusInDatabase(lockerItem, true);
                }
                // 情况2：硬件状态是关闭的，但UI显示为打开 - 立即更新UI
                else if (!hardwareStatus.Value && lockerItem.IsOpened)
                {
                    _logger.LogInformation($"检测到格子 {lockerItem.FullNumber} 已关闭，立即更新UI状态");
                    lockerItem.IsOpened = false;
                    lockerItem.IsEnabled = true; // 恢复按钮可用性
                    hasUpdate = true;

                    // 更新数据库中的状态
                    await UpdateLockerStatusInDatabase(lockerItem, false);
                }

                if (hasUpdate)
                {
                    // 触发UI属性变更通知，确保界面立即刷新
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.DisplayColor));
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.IsOpened));
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.IsEnabled));
                    _logger.LogInformation($"格子 {lockerItem.FullNumber} 状态更新完成");
                }
                else
                {
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.DisplayColor));
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.IsOpened));
                    lockerItem.RaisePropertyChanged(nameof(lockerItem.IsEnabled));
                    _logger.LogDebug($"格子 {lockerItem.FullNumber} 状态无变化，无需更新");
                }
            }
            else
            {
                _logger.LogWarning($"读取格子 {lockerItem.FullNumber} 硬件状态失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"更新单个格子 {lockerItem.FullNumber} 状态时发生异常");
        }
    }
    #endregion

    #region 更新小格子状态到数据库
    /// <summary>
    /// 更新小格子状态到数据库
    /// </summary>
    private async Task UpdateLockerStatusInDatabase(SmallLockerItem lockerItem, bool? isOpened = false)
    {
        _logger.LogDebug($"更新柜子 {lockerItem.FullNumber} 数据库状态，开锁状态: {isOpened}");

        try
        {
            var locker = await _lockerService.GetLockerByAddressAndChannelAsync(lockerItem.BoardAddress, lockerItem.ChannelNumber);
            if (locker != null)
            {
                // 如果提供了开锁状态，使用提供的值，否则使用界面当前值
                locker.IsOpened = isOpened ?? lockerItem.IsOpened;

                // 如果开锁状态为true，更新最后打开时间
                if (locker.IsOpened)
                {
                    locker.LastOpened = DateTime.Now;
                }

                var result = await _lockerService.UpdateLockerAsync(locker);

                if (result)
                {
                    _logger.LogDebug($"更新柜子 {lockerItem.FullNumber} 数据库状态成功: {(locker.IsOpened ? "打开" : "关闭")}");
                }
                else
                {
                    _logger.LogWarning($"更新柜子 {lockerItem.FullNumber} 数据库状态失败");
                }
            }
            else
            {
                _logger.LogWarning($"未找到柜子 {lockerItem.FullNumber} 的数据库记录，LockerId: {lockerItem.LockerId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"更新柜子 {lockerItem.FullNumber} 数据库状态失败");
        }
    }
    #endregion

    #region 定期更新小格子状态
    /// <summary>
    /// 定期更新格子状态
    /// </summary>
    private async Task UpdateLockersStatusAsync()
    {
        _logger.LogDebug("开始定期更新柜子状态");

        try
        {
            // 从数据库获取所有柜子最新状态
            var dbLockers = await _lockerService.GetLockersAsync();
            UpdateUIFromDatabase(dbLockers);

            // 只对数据库中存在且已打开的柜子进行硬件状态检查
            await UpdateLockersFromHardwareAsync();

            _logger.LogDebug("定期更新柜子状态完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新格子状态失败");
        }
        finally
        {
            _logger.LogInformation("定期更新柜子状态已结束");
        }
    }
    #endregion

    #region 根据数据库数据更新UI状态
    /// <summary>
    /// 根据数据库数据更新UI状态
    /// </summary>
    private void UpdateUIFromDatabase(List<Locker> dbLockers)
    {
        if (dbLockers == null)
        {
            _logger.LogWarning("数据库柜子列表为null");
            return;
        }

        _logger.LogDebug($"开始更新UI状态，数据库中有 {dbLockers.Count} 个柜子");

        int updatedCount = 0;

        // 创建数据库柜子的字典，便于快速查找
        var dbLockerDict = dbLockers.ToDictionary(l => (l.BoardAddress, l.ChannelNumber), l => l);

        foreach (var lockerItem in Lockers)
        {
            foreach (var smallLocker in lockerItem.SmallLockers)
            {
                // 查找对应的数据库记录
                var key = (smallLocker.BoardAddress, smallLocker.ChannelNumber);
                if (dbLockerDict.TryGetValue(key, out var dbLocker))
                {
                    // 只更新实际发生变化的状态，避免不必要的属性变更
                    bool shouldUpdate = false;

                    // 检查LockerId是否变化
                    if (smallLocker.LockerId != dbLocker.LockerId)
                    {
                        smallLocker.LockerId = dbLocker.LockerId;
                        shouldUpdate = true;
                    }

                    // 检查LockerName是否变化
                    if (smallLocker.LockerName != dbLocker.LockerName)
                    {
                        smallLocker.LockerName = dbLocker.LockerName;
                        shouldUpdate = true;
                    }

                    // 检查DisplayNumber是否变化
                    if (smallLocker.DisplayNumber != dbLocker.LockerName)
                    {
                        smallLocker.DisplayNumber = dbLocker.LockerName;
                        shouldUpdate = true;
                    }

                    // 检查Status是否变化
                    if (smallLocker.Status != dbLocker.Status)
                    {
                        smallLocker.Status = dbLocker.Status;
                        shouldUpdate = true;
                    }

                    // 检查IsAvailable是否变化
                    if (smallLocker.IsAvailable != dbLocker.IsAvailable)
                    {
                        smallLocker.IsAvailable = dbLocker.IsAvailable;
                        shouldUpdate = true;
                    }

                    // 检查IsOpened是否变化
                    if (smallLocker.IsOpened != dbLocker.IsOpened)
                    {
                        smallLocker.IsOpened = dbLocker.IsOpened;
                        shouldUpdate = true;
                    }

                    // 计算新的IsEnabled状态
                    var newIsEnabled = smallLocker.Status == LockerStatus.Available && smallLocker.IsAvailable;
                    if (smallLocker.IsEnabled != newIsEnabled)
                    {
                        smallLocker.IsEnabled = newIsEnabled;
                        shouldUpdate = true;
                    }

                    // 检查LastOpenedTime是否变化
                    if (dbLocker.LastOpened.HasValue)
                    {
                        if (smallLocker.LastOpenedTime != dbLocker.LastOpened.Value)
                        {
                            smallLocker.LastOpenedTime = dbLocker.LastOpened.Value;
                            shouldUpdate = true;
                        }
                    }
                    else if (smallLocker.LastOpenedTime != null)
                    {
                        smallLocker.LastOpenedTime = null;
                        shouldUpdate = true;
                    }

                    if (shouldUpdate)
                    {
                        updatedCount++;
                    }
                }
                else
                {
                    // 数据库中不存在的柜子，只有在当前不是离线状态时才更新
                    if (smallLocker.IsAvailable || smallLocker.IsOpened || smallLocker.LockerId > 0)
                    {
                        smallLocker.IsAvailable = false;
                        smallLocker.IsEnabled = false;
                        smallLocker.IsOpened = false;
                        smallLocker.LockerId = 0;
                        smallLocker.LockerName = string.Empty;
                        smallLocker.DisplayNumber = string.Empty;
                        updatedCount++;
                    }
                }
            }
        }

        _logger.LogDebug($"UI状态更新完成，共更新 {updatedCount} 个小格子");
    }
    #endregion

    #region 一键清柜
    /// <summary>
    /// 一键清柜操作
    /// </summary>
    private async Task ClearAll()
    {
        _logger.LogInformation("开始执行一键清柜操作");

        try
        {
            CanClearAll = false;
            IsOperating = true;

            // 确保锁控服务已初始化
            if (!await _lockControlService.InitializeLockControlAsync())
            {
                _logger.LogError("锁控服务未初始化，无法执行清柜操作");
                return;
            }

            _logger.LogInformation("锁控服务初始化成功，开始执行一键开锁");

            // 先更新所有格子的UI状态为打开
            int updatedCount = 0;
            foreach (var lockerItem in Lockers)
            {
                foreach (var smallLocker in lockerItem.SmallLockers)
                {
                    // 只更新可用且未打开的柜子
                    if (smallLocker.Status == LockerStatus.Available && smallLocker.IsAvailable && !smallLocker.IsOpened)
                    {
                        smallLocker.IsOpened = true;
                        smallLocker.IsEnabled = false;
                        smallLocker.LastOpenedTime = DateTime.Now;

                        // 立即更新数据库状态
                        await UpdateLockerStatusInDatabase(smallLocker, true);

                        updatedCount++;
                    }
                }
            }

            _logger.LogInformation($"已更新 {updatedCount} 个格子的UI状态为打开");

            // 执行一键开锁操作
            var result = await _lockControlService.OpenAllLocksAsync();

            _logger.LogInformation("一键清柜指令发送成功");

            // 记录管理员操作日志
            await _accessLogService.LogAccessAsync(1, "超级管理员", 0, "全部", AccessAction.AdminOpenAll, AccessResult.Success, $"管理员一键清柜执行成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "一键清柜操作异常");

            // 发生异常，恢复UI状态
            foreach (var lockerItem in Lockers)
            {
                foreach (var smallLocker in lockerItem.SmallLockers)
                {
                    if (smallLocker.Status == LockerStatus.Available && smallLocker.IsAvailable)
                    {
                        smallLocker.IsOpened = false;
                        smallLocker.IsEnabled = true; // 恢复按钮可用性

                        await UpdateLockerStatusInDatabase(smallLocker, false);
                    }
                }
            }
            await _accessLogService.LogAccessAsync(1, "超级管理员", 0, "全部", AccessAction.AdminOpenAll, AccessResult.Failed, $"管理员一键清柜执行异常:{ex.Message}");
        }
        finally
        {
            CanClearAll = true;
            IsOperating = false;
            _logger.LogInformation($"DEBUG: 一键清柜finally-解除IsOperating={IsOperating}, CanClearAll={CanClearAll}");
            await UpdateLockersStatusAsync();
            _logger.LogInformation("一键清柜操作完成");
        }
    }
    #endregion

    #region 资源释放
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("释放 AdminLockViewModel 资源");
        _statusUpdateTimer?.Stop();
        _statusUpdateTimer?.Dispose();

        // 清理命令
        OnLockerClickCommand?.Dispose();
        ClearAllCommand?.Dispose();

        _logger.LogInformation("AdminLockViewModel 资源释放完成");
    }
    #endregion
}

#region 大柜子项
/// <summary>
/// 大柜子项
/// </summary>
public partial class LockerItem : ViewModelBase
{
    private string _groupName = string.Empty;
    /// <summary>
    /// 柜子名称
    /// </summary>
    public string GroupName
    {
        get => _groupName;
        set => this.RaiseAndSetIfChanged(ref _groupName, value);
    }

    private IList<SmallLockerItem> _smallLockers = new List<SmallLockerItem>();
    /// <summary>
    /// 小格子列表
    /// </summary>
    public IList<SmallLockerItem> SmallLockers
    {
        get => _smallLockers;
        set => this.RaiseAndSetIfChanged(ref _smallLockers, value);
    }
}
#endregion

#region 小格子项
/// <summary>
/// 小格子项
/// </summary>
public partial class SmallLockerItem : ViewModelBase
{
    private string _number = string.Empty;
    /// <summary>
    /// 格子编号
    /// </summary>
    public string Number
    {
        get => _number;
        set => this.RaiseAndSetIfChanged(ref _number, value);
    }

    private string _displayNumber = string.Empty;
    /// <summary>
    /// 显示编号
    /// </summary>
    public string DisplayNumber
    {
        get => _displayNumber;
        set => this.RaiseAndSetIfChanged(ref _displayNumber, value);
    }

    private LockerStatus _status = LockerStatus.Available;
    /// <summary>
    /// 格子状态
    /// </summary>
    public LockerStatus Status
    {
        get => _status;
        set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(DisplayColor));
            this.RaisePropertyChanged(nameof(IsOffline));
        }
    }

    private bool _isAvailable = true;
    /// <summary>
    /// 是否可用
    /// </summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAvailable, value);
            this.RaisePropertyChanged(nameof(DisplayColor));
        }
    }

    private int _boardAddress = 1;
    /// <summary>
    /// 控制板地址
    /// </summary>
    public int BoardAddress
    {
        get => _boardAddress;
        set => this.RaiseAndSetIfChanged(ref _boardAddress, value);
    }

    private int _channelNumber = 1;
    /// <summary>
    /// 通道编号
    /// </summary>
    public int ChannelNumber
    {
        get => _channelNumber;
        set => this.RaiseAndSetIfChanged(ref _channelNumber, value);
    }

    private bool _isOpened = false;
    /// <summary>
    /// 是否已打开
    /// </summary>
    public bool IsOpened
    {
        get => _isOpened;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isOpened, value))
            {
                // 当IsOpened变化时，自动触发DisplayColor的变更通知
                this.RaisePropertyChanged(nameof(DisplayColor));
                this.RaisePropertyChanged(nameof(IsEnabled));
            }
        }
    }

    private DateTime? _lastOpenedTime = null;
    /// <summary>
    /// 最后打开时间
    /// </summary>
    public DateTime? LastOpenedTime
    {
        get => _lastOpenedTime;
        set => this.RaiseAndSetIfChanged(ref _lastOpenedTime, value);
    }

    private string _fullNumber = string.Empty;
    /// <summary>
    /// 完整格子编号
    /// </summary>
    public string FullNumber
    {
        get => _fullNumber;
        set => this.RaiseAndSetIfChanged(ref _fullNumber, value);
    }

    private long _lockerId = 0;
    /// <summary>
    /// 柜子ID
    /// </summary>
    public long LockerId
    {
        get => _lockerId;
        set => this.RaiseAndSetIfChanged(ref _lockerId, value);
    }

    private string _lockerName = string.Empty;
    /// <summary>
    /// 柜子名称
    /// </summary>
    public string LockerName
    {
        get => _lockerName;
        set => this.RaiseAndSetIfChanged(ref _lockerName, value);
    }

    /// <summary>
    /// 按钮是否可用
    /// </summary>
    private bool _isEnabled = true;
    /// <summary>
    /// 按钮是否可用
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isEnabled, value))
            {
                // 确保UI立即响应按钮状态变化
                this.RaisePropertyChanged(nameof(IsEnabled));
            }
        }
    }

    /// <summary>
    /// 是否离线（数据库中不存在）
    /// </summary>
    [NotMapped]
    public bool IsOffline => Status == LockerStatus.Disabled;
    /// <summary>
    /// 显示颜色 - 根据状态优先级和打开状态确定
    /// </summary>
    [NotMapped]
    public IBrush DisplayColor
    {
        get
        {
            // 状态优先级：屏幕占用 > 禁用 > 故障 > 可用

            // 深灰色 - 屏幕占用
            if (Status == LockerStatus.ScreenOccupied)
                return new SolidColorBrush(Color.Parse("#808080"));

            // 浅灰色 - 禁用
            if (Status == LockerStatus.Disabled)
                return new SolidColorBrush(Color.Parse("#D3D3D3"));

            // 浅红色 - 故障
            if (Status == LockerStatus.Fault)
                return new SolidColorBrush(Color.Parse("#FF0000"));

            // 浅橙色 - 已分配
            if (Status == LockerStatus.Assigned)
                return new SolidColorBrush(Color.Parse("#FFA500"));

            // 可用状态
            if (IsOpened)
                // 蓝色 - 可用且已打开
                return new SolidColorBrush(Color.Parse("#2196F3"));
            else
                // 浅绿色 - 可用且关闭
                return new SolidColorBrush(Color.Parse("#90EE90"));
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public SmallLockerItem()
    {
        this.WhenAnyValue(x => x.BoardAddress, x => x.ChannelNumber)
            .Subscribe(_ => UpdateFullNumber());

        // 监听状态变化，更新离线状态
        this.WhenAnyValue(x => x.Status)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsOffline)));
    }

    /// <summary>
    /// 更新完整编号
    /// </summary>
    private void UpdateFullNumber()
    {
        FullNumber = $"{BoardAddress}-{ChannelNumber:00}";
    }
}
#endregion