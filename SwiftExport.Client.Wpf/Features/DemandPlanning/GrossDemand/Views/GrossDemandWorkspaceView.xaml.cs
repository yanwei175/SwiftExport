using System.Windows.Controls;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Views;

public partial class GrossDemandWorkspaceView : UserControl
{
    public GrossDemandWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is GrossDemandWorkspaceViewModel viewModel
            && sender is ListBox { SelectedItem: GrossDemandSourceGroupModel group })
        {
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        }
    }
}
