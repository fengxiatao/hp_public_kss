using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FaceLocker.ViewModels;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Views;

public partial class AdminUserView : UserControl
{
    #region 私有字段
    private AdminUserViewModel? _viewModel;
    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化用户管理视图
    /// </summary>
    public AdminUserView()
    {
        InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
    }
    #endregion

    #region 初始化方法
    /// <summary>
    /// 初始化组件
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 数据上下文变化事件处理
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AdminUserViewModel viewModel)
        {
            _viewModel = viewModel;

            // 延迟设置绑定，确保在UI线程
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SetupBindings();
            });

            // 激活ViewModel
            _ = Task.Run(async () => await viewModel.ActivateAsync());
        }
    }
    #endregion

    #region 绑定设置
    /// <summary>
    /// 设置数据绑定
    /// </summary>
    private void SetupBindings()
    {
        if (_viewModel == null) return;

        // 设置分页控件
        if (this.FindControl<ComboBox>("PageSizeComboBox") is ComboBox pageSizeCombo)
        {
            pageSizeCombo.ItemsSource = _viewModel.PageSizes;
            pageSizeCombo.SelectedItem = _viewModel.PageSize;
            pageSizeCombo.SelectionChanged += (s, e) =>
            {
                if (pageSizeCombo.SelectedItem is int size)
                    _viewModel.PageSize = size;
            };
        }

        // 设置分页按钮
        if (this.FindControl<Button>("FirstPageButton") is Button firstPageButton)
        {
            firstPageButton.Command = _viewModel.FirstPageCommand;
        }

        if (this.FindControl<Button>("PreviousPageButton") is Button previousPageButton)
        {
            previousPageButton.Command = _viewModel.PreviousPageCommand;
        }

        if (this.FindControl<Button>("NextPageButton") is Button nextPageButton)
        {
            nextPageButton.Command = _viewModel.NextPageCommand;
        }

        if (this.FindControl<Button>("LastPageButton") is Button lastPageButton)
        {
            lastPageButton.Command = _viewModel.LastPageCommand;
        }

        // 设置搜索框
        if (this.FindControl<TextBox>("SearchTextBox") is TextBox searchTextBox)
        {
            searchTextBox.Text = _viewModel.SearchKeyword;
            searchTextBox.KeyUp += async (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    await _viewModel.SearchCommand.Execute();
                }
            };
        }

        // 设置搜索按钮
        if (this.FindControl<Button>("SearchButton") is Button searchButton)
        {
            searchButton.Command = _viewModel.SearchCommand;
        }

        // 设置重置按钮
        if (this.FindControl<Button>("ResetSearchButton") is Button resetSearchButton)
        {
            resetSearchButton.Command = _viewModel.ResetSearchCommand;
        }

        // 设置用户列表
        if (this.FindControl<ItemsControl>("UsersItemsControl") is ItemsControl usersControl)
            usersControl.ItemsSource = _viewModel.Users;

        // 监听属性变化 - 使用Dispatcher确保在UI线程
        _viewModel.PropertyChanged += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                HandlePropertyChanged(e.PropertyName);
            });
        };
    }

    /// <summary>
    /// 处理属性变化
    /// </summary>
    private void HandlePropertyChanged(string? propertyName)
    {
        if (_viewModel == null) return;

        switch (propertyName)
        {
            case nameof(AdminUserViewModel.TotalCount):
            case nameof(AdminUserViewModel.CurrentPage):
            case nameof(AdminUserViewModel.TotalPages):
                UpdatePaginationText();
                UpdatePaginationButtons();
                break;
            case nameof(AdminUserViewModel.Users):
                if (this.FindControl<ItemsControl>("UsersItemsControl") is ItemsControl uc)
                    uc.ItemsSource = _viewModel.Users;
                break;
            case nameof(AdminUserViewModel.PageSize):
                if (this.FindControl<ComboBox>("PageSizeComboBox") is ComboBox psCombo)
                    psCombo.SelectedItem = _viewModel.PageSize;
                break;
            case nameof(AdminUserViewModel.SearchKeyword):
                if (this.FindControl<TextBox>("SearchTextBox") is TextBox stb)
                    stb.Text = _viewModel.SearchKeyword;
                break;
        }
    }

    /// <summary>
    /// 更新分页文本显示
    /// </summary>
    private void UpdatePaginationText()
    {
        if (_viewModel == null) return;

        if (this.FindControl<TextBlock>("TotalCountText") is TextBlock totalCountText)
            totalCountText.Text = $"共 {_viewModel.TotalCount} 条记录";

        if (this.FindControl<TextBlock>("CurrentPageText") is TextBlock currentPageText)
            currentPageText.Text = $"第 {_viewModel.CurrentPage} 页";

        if (this.FindControl<TextBlock>("TotalPagesText") is TextBlock totalPagesText)
            totalPagesText.Text = $"共 {_viewModel.TotalPages} 页";
    }

    /// <summary>
    /// 更新分页按钮状态
    /// </summary>
    private void UpdatePaginationButtons()
    {
        if (_viewModel == null) return;

        if (this.FindControl<Button>("FirstPageButton") is Button fpButton)
            fpButton.IsEnabled = _viewModel.CanGoToFirstPage;

        if (this.FindControl<Button>("PreviousPageButton") is Button ppButton)
            ppButton.IsEnabled = _viewModel.CanGoToPreviousPage;

        if (this.FindControl<Button>("NextPageButton") is Button npButton)
            npButton.IsEnabled = _viewModel.CanGoToNextPage;

        if (this.FindControl<Button>("LastPageButton") is Button lpButton)
            lpButton.IsEnabled = _viewModel.CanGoToLastPage;
    }
    #endregion
}