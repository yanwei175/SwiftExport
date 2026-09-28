using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;
using SwiftExportClient.Features.SystemSettings.Services;
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
    /// <summary>与窗口一同释放的设置页签模型。</summary>
    public SettingsPageViewModel Model { get; }
    /// <summary>真实设置导航视图。</summary>
    public SystemSettingsView View { get; }
    /// <summary>模拟 Shell 的单页签容器。</summary>
    public TabControl Tabs { get; } = new();
    /// <summary>当前内部页面名称。</summary>
    public string? CurrentPageName => View.CurrentPageName;

    /// <summary>创建公共 DialogHost；四个工厂仅在访问对应内部页面时调用。</summary>
    public SettingsTabTestHost(Func<UsersQueryViewModel> users,
        Func<DictionaryManagerViewModel> dictionary, Func<RolesQueryViewModel> roles,
        Func<PermissionsQueryViewModel> permissions, ICurrentUserService user, IContentDialogService dialogs,
        WindowMessageService messages, ISettingsDialogService? settingsDialogs = null)
    {
        _currentUser = user; _userId = user.CurrentUser?.UserId;
        Width = 1280; Height = 820; ShowInTaskbar = false;
        Model = new(users, roles, permissions, dictionary, user, settingsDialogs ?? new SettingsDialogStub());
        Loaded += async (_, _) =>
        {
            var page = Enum.GetValues<SettingsPage>().FirstOrDefault(p => p != SettingsPage.Personal && Model.CanRead(p));
            if (Model.CanRead(page)) await Model.NavigateAsync(page);
        };
        View = new() { DataContext = Model };
        var root = new Grid();
        Tabs.Items.Add(new TabItem { Header = "设置", Content = View });
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

    private void OnUserChanged(object? sender, EventArgs e)
    {
        if (_currentUser.CurrentUser?.UserId != _userId || !_currentUser.IsAuthenticated) ForceClose();
    }

    /// <summary>模拟 Shell 页签关闭时的业务门禁。</summary>
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

    /// <summary>注销或测试结束时强制释放页签及窗口。</summary>
    public void ForceClose() { _allowClose = true; Model.Dispose(); Close(); }
}
