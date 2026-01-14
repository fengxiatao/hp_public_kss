using Avalonia.Threading;
using FaceLocker.Models;
using FaceLocker.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Threading.Tasks;

namespace FaceLocker.ViewModels
{
    /// <summary>
    /// 访问日志管理视图模型
    /// 提供开锁日志的显示、查询、分页等功能
    /// </summary>
    public class AdminAccessLogViewModel : ReactiveObject, IActivatableViewModel, IDisposable
    {
        #region 私有字段

        private readonly IAccessLogService _accessLogService;
        private readonly ILogger<AdminAccessLogViewModel> _logger;
        private bool _disposed = false;
        private bool _isLoading;
        private string _lockerFilter = string.Empty;
        private DateTimeOffset? _startDate;
        private DateTimeOffset? _endDate;
        private int _currentPage = 1;
        private int _pageSize = 20;
        private int _totalCount;
        private int _totalPages;
        private ObservableCollection<AccessLogItem> _accessLogs = new();
        private List<AccessLog> _allLogs = new();
        private bool _showNoDataMessage;

        #endregion

        #region 公共属性

        /// <summary>
        /// ViewModel激活器
        /// </summary>
        public ViewModelActivator Activator { get; } = new();

        /// <summary>
        /// 访问日志列表
        /// </summary>
        public ObservableCollection<AccessLogItem> AccessLogs
        {
            get => _accessLogs;
            private set => this.RaiseAndSetIfChanged(ref _accessLogs, value);
        }

        /// <summary>
        /// 是否正在加载
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isLoading, value);
                UpdateShowNoDataMessage();
            }
        }

        /// <summary>
        /// 柜子名称过滤条件
        /// </summary>
        public string LockerFilter
        {
            get => _lockerFilter;
            set => this.RaiseAndSetIfChanged(ref _lockerFilter, value);
        }

        /// <summary>
        /// 开始日期过滤条件
        /// </summary>
        public DateTimeOffset? StartDate
        {
            get => _startDate;
            set => this.RaiseAndSetIfChanged(ref _startDate, value);
        }

        /// <summary>
        /// 结束日期过滤条件
        /// </summary>
        public DateTimeOffset? EndDate
        {
            get => _endDate;
            set => this.RaiseAndSetIfChanged(ref _endDate, value);
        }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage
        {
            get => _currentPage;
            private set => this.RaiseAndSetIfChanged(ref _currentPage, value);
        }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value != _pageSize)
                {
                    this.RaiseAndSetIfChanged(ref _pageSize, value);
                    CurrentPage = 1; // 重置到第一页
                    _ = LoadAccessLogsAsync();
                }
            }
        }

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount
        {
            get => _totalCount;
            private set => this.RaiseAndSetIfChanged(ref _totalCount, value);
        }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages
        {
            get => _totalPages;
            private set => this.RaiseAndSetIfChanged(ref _totalPages, value);
        }

        /// <summary>
        /// 可用的每页大小选项
        /// </summary>
        public List<int> PageSizes { get; } = new() { 10, 20, 50, 100 };

        /// <summary>
        /// 是否可以前往上一页
        /// </summary>
        public bool CanGoToPreviousPage => CurrentPage > 1;

        /// <summary>
        /// 是否可以前往下一页
        /// </summary>
        public bool CanGoToNextPage => CurrentPage < TotalPages;

        /// <summary>
        /// 是否可以前往第一页
        /// </summary>
        public bool CanGoToFirstPage => CurrentPage > 1;

        /// <summary>
        /// 是否可以前往最后一页
        /// </summary>
        public bool CanGoToLastPage => CurrentPage < TotalPages;

        private bool _hasData = false;
        /// <summary>
        /// 是否有数据
        /// </summary>
        public bool HasData
        {
            get => _hasData;
            private set
            {
                this.RaiseAndSetIfChanged(ref _hasData, value);
                UpdateShowNoDataMessage();
            }
        }

        /// <summary>
        /// 是否显示无数据消息
        /// </summary>
        public bool ShowNoDataMessage
        {
            get => _showNoDataMessage;
            private set => this.RaiseAndSetIfChanged(ref _showNoDataMessage, value);
        }

        #endregion

        #region 命令

        /// <summary>
        /// 搜索命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> SearchCommand { get; }

        /// <summary>
        /// 重置搜索命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ResetSearchCommand { get; }

        /// <summary>
        /// 上一页命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

        /// <summary>
        /// 下一页命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> NextPageCommand { get; }

        /// <summary>
        /// 第一页命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> FirstPageCommand { get; }

        /// <summary>
        /// 最后一页命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> LastPageCommand { get; }

        /// <summary>
        /// 刷新命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化访问日志管理视图模型
        /// </summary>
        /// <param name="accessLogService">访问日志服务</param>
        /// <param name="logger">日志记录器</param>
        public AdminAccessLogViewModel(IAccessLogService accessLogService, ILogger<AdminAccessLogViewModel> logger)
        {
            _accessLogService = accessLogService ?? throw new ArgumentNullException(nameof(accessLogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("AdminAccessLogViewModel 初始化开始");

            try
            {
                // 初始化默认日期范围（最近30天）
                EndDate = DateTimeOffset.Now.Date;
                StartDate = EndDate.Value.AddDays(-30);

                // 初始化命令
                SearchCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    _logger.LogInformation("执行搜索命令，柜子过滤：{LockerFilter}，日期范围：{StartDate} 到 {EndDate}",
                        LockerFilter, StartDate?.ToString("yyyy-MM-dd"), EndDate?.ToString("yyyy-MM-dd"));
                    CurrentPage = 1;
                    await LoadAccessLogsAsync();
                });

                ResetSearchCommand = ReactiveCommand.Create(() =>
                {
                    _logger.LogInformation("执行重置搜索命令");
                    LockerFilter = string.Empty;
                    StartDate = DateTimeOffset.Now.Date.AddDays(-30);
                    EndDate = DateTimeOffset.Now.Date;
                    CurrentPage = 1;
                    _ = LoadAccessLogsAsync();
                });

                PreviousPageCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (CanGoToPreviousPage)
                    {
                        _logger.LogDebug("执行上一页命令，从第 {CurrentPage} 页到第 {PreviousPage} 页", CurrentPage, CurrentPage - 1);
                        CurrentPage--;
                        await LoadAccessLogsAsync();
                    }
                });

                NextPageCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (CanGoToNextPage)
                    {
                        _logger.LogDebug("执行下一页命令，从第 {CurrentPage} 页到第 {NextPage} 页", CurrentPage, CurrentPage + 1);
                        CurrentPage++;
                        await LoadAccessLogsAsync();
                    }
                });

                FirstPageCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (CanGoToFirstPage)
                    {
                        _logger.LogDebug("执行第一页命令，从第 {CurrentPage} 页到第 1 页", CurrentPage);
                        CurrentPage = 1;
                        await LoadAccessLogsAsync();
                    }
                });

                LastPageCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (CanGoToLastPage)
                    {
                        _logger.LogDebug("执行最后一页命令，从第 {CurrentPage} 页到第 {TotalPages} 页", CurrentPage, TotalPages);
                        CurrentPage = TotalPages;
                        await LoadAccessLogsAsync();
                    }
                });

                RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    _logger.LogInformation("执行刷新命令");
                    await LoadAccessLogsAsync();
                });

                // 使用 WhenActivated
                this.WhenActivated((System.Reactive.Disposables.CompositeDisposable disposables) =>
                {
                    _logger.LogInformation("AdminAccessLogViewModel 激活，开始加载日志数据");
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await LoadAccessLogsAsync();
                    });
                });

                _logger.LogInformation("AdminAccessLogViewModel 初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminAccessLogViewModel 初始化过程中发生异常");
                throw;
            }
        }

        #endregion

        #region 数据加载方法

        /// <summary>
        /// 加载访问日志数据
        /// </summary>
        private async Task LoadAccessLogsAsync()
        {
            try
            {
                IsLoading = true;
                _logger.LogInformation("开始加载访问日志数据，页码：{Page}，每页大小：{PageSize}", CurrentPage, PageSize);

                // 构建查询条件
                long lockerId = 0;
                if (!string.IsNullOrWhiteSpace(LockerFilter))
                {
                    // 这里可以根据柜子名称查询柜子ID
                    // 暂时使用0表示不按柜子ID过滤
                }

                // 将DateTimeOffset?转换为DateTime?传递给服务层
                DateTime? startDate = null;
                DateTime? endDate = null;

                if (StartDate.HasValue)
                {
                    startDate = StartDate.Value.UtcDateTime.Date;
                }

                if (EndDate.HasValue)
                {
                    endDate = EndDate.Value.UtcDateTime.Date.AddDays(1);
                }

                _logger.LogDebug("搜索条件 - 日期范围: {StartDate} 到 {EndDate}",
                    startDate?.ToString("yyyy-MM-dd"), endDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                // 调用服务获取分页数据
                var (logs, totalCount) = await _accessLogService.GetAccessLogsPagedAsync(
                    CurrentPage,
                    PageSize,
                    keywords: LockerFilter,
                    startDate: startDate,
                    endDate: endDate);

                _allLogs = logs;
                TotalCount = totalCount;
                TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
                HasData = TotalCount > 0;

                // 转换为显示项
                var accessLogItems = new List<AccessLogItem>();
                int startIndex = (CurrentPage - 1) * PageSize + 1;

                foreach (var log in logs)
                {
                    accessLogItems.Add(new AccessLogItem(log, startIndex++));
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AccessLogs.Clear();
                    foreach (var item in accessLogItems)
                    {
                        AccessLogs.Add(item);
                    }
                });

                _logger.LogInformation("访问日志数据加载完成，共 {TotalCount} 条记录，当前显示 {DisplayCount} 条",
                    TotalCount, logs.Count);
                _logger.LogDebug("数据状态: HasData={HasData}", HasData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载访问日志数据时发生异常");
                await ShowErrorMessageAsync("加载日志数据失败", ex.Message);
                HasData = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 更新显示无数据消息的状态
        /// </summary>
        private void UpdateShowNoDataMessage()
        {
            ShowNoDataMessage = !IsLoading && !HasData;
        }

        /// <summary>
        /// 激活ViewModel
        /// </summary>
        public async Task ActivateAsync()
        {
            _logger.LogInformation("激活 AdminAccessLogViewModel");
            await LoadAccessLogsAsync();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 显示错误消息
        /// </summary>
        private async Task ShowErrorMessageAsync(string title, string message)
        {
            _logger.LogWarning("显示错误消息：{Title} - {Message}", title, message);
            // 这里可以使用对话框服务显示消息
            // 暂时记录日志
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.LogInformation("释放 AdminAccessLogViewModel 资源");

            try
            {
                // 清理命令
                SearchCommand?.Dispose();
                ResetSearchCommand?.Dispose();
                PreviousPageCommand?.Dispose();
                NextPageCommand?.Dispose();
                FirstPageCommand?.Dispose();
                LastPageCommand?.Dispose();
                RefreshCommand?.Dispose();

                // 清理集合
                AccessLogs?.Clear();
                _allLogs?.Clear();

                _disposed = true;
                _logger.LogInformation("AdminAccessLogViewModel 资源释放完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放 AdminAccessLogViewModel 资源时发生异常");
            }
        }

        #endregion
    }

    /// <summary>
    /// 访问日志显示项
    /// 用于在界面上显示访问日志信息
    /// </summary>
    public class AccessLogItem
    {
        #region 构造函数

        /// <summary>
        /// 初始化访问日志显示项
        /// </summary>
        /// <param name="log">访问日志对象</param>
        /// <param name="rowIndex">行索引</param>
        public AccessLogItem(AccessLog log, int rowIndex)
        {
            Log = log;
            RowIndex = rowIndex;
        }

        #endregion

        #region 属性

        /// <summary>
        /// 原始访问日志对象
        /// </summary>
        public AccessLog Log { get; }

        /// <summary>
        /// 行索引
        /// </summary>
        public int RowIndex { get; }

        /// <summary>
        /// 日志ID
        /// </summary>
        public long LogId => Log.Id;

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId => Log.UserId;

        /// <summary>
        /// 用户姓名
        /// </summary>
        public string UserName => Log.UserName ?? "未知用户";

        /// <summary>
        /// 柜子ID
        /// </summary>
        public long LockerId => Log.LockerId;

        /// <summary>
        /// 柜子名称
        /// </summary>
        public string LockerName => Log.LockerName ?? "未知柜子";

        /// <summary>
        /// 操作类型
        /// </summary>
        public string ActionType => Log.ActionText;

        /// <summary>
        /// 操作结果
        /// </summary>
        public string Result => Log.ResultText;

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Details => Log.Details ?? string.Empty;

        /// <summary>
        /// 操作时间
        /// </summary>
        public string Timestamp
        {
            get
            {
                try
                {
                    return Log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
                catch
                {
                    return "日期格式错误";
                }
            }
        }

        /// <summary>
        /// 操作摘要
        /// </summary>
        public string Summary => Log.Summary;

        #endregion

        #region 状态相关属性

        /// <summary>
        /// 成功操作的背景颜色
        /// </summary>
        public string ResultBackgroundColor => Log.Result == AccessResult.Success ? "#d4edda" : "#f8d7da";

        /// <summary>
        /// 成功操作的前景颜色
        /// </summary>
        public string ResultForegroundColor => Log.Result == AccessResult.Success ? "#155724" : "#721c24";

        /// <summary>
        /// 操作类型图标
        /// </summary>
        public string ActionIcon => Log.Action switch
        {
            AccessAction.Store => "📥",
            AccessAction.Rerieve => "📤",
            AccessAction.AdminOpenAll => "🗃️",
            AccessAction.AdminOpenLocker => "🔓",
            _ => "❓"
        };

        #endregion
    }
}
