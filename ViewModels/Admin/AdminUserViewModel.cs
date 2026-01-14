using Avalonia.Collections;
using FaceLocker.Models;
using FaceLocker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace FaceLocker.ViewModels;

/// <summary>
/// 用户管理 ViewModel
/// </summary>
public class AdminUserViewModel : ViewModelBase, IDisposable
{
    #region 私有字段
    private readonly ILogger<AdminUserViewModel> _logger;
    private bool _isInitialized = false;
    private string _searchKeyword = string.Empty;
    private bool _disposed = false;
    #endregion

    #region 构造函数
    /// <summary>
    /// 构造函数
    /// </summary>
    public AdminUserViewModel(
        ILogger<AdminUserViewModel> logger)
    {
        _logger = logger;

        _logger.LogInformation("AdminUserViewModel 初始化开始");

        // 初始化分页相关属性
        PageSizes = new ObservableCollection<int> { 10, 20, 50, 100 };
        PageSize = 10;
        CurrentPage = 1;
        TotalCount = 0;

        // 初始化命令
        InitializeCommands();

        _logger.LogInformation("AdminUserViewModel 初始化完成");
    }
    #endregion

    #region 属性定义
    private AvaloniaList<UserItem> _users = new AvaloniaList<UserItem>();
    /// <summary>
    /// 用户列表
    /// </summary>
    public AvaloniaList<UserItem> Users
    {
        get => _users;
        set => this.RaiseAndSetIfChanged(ref _users, value);
    }

    private ObservableCollection<int> _pageSizes = new ObservableCollection<int>();
    /// <summary>
    /// 分页大小选项
    /// </summary>
    public ObservableCollection<int> PageSizes
    {
        get => _pageSizes;
        set => this.RaiseAndSetIfChanged(ref _pageSizes, value);
    }

    private int _pageSize = 10;
    /// <summary>
    /// 每页显示数量
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _pageSize, value);
            _ = Task.Run(async () => await LoadUsersAsync());
        }
    }

    private int _currentPage = 1;
    /// <summary>
    /// 当前页码
    /// </summary>
    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPage, value);
            _ = Task.Run(async () => await LoadUsersAsync());
        }
    }

    private int _totalCount = 0;
    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount
    {
        get => _totalCount;
        set => this.RaiseAndSetIfChanged(ref _totalCount, value);
    }

    private int _totalPages = 0;
    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages
    {
        get => _totalPages;
        set => this.RaiseAndSetIfChanged(ref _totalPages, value);
    }

    private bool _canGoToFirstPage = false;
    /// <summary>
    /// 是否可以跳转到首页
    /// </summary>
    public bool CanGoToFirstPage
    {
        get => _canGoToFirstPage;
        set => this.RaiseAndSetIfChanged(ref _canGoToFirstPage, value);
    }

    private bool _canGoToPreviousPage = false;
    /// <summary>
    /// 是否可以跳转到上一页
    /// </summary>
    public bool CanGoToPreviousPage
    {
        get => _canGoToPreviousPage;
        set => this.RaiseAndSetIfChanged(ref _canGoToPreviousPage, value);
    }

    private bool _canGoToNextPage = false;
    /// <summary>
    /// 是否可以跳转到下一页
    /// </summary>
    public bool CanGoToNextPage
    {
        get => _canGoToNextPage;
        set => this.RaiseAndSetIfChanged(ref _canGoToNextPage, value);
    }

    private bool _canGoToLastPage = false;
    /// <summary>
    /// 是否可以跳转到末页
    /// </summary>
    public bool CanGoToLastPage
    {
        get => _canGoToLastPage;
        set => this.RaiseAndSetIfChanged(ref _canGoToLastPage, value);
    }

    private bool _isLoading = false;
    /// <summary>
    /// 是否正在加载数据
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// 搜索关键词
    /// </summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set => this.RaiseAndSetIfChanged(ref _searchKeyword, value);
    }

    private bool _hasNoData = false;
    /// <summary>
    /// 是否没有数据
    /// </summary>
    public bool HasNoData
    {
        get => _hasNoData;
        set => this.RaiseAndSetIfChanged(ref _hasNoData, value);
    }

    private string _noDataMessage = "暂无数据";
    /// <summary>
    /// 无数据提示消息
    /// </summary>
    public string NoDataMessage
    {
        get => _noDataMessage;
        set => this.RaiseAndSetIfChanged(ref _noDataMessage, value);
    }
    #endregion

    #region 命令定义
    /// <summary>
    /// 首页命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> FirstPageCommand { get; private set; } = null!;

    /// <summary>
    /// 上一页命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; private set; } = null!;

    /// <summary>
    /// 下一页命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; private set; } = null!;

    /// <summary>
    /// 末页命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> LastPageCommand { get; private set; } = null!;

    /// <summary>
    /// 搜索命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SearchCommand { get; private set; } = null!;

    /// <summary>
    /// 重置搜索命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetSearchCommand { get; private set; } = null!;

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        _logger.LogDebug("初始化 AdminUserViewModel 命令");

        // 分页命令
        FirstPageCommand = ReactiveCommand.Create(GoToFirstPage);
        PreviousPageCommand = ReactiveCommand.Create(GoToPreviousPage);
        NextPageCommand = ReactiveCommand.Create(GoToNextPage);
        LastPageCommand = ReactiveCommand.Create(GoToLastPage);

        // 搜索命令
        SearchCommand = ReactiveCommand.CreateFromTask(SearchUsers);
        ResetSearchCommand = ReactiveCommand.Create(ResetSearch);

        _logger.LogDebug("AdminUserViewModel 命令初始化完成");
    }
    #endregion

    #region 公共方法
    /// <summary>
    /// 激活视图时调用
    /// </summary>
    public async Task ActivateAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("AdminUserViewModel 激活");

        if (!_isInitialized)
        {
            _logger.LogInformation("首次激活，加载用户数据");
            await LoadUsersAsync();
            _isInitialized = true;
        }
        else
        {
            _logger.LogDebug("已经初始化过，跳过数据加载");
        }
    }
    #endregion

    #region 数据加载
    /// <summary>
    /// 加载用户数据
    /// </summary>
    private async Task LoadUsersAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("开始加载用户数据，页码: {CurrentPage}, 每页: {PageSize}, 搜索关键词: {SearchKeyword}",
            CurrentPage, PageSize, SearchKeyword);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
        });

        try
        {
            // 使用新的作用域创建 DbContext，避免并发问题
            using var scope = App.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FaceLockerDbContext>();

            IQueryable<User> query = dbContext.Users;

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                query = query.Where(u => u.Name.Contains(SearchKeyword) ||
                                           u.UserNumber.Contains(SearchKeyword) ||
                                           u.Department.Contains(SearchKeyword));
            }

            // 获取总记录数
            var totalCount = await query.CountAsync();

            // 从数据库读取用户分页列表
            var users = await query
                .OrderBy(u => u.UserNumber)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            var roles = await dbContext.Roles.OrderBy(r => r.Id).ToListAsync();

            // 转换为显示模型
            var userItems = new List<UserItem>();
            foreach (var user in users)
            {
                // 获取角色名称
                var role = roles.FirstOrDefault(o => o.Id == user.RoleId);

                var userItem = new UserItem
                {
                    UserId = user.UserNumber,
                    UserName = user.Name,
                    RoleName = role == null ? "" : role.Name,
                    AvatarPath = user.AvatarPath ?? "avares://FaceLocker/Assets/default-avatar.png",
                    HasFaceFeature = user.HasFaceFeature,
                    FaceFeatureVersion = user.FaceFeatureVersion.ToString(),
                    CreatedTime = user.CreatedAt,
                    OriginalUser = user
                };

                userItems.Add(userItem);
            }

            // 在UI线程更新所有状态
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                TotalCount = totalCount;
                TotalPages = (int)Math.Ceiling((double)totalCount / PageSize);

                // 更新分页按钮状态
                UpdatePaginationState();

                // 先清空再添加，避免重复
                Users.Clear();
                Users.AddRange(userItems);

                // 更新无数据状态
                UpdateNoDataState(userItems.Count);

                IsLoading = false;
            });

            _logger.LogInformation("用户数据加载完成，共 {TotalCount} 条记录，当前显示 {UserCount} 条",
                totalCount, userItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载用户数据时发生异常");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                HasNoData = true;
                NoDataMessage = "加载数据时发生错误";
                IsLoading = false;
            });
        }
    }

    /// <summary>
    /// 搜索用户
    /// </summary>
    private async Task SearchUsers()
    {
        if (_disposed) return;

        _logger.LogInformation("开始搜索用户，关键词: {SearchKeyword}", SearchKeyword);

        // 重置到第一页进行搜索
        CurrentPage = 1;
        await LoadUsersAsync();
    }

    /// <summary>
    /// 重置搜索
    /// </summary>
    private void ResetSearch()
    {
        if (_disposed) return;

        _logger.LogInformation("重置搜索");

        SearchKeyword = string.Empty;
        CurrentPage = 1;
        _ = Task.Run(async () => await LoadUsersAsync());
    }

    /// <summary>
    /// 更新分页状态
    /// </summary>
    private void UpdatePaginationState()
    {
        CanGoToFirstPage = CurrentPage > 1;
        CanGoToPreviousPage = CurrentPage > 1;
        CanGoToNextPage = CurrentPage < TotalPages;
        CanGoToLastPage = CurrentPage < TotalPages;

        _logger.LogDebug("分页状态更新 - 当前页: {CurrentPage}, 总页数: {TotalPages}, 首页: {FirstPage}, 上页: {PreviousPage}, 下页: {NextPage}, 末页: {LastPage}", CurrentPage, TotalPages, CanGoToFirstPage, CanGoToPreviousPage, CanGoToNextPage, CanGoToLastPage);
    }

    /// <summary>
    /// 更新无数据状态
    /// </summary>
    private void UpdateNoDataState(int userCount)
    {
        HasNoData = userCount == 0;

        if (HasNoData)
        {
            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                NoDataMessage = $"未找到包含\"{SearchKeyword}\"的用户";
            }
            else
            {
                NoDataMessage = "暂无用户数据";
            }
        }
    }
    #endregion

    #region 命令处理方法
    /// <summary>
    /// 跳转到首页
    /// </summary>
    private void GoToFirstPage()
    {
        if (_disposed) return;

        _logger.LogInformation("跳转到首页");
        CurrentPage = 1;
    }

    /// <summary>
    /// 跳转到上一页
    /// </summary>
    private void GoToPreviousPage()
    {
        if (_disposed) return;

        if (CurrentPage > 1)
        {
            _logger.LogInformation("跳转到上一页，从 {CurrentPage} 到 {PreviousPage}", CurrentPage, CurrentPage - 1);
            CurrentPage--;
        }
    }

    /// <summary>
    /// 跳转到下一页
    /// </summary>
    private void GoToNextPage()
    {
        if (_disposed) return;

        if (CurrentPage < TotalPages)
        {
            _logger.LogInformation("跳转到下一页，从 {CurrentPage} 到 {NextPage}", CurrentPage, CurrentPage + 1);
            CurrentPage++;
        }
    }

    /// <summary>
    /// 跳转到末页
    /// </summary>
    private void GoToLastPage()
    {
        if (_disposed) return;

        _logger.LogInformation("跳转到末页: {TotalPages}", TotalPages);
        CurrentPage = TotalPages;
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

        _logger.LogInformation("释放 AdminUserViewModel 资源");

        try
        {
            _disposed = true;

            // 清理命令
            FirstPageCommand?.Dispose();
            PreviousPageCommand?.Dispose();
            NextPageCommand?.Dispose();
            LastPageCommand?.Dispose();
            SearchCommand?.Dispose();
            ResetSearchCommand?.Dispose();

            // 清空数据
            Users?.Clear();
            PageSizes?.Clear();

            _logger.LogInformation("AdminUserViewModel 资源释放完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放 AdminUserViewModel 资源时发生异常");
        }
    }
    #endregion
}

#region 用户项显示模型
/// <summary>
/// 用户项显示模型
/// </summary>
public class UserItem : ViewModelBase
{
    private string _userId = string.Empty;
    /// <summary>
    /// 用户编号
    /// </summary>
    public string UserId
    {
        get => _userId;
        set => this.RaiseAndSetIfChanged(ref _userId, value);
    }

    private string _userName = string.Empty;
    /// <summary>
    /// 用户姓名
    /// </summary>
    public string UserName
    {
        get => _userName;
        set => this.RaiseAndSetIfChanged(ref _userName, value);
    }

    private string _roleName = string.Empty;
    /// <summary>
    /// 角色名称
    /// </summary>
    public string RoleName
    {
        get => _roleName;
        set => this.RaiseAndSetIfChanged(ref _roleName, value);
    }

    private string _avatarPath = string.Empty;
    /// <summary>
    /// 头像路径
    /// </summary>
    public string AvatarPath
    {
        get => _avatarPath;
        set => this.RaiseAndSetIfChanged(ref _avatarPath, value);
    }

    private bool _hasFaceFeature = false;
    /// <summary>
    /// 是否有人脸特征
    /// </summary>
    public bool HasFaceFeature
    {
        get => _hasFaceFeature;
        set => this.RaiseAndSetIfChanged(ref _hasFaceFeature, value);
    }

    private string _faceFeatureVersion = string.Empty;
    /// <summary>
    /// 人脸特征版本
    /// </summary>
    public string FaceFeatureVersion
    {
        get => _faceFeatureVersion;
        set => this.RaiseAndSetIfChanged(ref _faceFeatureVersion, value);
    }

    private DateTime _createdTime;
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime
    {
        get => _createdTime;
        set => this.RaiseAndSetIfChanged(ref _createdTime, value);
    }

    private User _originalUser = null!;
    /// <summary>
    /// 原始用户对象
    /// </summary>
    public User OriginalUser
    {
        get => _originalUser;
        set => this.RaiseAndSetIfChanged(ref _originalUser, value);
    }
}
#endregion