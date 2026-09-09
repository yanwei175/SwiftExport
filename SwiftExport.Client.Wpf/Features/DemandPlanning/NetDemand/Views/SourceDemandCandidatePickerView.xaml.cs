using System.Windows.Controls;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Views;

/// <summary>承载来源需求候选选择器的视图。</summary>
public partial class SourceDemandCandidatePickerView : UserControl
{
    public SourceDemandCandidatePickerView() => InitializeComponent();

    /// <summary>把列表选择事件转交生成命令，使属性 setter 不直接发起 API 查询。</summary>
    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SourceDemandCandidatePickerViewModel viewModel
            && sender is ListBox { SelectedItem: SourceDemandCandidateGroupModel group })
        {
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        }
    }
}
