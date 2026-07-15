using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

public sealed class ShellViewModel : ViewModelBaseEx
{
    private const string HomeRouteKey = "home";
    private readonly INavigationService _navigationService;
    private readonly ISessionService _sessionService;
    private readonly IStatusMessageService _statusMessageService;
    private readonly IReadOnlyList<ShellNavigationItem> _allMenuItems;
    private bool _isInitialized;
    private bool _isNavigationPaneExpanded = true;
    private string _menuSearchText = string.Empty;
    private IReadOnlyList<ShellNavigationItem> _filteredMenuItems = [];
    private bool _isInfoBarOpen;
    private string? _infoBarTitle;
    private string? _infoBarMessage;
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;

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
        OpenRouteCommand = new AsyncRelayCommand<string?>(OpenRouteAsync);
        CloseTabCommand = new AsyncRelayCommand<NavigationTabItem?>(CloseTabAsync);
        ToggleNavigationPaneCommand = new RelayCommand(ToggleNavigationPane);
        ToggleNavigationGroupCommand = new RelayCommand<ShellNavigationItem?>(ToggleNavigationGroup);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LogoutCommand = new RelayCommand(Logout);

        navigationService.PropertyChanged += OnNavigationPropertyChanged;
        statusMessageService.MessageChanged += OnStatusMessageChanged;
    }

    public string StatusMessage { get; }

    public IReadOnlyList<ShellNavigationItem> FilteredMenuItems
    {
        get => _filteredMenuItems;
        private set => SetProperty(ref _filteredMenuItems, value);
    }

    public string MenuSearchText
    {
        get => _menuSearchText;
        set
        {
            if (SetProperty(ref _menuSearchText, value))
            {
                ApplyMenuFilter();
            }
        }
    }

    public bool IsNavigationPaneExpanded
    {
        get => _isNavigationPaneExpanded;
        private set
        {
            if (SetProperty(ref _isNavigationPaneExpanded, value))
            {
                OnPropertyChanged(nameof(IsNavigationPaneCollapsed));
                OnPropertyChanged(nameof(NavigationPaneWidth));
            }
        }
    }

    public bool IsNavigationPaneCollapsed => !IsNavigationPaneExpanded;

    public GridLength NavigationPaneWidth => IsNavigationPaneExpanded
        ? new GridLength(220)
        : new GridLength(56);

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

    public bool IsInfoBarOpen
    {
        get => _isInfoBarOpen;
        private set => SetProperty(ref _isInfoBarOpen, value);
    }

    public string? InfoBarTitle
    {
        get => _infoBarTitle;
        private set => SetProperty(ref _infoBarTitle, value);
    }

    public string? InfoBarMessage
    {
        get => _infoBarMessage;
        private set => SetProperty(ref _infoBarMessage, value);
    }

    public InfoBarSeverity InfoBarSeverity
    {
        get => _infoBarSeverity;
        private set => SetProperty(ref _infoBarSeverity, value);
    }

    public IAsyncRelayCommand<string?> OpenRouteCommand { get; }

    public IAsyncRelayCommand<NavigationTabItem?> CloseTabCommand { get; }

    public IRelayCommand ToggleNavigationPaneCommand { get; }

    public IRelayCommand<ShellNavigationItem?> ToggleNavigationGroupCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand LogoutCommand { get; }

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

    private Task OpenRouteAsync(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return Task.CompletedTask;
        }

        return _navigationService.OpenAsync(routeKey);
    }

    private Task CloseTabAsync(NavigationTabItem? tab)
    {
        return _navigationService.CloseAsync(tab);
    }

    private void ToggleNavigationPane()
    {
        IsNavigationPaneExpanded = !IsNavigationPaneExpanded;
    }

    private void ToggleNavigationGroup(ShellNavigationItem? item)
    {
        if (item is not null)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    private void OpenSettings()
    {
        _statusMessageService.Show("设置", "设置页面将在后续阶段接入。", UiMessageSeverity.Info);
    }

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
        InfoBarTitle = e.Title;
        InfoBarMessage = e.Message;
        InfoBarSeverity = ToInfoBarSeverity(e.Severity);
        IsInfoBarOpen = e.IsOpen;
    }

    private static InfoBarSeverity ToInfoBarSeverity(UiMessageSeverity severity)
    {
        return severity switch
        {
            UiMessageSeverity.Success => InfoBarSeverity.Success,
            UiMessageSeverity.Warning => InfoBarSeverity.Warning,
            UiMessageSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }
}
