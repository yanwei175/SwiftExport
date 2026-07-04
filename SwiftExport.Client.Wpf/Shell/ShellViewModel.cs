using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private bool _isInitialized;

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
        var client = clientOptions.Value;
        var api = apiOptions.Value;

        Title = client.ApplicationName;
        StatusMessage = $"v{client.Version} · {currentUserService.DisplayName} · {api.BaseUrl}";
        MenuItems = routeRegistry.MenuItems;
        OpenTabs = navigationService.OpenTabs;
        OpenRouteCommand = new AsyncRelayCommand<string?>(OpenRouteAsync);
        CloseTabCommand = new AsyncRelayCommand<NavigationTabItem?>(CloseTabAsync);
        LogoutCommand = new RelayCommand(Logout);

        navigationService.PropertyChanged += OnNavigationPropertyChanged;
        statusMessageService.MessageChanged += OnStatusMessageChanged;
    }

    public string StatusMessage { get; }

    public IReadOnlyList<NavigationMenuItem> MenuItems { get; }

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

    private bool _isInfoBarOpen;

    public bool IsInfoBarOpen
    {
        get => _isInfoBarOpen;
        private set => SetProperty(ref _isInfoBarOpen, value);
    }

    private string? _infoBarTitle;

    public string? InfoBarTitle
    {
        get => _infoBarTitle;
        private set => SetProperty(ref _infoBarTitle, value);
    }

    private string? _infoBarMessage;

    public string? InfoBarMessage
    {
        get => _infoBarMessage;
        private set => SetProperty(ref _infoBarMessage, value);
    }

    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;

    public InfoBarSeverity InfoBarSeverity
    {
        get => _infoBarSeverity;
        private set => SetProperty(ref _infoBarSeverity, value);
    }

    public IAsyncRelayCommand<string?> OpenRouteCommand { get; }

    public IAsyncRelayCommand<NavigationTabItem?> CloseTabCommand { get; }

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
        return string.IsNullOrWhiteSpace(routeKey)
            ? Task.CompletedTask
            : _navigationService.OpenAsync(routeKey);
    }

    private Task CloseTabAsync(NavigationTabItem? tab)
    {
        return _navigationService.CloseAsync(tab);
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
        OnPropertyChanged(nameof(CurrentPageStatus));
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
