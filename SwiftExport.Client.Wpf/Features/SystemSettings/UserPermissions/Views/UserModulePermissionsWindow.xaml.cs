using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.UserPermissions.ViewModels;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.UserPermissions.Views;

/// <summary>承载模块选择，并使用窗口本地 ContentDialogHost 完成未保存确认。</summary>
public partial class UserModulePermissionsWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly UserModulePermissionsViewModel _viewModel;
    private readonly IContentDialogService _contentDialogService;
    private bool _allowClose;
    private bool _checkingClose;

    /// <summary>创建模块权限模态窗口，并绑定窗口本地确认对话框宿主。</summary>
    public UserModulePermissionsWindow(
        UserModulePermissionsViewModel viewModel,
        IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _contentDialogService = contentDialogService;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        UserBox.SelectionChanged += OnUserSelectionChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contentDialogService.SetDialogHost(LocalDialogHost);
        await _viewModel.InitializeAsync();
        UserBox.Focus();
    }

    private async void OnUserSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserBox.SelectedItem is UserSummaryDto user
            && _viewModel.SelectUserCommand.CanExecute(user))
        {
            await _viewModel.SelectUserCommand.ExecuteAsync(user);
        }

        // 用户取消放弃变更或加载期间误切换时，把控件选择恢复到 ViewModel 的已确认用户。
        if (!ReferenceEquals(UserBox.SelectedItem, _viewModel.SelectedUser))
        {
            UserBox.SelectedItem = _viewModel.SelectedUser;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_checkingClose || _viewModel.IsWriting)
        {
            return;
        }

        _checkingClose = true;
        try
        {
            if (await _viewModel.CanCloseAsync(CancellationToken.None))
            {
                _allowClose = true;
                // CanCloseAsync 可能同步完成；必须等当前 Closing 回调返回后再发起第二次关闭。
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
        finally
        {
            _checkingClose = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        UserBox.SelectionChanged -= OnUserSelectionChanged;
    }

    /// <inheritdoc />
    public void ForceClose()
    {
        _allowClose = true;
        _viewModel.Dispose();
        Close();
    }
}
