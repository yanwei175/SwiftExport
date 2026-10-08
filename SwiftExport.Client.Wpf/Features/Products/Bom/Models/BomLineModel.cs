using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Bom.Models;

/// <summary>当前 BOM 可编辑组成行，复用基类脏状态和并发元数据。</summary>
public sealed partial class BomLineModel : BaseLineModel<BomDto>
{
    /// <summary>保存结果回填使用的客户端行键，不进入数据库。</summary>
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");
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

    /// <summary>检查必选子件与正整数比例；共享图、重复和循环由集合及后端继续校验。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(BomNo)) return "BOM 号不能为空。";
        if (SubSkuId == Guid.Empty || string.IsNullOrWhiteSpace(SubSku)) return "必须选择子件 SKU。";
        if (Numerator <= 0 || Denominator <= 0) return "用量分子和分母必须大于 0。";
        return null;
    }

    /// <summary>通过统一 Mapster 配置接纳服务端组成。</summary>
    protected override void LoadFromDto(BomDto dto)
    {
        dto.Adapt(this);
    }

    /// <summary>输出组成保存契约，页面状态和客户端行键不进入 DTO。</summary>
    public override BomDto ToDto() => this.Adapt<BomDto>();
}
