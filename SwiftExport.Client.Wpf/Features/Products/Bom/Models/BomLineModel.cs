using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Bom.Models;

public sealed partial class BomLineModel : BaseLineModel<BomDto>
{
    [ObservableProperty]
    public partial string BomNo { get; set; } = string.Empty;
    [ObservableProperty]
    public partial Guid SubSkuId { get; set; }
    [ObservableProperty]
    public partial string SubSku { get; set; } = string.Empty;
    [ObservableProperty]
    public partial int Numerator { get; set; } = 1;
    [ObservableProperty]
    public partial int Denominator { get; set; } = 1;
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Finish { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Features { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string MainImage { get; set; } = string.Empty;

    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(BomNo)) return "BOM 号不能为空。";
        if (SubSkuId == Guid.Empty || string.IsNullOrWhiteSpace(SubSku)) return "必须选择子件 SKU。";
        if (Numerator <= 0 || Denominator <= 0) return "用量分子和分母必须大于 0。";
        return null;
    }

    protected override void LoadFromDto(BomDto dto)
    {
        dto.Adapt(this);
    }

    public override BomDto ToDto() => this.Adapt<BomDto>();
}
