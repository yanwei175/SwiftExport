using System.Windows.Controls;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Views;

/// <summary>
/// 净需求工作台视图，负责把来源 ListBox 的选择事件转交给异步 ViewModel 命令。
/// </summary>
public partial class NetDemandWorkspaceView : UserControl
{
    public NetDemandWorkspaceView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 选择来源单时异步加载该来源的全部匹配净需求；命令会处理未保存修改确认和选择恢复。
    /// </summary>
    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is NetDemandWorkspaceViewModel viewModel
            && sender is ListBox { SelectedItem: NetDemandSourceGroupModel group })
        {
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        }
    }
}
