using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Updates;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace SwiftExportClient.Shell;

/// <summary>协调 Shell 页签、按权限过滤的导航菜单及固定状态消息。</summary>
public sealed partial class ShellViewModel : ViewModelBaseEx, IDisposable
{
    private const string HomeRouteKey = "home";
    private const string SettingsRouteKey = "system-settings";
    private static readonly TimeSpan TransientStatusDuration = TimeSpan.FromSeconds(5);
    private readonly INavigationService _navigationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISessionService _sessionService;
    private readonly IStatusMessageService _statusMessageService;
    private readonly IReadOnlyList<ShellNavigationItem> _allMenuItems;
    private readonly ShellNavigationMenuFilter _menuFilter;
    private bool _isInitialized;
    private bool _isDisposed;
    private CancellationTokenSource? _statusResetCancellation;

    /// <summary>组合导航、会话、状态消息及应用级更新状态。</summary>
    public ShellViewModel(
        IOptions<ClientOptions> clientOptions,
        IOptions<ApiOptions> apiOptions,
        ICurrentUserService currentUserService,
        INavigationRouteRegistry routeRegistry,
        INavigationAccessService navigationAccessService,
        INavigationService navigationService,
        ISessionService sessionService,
        IStatusMessageService statusMessageService,
        ClientUpdateViewModel updates)
    {
        _navigationService = navigationService;
        _currentUserService = currentUserService;
        _sessionService = sessionService;
        _statusMessageService = statusMessageService;
        _menuFilter = new ShellNavigationMenuFilter(navigationAccessService);
        _allMenuItems = routeRegistry.MenuItems
            .Select(item => new ShellNavigationItem(item))
            .ToArray();

        var client = clientOptions.Value;
        var api = apiOptions.Value;

        Title = client.ApplicationName;
        Updates = updates;
        ClientVersion = updates.CurrentVersion;
        ApiBaseUrl = api.BaseUrl;
        FilteredMenuItems = _menuFilter.Filter(_allMenuItems, MenuSearchText);
        OpenTabs = navigationService.OpenTabs;

        navigationService.PropertyChanged += OnNavigationPropertyChanged;
        currentUserService.CurrentUserChanged += OnCurrentUserChanged;
        statusMessageService.MessageChanged += OnStatusMessageChanged;
    }

    private string ClientVersion { get; }

    /// <summary>应用级更新提示与进度，切换登录不会清空已下载包。</summary>
    public ClientUpdateViewModel Updates { get; }

    private string ApiBaseUrl { get; }

    public string StatusMessage => $"v{ClientVersion} · {_currentUserService.DisplayName} · {ApiBaseUrl}";

    [ObservableProperty]
    public partial IReadOnlyList<ShellNavigationItem> FilteredMenuItems { get; private set; } = [];

    [ObservableProperty]
    public partial string MenuSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNavigationPaneExpanded { get; set; } = true;

    public ObservableCollection<NavigationTabItem> OpenTabs { get; }

    public NavigationTabItem? SelectedTab
    {
        get => _navigationService.SelectedTab;
        set
        {
            if (!ReferenceEquals(_navigationService.SelectedTab, value))
            {
                _navigationService.SelectedTab = value;
                RaiseSelectedTabStateChanged();
            }
        }
    }

    public string CurrentPageStatus => SelectedTab is null
        ? "未打开页面"
        : $"当前页面：{SelectedTab.Title}";

    public string? SelectedRouteKey => SelectedTab?.RouteKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
    public partial string? StatusTitle { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
    public partial string? StatusDetail { get; private set; }

    [ObservableProperty]
    public partial UiMessageSeverity StatusSeverity { get; private set; } = UiMessageSeverity.Info;

    [ObservableProperty]
    public partial bool IsStatusMessageOpen { get; private set; }

    public string StatusDisplayText => string.IsNullOrWhiteSpace(StatusTitle)
        ? StatusDetail ?? string.Empty
        : string.IsNullOrWhiteSpace(StatusDetail)
            ? StatusTitle
            : $"{StatusTitle}：{StatusDetail}";

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _navigationService.Reset();
        await _navigationService.OpenAsync(HomeRouteKey);
        RaiseSelectedTabStateChanged();
    }

    [RelayCommand]
    private Task OpenRouteAsync(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return Task.CompletedTask;
        }

        return _navigationService.OpenAsync(routeKey);
    }

    [RelayCommand]
    private Task CloseTabAsync(NavigationTabItem? tab)
    {
        return _navigationService.CloseAsync(tab);
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => _navigationService.OpenAsync(SettingsRouteKey);

    [RelayCommand]
    private void DismissStatusMessage()
    {
        _statusMessageService.Clear();
    }

    [RelayCommand]
    private void Logout()
    {
        _sessionService.EndSession(SessionEndReason.Logout);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationService.SelectedTab))
        {
            RaiseSelectedTabStateChanged();
        }
    }

    private void RaiseSelectedTabStateChanged()
    {
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(SelectedRouteKey));
        OnPropertyChanged(nameof(CurrentPageStatus));
    }

    private void ApplyMenuFilter()
    {
        FilteredMenuItems = _menuFilter.Filter(_allMenuItems, MenuSearchText);
    }

    partial void OnMenuSearchTextChanged(string value)
    {
        ApplyMenuFilter();
    }

    private void OnStatusMessageChanged(object? sender, StatusMessageChangedEventArgs e)
    {
        CancelStatusReset();

        StatusTitle = e.Title;
        StatusDetail = e.Message;
        StatusSeverity = e.Severity;
        IsStatusMessageOpen = e.IsOpen;

        if (e.IsOpen && e.Severity is UiMessageSeverity.Info or UiMessageSeverity.Success)
        {
            _statusResetCancellation = new CancellationTokenSource();
            _ = ClearTransientStatusAsync(e.Title, e.Message, _statusResetCancellation.Token);
        }
    }

    private async Task ClearTransientStatusAsync(
        string? expectedTitle,
        string? expectedMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TransientStatusDuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (IsStatusMessageOpen
            && string.Equals(StatusTitle, expectedTitle, StringComparison.Ordinal)
            && string.Equals(StatusDetail, expectedMessage, StringComparison.Ordinal))
        {
            _statusMessageService.Clear();
        }
    }

    private void CancelStatusReset()
    {
        var cancellation = _statusResetCancellation;
        _statusResetCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    /// <summary>身份快照变化时刷新用户显示和菜单；页签清理由 NavigationService 统一完成。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            OnPropertyChanged(nameof(StatusMessage));
            ApplyMenuFilter();
            RaiseSelectedTabStateChanged();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Refresh);
            return;
        }

        Refresh();
    }

    /// <summary>取消计时任务并解除 Shell 生命周期内建立的事件订阅。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelStatusReset();
        _navigationService.PropertyChanged -= OnNavigationPropertyChanged;
        _currentUserService.CurrentUserChanged -= OnCurrentUserChanged;
        _statusMessageService.MessageChanged -= OnStatusMessageChanged;
    }
}
