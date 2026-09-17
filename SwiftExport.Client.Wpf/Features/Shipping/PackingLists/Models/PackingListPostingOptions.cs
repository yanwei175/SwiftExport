using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>过账弹窗的临时输入，不修改箱单 InvoiceDate 或持久化状态。</summary>
public sealed partial class PackingListPostingOptions : ObservableObject
{
    [ObservableProperty] public partial DateTime? BusinessDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string? Reason { get; set; }
    /// <summary>反向时要求填写原因。</summary>
    public bool IsReverse { get; init; }
}
