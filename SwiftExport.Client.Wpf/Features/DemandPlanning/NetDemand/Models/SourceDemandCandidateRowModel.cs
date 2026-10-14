using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>为成品需求候选增加仅属于选择对话框的勾选状态。</summary>
public sealed partial class SourceDemandCandidateRowModel : ObservableObject
{
    public required SourceDemandCandidateDto Demand { get; init; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
