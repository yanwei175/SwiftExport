using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>待发货来源候选快照；仅勾选和本次数量属于界面输入，不保存库存事实。</summary>
public sealed partial class PackingListSourcePickerRowModel : ObservableObject
{
    /// <summary>来源需求唯一标识。</summary>
    public Guid GrossDemandId { get; init; }
    /// <summary>候选来源并发版本。</summary>
    public int DemandVersion { get; init; }
    /// <summary>上游事实版本，未知为空。</summary>
    public int? SourceVersion { get; init; }
    /// <summary>已证明的最终客户。</summary>
    public Guid CustomerId { get; init; }
    /// <summary>标准产品标识；非标来源为空。</summary>
    public Guid? ProductId { get; init; }
    /// <summary>是否为不产生库存事实的非标来源。</summary>
    public bool IsNonStandard { get; init; }
    /// <summary>来源类型。</summary>
    public string? SourceType { get; init; }
    /// <summary>来源单号快照。</summary>
    public string? SourceNo { get; init; }
    /// <summary>来源行号快照。</summary>
    public int? SourceLineNumber { get; init; }
    /// <summary>来源要求数量。</summary>
    public decimal DemandQty { get; init; }
    /// <summary>正式交付净额；不可用时为空，不伪装零。</summary>
    public decimal? DeliveredQty { get; init; }
    /// <summary>剩余交付量；不可用时为空。</summary>
    public decimal? RemainingQty { get; init; }
    /// <summary>交付投影不可用原因。</summary>
    public string? DeliveryUnavailableReason { get; init; }
    /// <summary>产品编码快照。</summary>
    public string? SkuSnapshot { get; init; }
    /// <summary>关联型号快照。</summary>
    public string? LinkSkuSnapshot { get; init; }
    /// <summary>客户型号快照。</summary>
    public string? CustomerSkuSnapshot { get; init; }
    /// <summary>英文描述快照。</summary>
    public string? DescriptionEnSnapshot { get; init; }
    /// <summary>英文颜色名称快照。</summary>
    public string? ColorNameEnSnapshot { get; init; }
    /// <summary>库存及交付数量的计量单位快照。</summary>
    public string? UnitSnapshot { get; init; }
    /// <summary>每包数量；未知为空，已知时须大于零。</summary>
    public decimal? PcsPerPackage { get; init; }
    /// <summary>每包净重。未知为空。</summary>
    public decimal? NwPerPackage { get; init; }
    /// <summary>每包毛重。未知为空。</summary>
    public decimal? GwPerPackage { get; init; }
    /// <summary>每包体积。未知为空。</summary>
    public decimal? CbmPerPackage { get; init; }
    /// <summary>客户采购编号文本快照，不作为关系主键。</summary>
    public string? CustomerPoNo { get; init; }
    /// <summary>查询展示图片，不持久化。</summary>
    public string? ImagePath { get; init; }
    /// <summary>是否选择此来源；不表示占用交付额度。</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>当前草稿同来源有效行数量，不包含其他草稿。</summary>
    public decimal DraftQty { get; internal set; }
    /// <summary>只读本次可选量，保留后端原始 RemainingQty 不变。</summary>
    public decimal? AvailableQty => RemainingQty.HasValue ? Math.Max(0, RemainingQty.Value - DraftQty) : null;

    /// <summary>验证本次选择数量；未知交付额度必须先由后端补齐。</summary>
    public string? ValidateSelection()
    {
        if (GrossDemandId == Guid.Empty || CustomerId == Guid.Empty
            || (!IsNonStandard && (!ProductId.HasValue || ProductId == Guid.Empty))
            || (IsNonStandard && ProductId.HasValue))
            return "来源、客户或标准/非标产品身份不一致。";
        if (!RemainingQty.HasValue || !string.IsNullOrWhiteSpace(DeliveryUnavailableReason))
            return "待发货数量尚未确认，请刷新来源。";
        if (!AvailableQty.HasValue || AvailableQty.Value <= 0) return "当前草稿已用尽此来源待交付量。";
        return null;
    }
}
