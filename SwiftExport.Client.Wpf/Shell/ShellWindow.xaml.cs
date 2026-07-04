using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

public partial class ShellWindow : FluentWindow
{
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly ShellViewModel _viewModel;

    public ShellWindow(
        ShellViewModel viewModel,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contentDialogService.SetDialogHost(RootContentDialogHost);
        _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
        await _viewModel.InitializeAsync();
    }

    private async void OnNavigationItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem { TargetPageTag: string routeKey })
        {
            return;
        }

        await _viewModel.OpenRouteCommand.ExecuteAsync(routeKey);
    }
}
