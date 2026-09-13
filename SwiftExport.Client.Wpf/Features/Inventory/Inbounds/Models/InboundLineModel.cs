using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>入库行编辑模型，复用基类的勾选、版本与脏状态。</summary>
public sealed partial class InboundLineModel : BaseLineModel<InboundLineDetailDto>
{
    [ObservableProperty]
    public partial Guid InboundDocumentId { get; set; }

    [ObservableProperty]
    public partial int LineNumber { get; set; }

    [ObservableProperty]
    public partial Guid ProductId { get; set; }

    [ObservableProperty]
    public partial string SkuSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DescriptionSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UnitSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal Quantity { get; set; }

    [ObservableProperty]
    public partial Guid SourceLineId { get; set; }
    /// <summary>来源查询版本快照，选择时不执行冲突判断。</summary>
    [ObservableProperty]
    public partial int SourceVersion { get; set; }

    [ObservableProperty]
    public partial Guid? OriginalLineId { get; set; }

    [ObservableProperty]
    public partial string SourceNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BoNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LinkSku { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImagePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal OriginalQty { get; set; }

    [ObservableProperty]
    public partial decimal RemainingReceivableQty { get; set; }

    /// <inheritdoc/>
    public override string? ValidateRow()
    {
        if (SourceLineId == Guid.Empty || ProductId == Guid.Empty) return "请选择有效的入库来源。";
        if (Quantity <= 0 || Quantity > RemainingReceivableQty) return "本次入库数量必须大于零且不超过待入库数量。";
        if (string.IsNullOrWhiteSpace(UnitSnapshot)) return "来源计量单位不能为空。";
        return null;
    }

    /// <inheritdoc/>
    protected override void LoadFromDto(InboundLineDetailDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override InboundLineDetailDto ToDto() => this.Adapt<InboundLineDetailDto>();
}
