using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Shell;

public sealed partial class ShellViewModel : ViewModelBaseEx
{
    private const string HomeRouteKey = "home";
    private static readonly TimeSpan TransientStatusDuration = TimeSpan.FromSeconds(5);
    private readonly INavigationService _navigationService;
    private readonly ISessionService _sessionService;
    private readonly IStatusMessageService _statusMessageService;
    private readonly IReadOnlyList<ShellNavigationItem> _allMenuItems;
    private bool _isInitialized;
    private CancellationTokenSource? _statusResetCancellation;

    public ShellViewModel(
        IOptions<ClientOptions> clientOptions,
        IOptions<ApiOptions> apiOptions,
        ICurrentUserService currentUserService,
        INavigationRouteRegistry routeRegistry,
        INavigationService navigationService,
        ISessionService sessionService,
        IStatusMessageService statusMessageService)
    {
        _navigationService = navigationService;
        _sessionService = sessionService;
        _statusMessageService = statusMessageService;
        _allMenuItems = routeRegistry.MenuItems
            .Select(item => new ShellNavigationItem(item))
            .ToArray();

        var client = clientOptions.Value;
        var api = apiOptions.Value;

        Title = client.ApplicationName;
        StatusMessage = $"v{client.Version} · {currentUserService.DisplayName} · {api.BaseUrl}";
        FilteredMenuItems = _allMenuItems;
        OpenTabs = navigationService.OpenTabs;

        navigationService.PropertyChanged += OnNavigationPropertyChanged;
        statusMessageService.MessageChanged += OnStatusMessageChanged;
    }

    public string StatusMessage { get; }

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
    private void OpenSettings()
    {
        _statusMessageService.Show("设置", "设置页面将在后续阶段接入。", UiMessageSeverity.Info);
    }

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
        var query = MenuSearchText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            FilteredMenuItems = _allMenuItems;
            return;
        }

        FilteredMenuItems = _allMenuItems
            .Select(item => FilterMenuItem(item, query))
            .Where(item => item is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();
    }

    partial void OnMenuSearchTextChanged(string value)
    {
        ApplyMenuFilter();
    }

    private static ShellNavigationItem? FilterMenuItem(ShellNavigationItem item, string query)
    {
        var selfMatches = item.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matchingChildren = item.Children
            .Select(child => FilterMenuItem(child, query))
            .Where(child => child is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();

        if (selfMatches)
        {
            return item.CloneForFilter(item.Children, forceExpanded: true);
        }

        return matchingChildren.Length == 0
            ? null
            : item.CloneForFilter(matchingChildren, forceExpanded: true);
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
}
