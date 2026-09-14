using CommunityToolkit.Mvvm.ComponentModel;
namespace SwiftExportClient.Features.Inventory.Outbounds.Models;
/// <summary>正库存选择快照；本单产品已被服务端排除，因此每个候选本单已选量为零。</summary>
public sealed partial class OutboundItemPickerRowModel : ObservableObject
{
    [ObservableProperty] public partial bool IsChecked { get; set; }
    public Guid Uid { get; init; }
    public Guid WarehouseSiteId { get; init; }
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string LinkSku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal OnHandQty { get; init; }
    /// <summary>已选择产品从候选排除，不把其他草稿算作本单选择。</summary>
    public decimal SelectedQuantity => 0;
    /// <summary>无预留机制，候选剩余量等于读取时余额。</summary>
    public decimal RemainingQuantity => OnHandQty;
}
