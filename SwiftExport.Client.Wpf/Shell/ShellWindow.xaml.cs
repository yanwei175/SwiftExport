using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

public partial class ShellWindow : FluentWindow
{
    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
