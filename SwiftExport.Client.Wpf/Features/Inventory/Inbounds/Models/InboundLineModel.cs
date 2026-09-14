using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>入库行编辑模型，复用基类的勾选、版本与脏状态。</summary>
public sealed partial class InboundLineModel : BaseLineModel<InboundLineDetailDto>, IDataErrorInfo
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
    public partial decimal? Quantity { get; set; }

    /// <summary>数量字段的非阻断错误，由 Grid 聚合呈现；不介入原生编辑提交。</summary>
    public string this[string columnName] => columnName == nameof(Quantity) ? ValidateQuantity() ?? string.Empty : string.Empty;

    /// <summary>数量错误已归属具体列，不重复生成整行错误。</summary>
    public string Error => string.Empty;

    /// <summary>编辑期允许空值，保存期与软提示共用同一数量规则。</summary>
    private string? ValidateQuantity()
    {
        if (Quantity is not { } quantity) return "请输入本次入库数量。";
        return quantity <= 0 || quantity > 99999999999999.9999m || decimal.Round(quantity, 4) != quantity
            ? "本次入库数量须为正数、最多四位小数且符合 decimal(18,4)。" : null;
    }

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
        // 待入库是查询快照，不作为保存容量门禁；实际累计容量由过账最终验证。
        if (ValidateQuantity() is { } quantityError) return quantityError;
        if (string.IsNullOrWhiteSpace(UnitSnapshot)) return "来源计量单位不能为空。";
        return null;
    }

    /// <inheritdoc/>
    protected override void LoadFromDto(InboundLineDetailDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override InboundLineDetailDto ToDto() => this.Adapt<InboundLineDetailDto>();
}
