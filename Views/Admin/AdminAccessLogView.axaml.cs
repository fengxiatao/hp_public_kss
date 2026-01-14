using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FaceLocker.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Views
{
    /// <summary>
    /// 访问日志管理视图
    /// 提供开锁日志的显示、查询和分页功能界面
    /// </summary>
    public partial class AdminAccessLogView : UserControl
    {
        #region 私有字段

        private readonly ILogger<AdminAccessLogView> _logger;
        private AdminAccessLogViewModel? _viewModel;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化访问日志管理视图
        /// </summary>
        public AdminAccessLogView()
        {
            _logger = App.GetService<ILogger<AdminAccessLogView>>();
            _logger.LogInformation("AdminAccessLogView 开始初始化");

            try
            {
                InitializeComponent();
                _logger.LogDebug("AdminAccessLogView 组件初始化完成");

                this.DataContextChanged += OnDataContextChanged;
                _logger.LogInformation("AdminAccessLogView 初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminAccessLogView 初始化过程中发生异常");
                throw;
            }
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化组件
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _logger.LogDebug("AdminAccessLogView XAML 加载完成");
        }

        /// <summary>
        /// 数据上下文变化事件处理
        /// </summary>
        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            _logger.LogDebug("AdminAccessLogView 数据上下文变化，新类型：{DataType}", DataContext?.GetType().Name);

            if (DataContext is AdminAccessLogViewModel viewModel)
            {
                _viewModel = viewModel;

                // 延迟设置绑定，确保在UI线程
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        SetupBindings();
                        _logger.LogDebug("AdminAccessLogView 绑定设置完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AdminAccessLogView 设置绑定时发生异常");
                    }
                });

                // 初始化日期选择器
                InitializeDatePicker();

                // 异步激活ViewModel
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("开始异步激活 AdminAccessLogViewModel");
                        await viewModel.ActivateAsync();
                        _logger.LogInformation("AdminAccessLogViewModel 异步激活完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "激活 AdminAccessLogViewModel 时发生异常");
                    }
                });
            }
            else
            {
                _logger.LogWarning("AdminAccessLogView 的数据上下文类型不是 AdminAccessLogViewModel");
            }
        }

        #endregion

        #region 控件事件处理

        /// <summary>
        /// 控件加载完成事件
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _logger.LogInformation("AdminAccessLogView 已加载到界面");
        }

        /// <summary>
        /// 控件卸载事件
        /// </summary>
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _logger.LogInformation("AdminAccessLogView 已从界面卸载");
        }

        #endregion

        #region 绑定设置

        /// <summary>
        /// 设置数据绑定
        /// </summary>
        private void SetupBindings()
        {
            if (_viewModel == null)
            {
                _logger.LogWarning("尝试设置绑定，但 ViewModel 为空");
                return;
            }

            try
            {
                // 设置分页控件
                if (this.FindControl<ComboBox>("PageSizeComboBox") is ComboBox pageSizeCombo)
                {
                    _logger.LogDebug("找到分页大小组合框，设置绑定");
                    pageSizeCombo.ItemsSource = _viewModel.PageSizes;
                    pageSizeCombo.SelectedItem = _viewModel.PageSize;
                    pageSizeCombo.SelectionChanged += (s, e) =>
                    {
                        if (pageSizeCombo.SelectedItem is int size)
                        {
                            _logger.LogDebug("分页大小改变：{PageSize}", size);
                            _viewModel.PageSize = size;
                        }
                    };
                }
                else
                {
                    _logger.LogWarning("未找到分页大小组合框");
                }

                // 设置分页按钮
                SetupPaginationButtons();

                // 设置日期选择器
                SetupDatePickerBindings();

                // 设置日志列表
                if (this.FindControl<ItemsControl>("AccessLogsItemsControl") is ItemsControl logsControl)
                {
                    _logger.LogDebug("找到访问日志列表控件，设置绑定");
                    logsControl.ItemsSource = _viewModel.AccessLogs;
                }
                else
                {
                    _logger.LogWarning("未找到访问日志列表控件");
                }

                // 监听属性变化
                _viewModel.PropertyChanged += (s, e) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        HandlePropertyChanged(e.PropertyName);
                    });
                };

                // 初始更新UI状态
                UpdatePaginationText();
                UpdatePaginationButtons();

                _logger.LogInformation("AdminAccessLogView 所有绑定设置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置绑定时发生异常");
            }
        }

        /// <summary>
        /// 设置分页按钮
        /// </summary>
        private void SetupPaginationButtons()
        {
            try
            {
                // 第一页按钮
                if (this.FindControl<Button>("FirstPageButton") is Button firstPageButton)
                {
                    _logger.LogDebug("找到第一页按钮");
                    firstPageButton.Command = _viewModel!.FirstPageCommand;
                    firstPageButton.IsEnabled = _viewModel.CanGoToFirstPage;
                }

                // 上一页按钮
                if (this.FindControl<Button>("PreviousPageButton") is Button previousPageButton)
                {
                    _logger.LogDebug("找到上一页按钮");
                    previousPageButton.Command = _viewModel!.PreviousPageCommand;
                    previousPageButton.IsEnabled = _viewModel.CanGoToPreviousPage;
                }

                // 下一页按钮
                if (this.FindControl<Button>("NextPageButton") is Button nextPageButton)
                {
                    _logger.LogDebug("找到下一页按钮");
                    nextPageButton.Command = _viewModel!.NextPageCommand;
                    nextPageButton.IsEnabled = _viewModel.CanGoToNextPage;
                }

                // 最后一页按钮
                if (this.FindControl<Button>("LastPageButton") is Button lastPageButton)
                {
                    _logger.LogDebug("找到最后一页按钮");
                    lastPageButton.Command = _viewModel!.LastPageCommand;
                    lastPageButton.IsEnabled = _viewModel.CanGoToLastPage;
                }

                _logger.LogDebug("分页按钮设置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置分页按钮时发生异常");
            }
        }

        /// <summary>
        /// 处理属性变化
        /// </summary>
        private void HandlePropertyChanged(string? propertyName)
        {
            if (_viewModel == null)
            {
                _logger.LogWarning("处理属性变化时，ViewModel 为空");
                return;
            }

            try
            {
                _logger.LogDebug("处理属性变化：{PropertyName}", propertyName);

                switch (propertyName)
                {
                    case nameof(AdminAccessLogViewModel.TotalCount):
                    case nameof(AdminAccessLogViewModel.CurrentPage):
                    case nameof(AdminAccessLogViewModel.TotalPages):
                        UpdatePaginationText();
                        UpdatePaginationButtons();
                        break;

                    case nameof(AdminAccessLogViewModel.AccessLogs):
                        if (this.FindControl<ItemsControl>("AccessLogsItemsControl") is ItemsControl logsControl)
                        {
                            _logger.LogDebug("更新访问日志列表数据源");
                            logsControl.ItemsSource = _viewModel.AccessLogs;
                        }
                        break;

                    case nameof(AdminAccessLogViewModel.PageSize):
                        if (this.FindControl<ComboBox>("PageSizeComboBox") is ComboBox pageSizeCombo)
                        {
                            _logger.LogDebug("更新分页大小组合框选择项");
                            pageSizeCombo.SelectedItem = _viewModel.PageSize;
                        }
                        break;

                    case nameof(AdminAccessLogViewModel.LockerFilter):
                        _logger.LogDebug("柜子过滤器内容变更：{LockerFilter}", _viewModel.LockerFilter);
                        break;

                    case nameof(AdminAccessLogViewModel.IsLoading):
                        _logger.LogDebug("加载状态改变：{IsLoading}", _viewModel.IsLoading);
                        break;

                    case nameof(AdminAccessLogViewModel.HasData):
                        _logger.LogDebug("数据状态改变：{HasData}", _viewModel.HasData);
                        break;

                    case nameof(AdminAccessLogViewModel.ShowNoDataMessage):
                        _logger.LogDebug("显示无数据消息状态改变：{ShowNoDataMessage}", _viewModel.ShowNoDataMessage);
                        break;

                    default:
                        _logger.LogTrace("处理未特别处理的属性变化：{PropertyName}", propertyName);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理属性变化时发生异常，属性名：{PropertyName}", propertyName);
            }
        }

        /// <summary>
        /// 更新分页文本显示
        /// </summary>
        private void UpdatePaginationText()
        {
            if (_viewModel == null) return;

            try
            {
                // 直接通过XAML绑定显示，这里可以添加额外的UI更新逻辑
                _logger.LogTrace("更新分页文本：第 {CurrentPage} 页，共 {TotalPages} 页，总记录数 {TotalCount}",
                    _viewModel.CurrentPage, _viewModel.TotalPages, _viewModel.TotalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分页文本时发生异常");
            }
        }

        /// <summary>
        /// 更新分页按钮状态
        /// </summary>
        private void UpdatePaginationButtons()
        {
            if (_viewModel == null) return;

            try
            {
                // 第一页按钮
                if (this.FindControl<Button>("FirstPageButton") is Button firstPageButton)
                    firstPageButton.IsEnabled = _viewModel.CanGoToFirstPage;

                // 上一页按钮
                if (this.FindControl<Button>("PreviousPageButton") is Button previousPageButton)
                    previousPageButton.IsEnabled = _viewModel.CanGoToPreviousPage;

                // 下一页按钮
                if (this.FindControl<Button>("NextPageButton") is Button nextPageButton)
                    nextPageButton.IsEnabled = _viewModel.CanGoToNextPage;

                // 最后一页按钮
                if (this.FindControl<Button>("LastPageButton") is Button lastPageButton)
                    lastPageButton.IsEnabled = _viewModel.CanGoToLastPage;

                _logger.LogTrace("更新分页按钮状态：上一页={CanPrev}, 下一页={CanNext}",
                    _viewModel.CanGoToPreviousPage, _viewModel.CanGoToNextPage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分页按钮状态时发生异常");
            }
        }

        #endregion

        #region 日期选择器处理
        /// <summary>
        /// 初始化日期选择器
        /// </summary>
        private void InitializeDatePicker()
        {
            try
            {
                // 初始化开始日期选择器
                if (this.FindControl<DatePicker>("StartDatePicker") is DatePicker startDatePicker)
                {
                    _logger.LogDebug("初始化开始日期选择器");
                    // 确保正确处理日期，不依赖时区
                    startDatePicker.SelectedDate = _viewModel?.StartDate?.DateTime;
                }

                // 初始化结束日期选择器
                if (this.FindControl<DatePicker>("EndDatePicker") is DatePicker endDatePicker)
                {
                    _logger.LogDebug("初始化结束日期选择器");
                    // 确保正确处理日期，不依赖时区
                    endDatePicker.SelectedDate = _viewModel?.EndDate?.DateTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化日期选择器时发生异常");
            }
        }

        /// <summary>
        /// 设置日期选择器绑定
        /// </summary>
        private void SetupDatePickerBindings()
        {
            try
            {
                // 开始日期选择器
                if (this.FindControl<DatePicker>("StartDatePicker") is DatePicker startDatePicker)
                {
                    _logger.LogDebug("绑定开始日期选择器");
                    startDatePicker.SelectedDate = _viewModel?.StartDate?.DateTime;
                    startDatePicker.SelectedDateChanged += (s, e) =>
                    {
                        if (e.NewDate.HasValue && _viewModel != null)
                        {
                            _logger.LogDebug("开始日期选择器值变更: {NewDate}", e.NewDate);
                            DateTime utcDate = new DateTime(e.NewDate.Value.Year, e.NewDate.Value.Month, e.NewDate.Value.Day, 0, 0, 0);
                            _viewModel.StartDate = new DateTimeOffset(utcDate, TimeSpan.Zero);
                        }
                        else if (_viewModel != null)
                        {
                            _logger.LogDebug("开始日期选择器值清空");
                            _viewModel.StartDate = null;
                        }
                    };
                }
                else
                {
                    _logger.LogWarning("未找到开始日期选择器");
                }

                // 结束日期选择器
                if (this.FindControl<DatePicker>("EndDatePicker") is DatePicker endDatePicker)
                {
                    _logger.LogDebug("绑定结束日期选择器");
                    endDatePicker.SelectedDate = _viewModel?.EndDate?.DateTime;
                    endDatePicker.SelectedDateChanged += (s, e) =>
                    {
                        if (e.NewDate.HasValue && _viewModel != null)
                        {
                            _logger.LogDebug("结束日期选择器值变更: {NewDate}", e.NewDate);
                            DateTime utcDate = new DateTime(e.NewDate.Value.Year, e.NewDate.Value.Month, e.NewDate.Value.Day, 0, 0, 0);
                            _viewModel.EndDate = new DateTimeOffset(utcDate, TimeSpan.Zero);
                        }
                        else if (_viewModel != null)
                        {
                            _logger.LogDebug("结束日期选择器值清空");
                            _viewModel.EndDate = null;
                        }
                    };
                }
                else
                {
                    _logger.LogWarning("未找到结束日期选择器");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "绑定日期选择器时发生异常");
            }
        }
        #endregion
    }
}
