using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;

namespace SwiftExportClient.Features.Products.Components.Models;

/// <summary>中性产品查询结果的只读候选外观；仅供物料独立选择身份，不推导主型号或颜色。</summary>
public sealed class ProductNeutralLookupItemModel(ProductNeutralManagerListItemDto source)
{
    /// <summary>完整候选查询快照，不维护另一份编辑状态。</summary>
    public ProductNeutralManagerListItemDto Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    /// <summary>中性产品稳定身份。</summary>
    public Guid Uid => Source.Uid;
    /// <summary>中性产品编码，作为选择器显示与本地匹配文本。</summary>
    public string? ProductCode => Source.Data?.ProductCode;
    /// <summary>候选颜色仅供查看，不回写物料颜色。</summary>
    public string? ColorName => Source.ColorName;
}
