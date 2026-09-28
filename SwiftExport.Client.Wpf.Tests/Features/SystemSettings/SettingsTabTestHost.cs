using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.Workspace.ViewModels;
using SwiftExportClient.Features.SystemSettings.Workspace.Views;
using SwiftExportClient.Services.UI;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>仅测试使用的 Shell 替身：页签内容和公共 DialogHost 位于同一窗口。</summary>
internal sealed class SettingsTabTestHost : FluentWindow
{
    private bool _allowClose, _checkingClose;
    private readonly ICurrentUserService _currentUser;
    private readonly string? _userId;
    public SystemSettingsWorkspaceViewModel Model { get; }
    public SystemSettingsView View { get; }
    public TabControl Tabs { get; } = new();
    public string? CurrentPageName => View.CurrentPageName;
    /// <summary>创建带真实公共弹窗服务的测试页签宿主。</summary>
    public SettingsTabTestHost(Func<RbacManagementViewModel> rbac,
        Func<DictionaryManagerViewModel> dictionary, Func<UserRegistrationViewModel> registration,
        Func<ResetPasswordViewModel> reset, ICurrentUserService user, IContentDialogService dialogs,
        WindowMessageService messages)
    {
        _currentUser = user; _userId = user.CurrentUser?.UserId;
        Width = 1280; Height = 820; ShowInTaskbar = false;
        Model = new(rbac, dictionary, registration, reset, user, new WpfUiDialogService(dialogs));
        View = new() { DataContext = Model };
        var root = new Grid();
        Tabs.Items.Add(new TabItem { Header = "系统设置", Content = View });
        root.Children.Add(Tabs);
        var host = new ContentDialogHost(); root.Children.Add(host);
        Content = root;
        dialogs.SetDialogHost(host);
        Closing += OnClosing;
        Closed += (_, _) => { user.CurrentUserChanged -= OnUserChanged; Model.Dispose(); };
        user.CurrentUserChanged += OnUserChanged;
    }
    /// <summary>查询页签内容中的命名导航控件。</summary>
    public new object FindName(string name) => View.FindName(name);
    /// <summary>模拟 Shell 会话结束时移除页签。</summary>
    private void OnUserChanged(object? sender, EventArgs e)
    {
        if (_currentUser.CurrentUser?.UserId != _userId || !SystemSettingsWorkspaceViewModel.EntryPermissions.Any(_currentUser.HasPermission)) ForceClose();
    }
    /// <summary>模拟页签关闭按钮调用文档关闭门禁。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_checkingClose) return;
        _checkingClose = true;
        try
        {
            if (!await Model.CanCloseAsync(CancellationToken.None)) return;
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        finally { _checkingClose = false; }
    }
    /// <summary>测试结束或会话失效时强制释放模型及窗口。</summary>
    public void ForceClose() { _allowClose = true; Model.Dispose(); Close(); }
}
