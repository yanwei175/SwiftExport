using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>中性产品可编辑行，负责行状态、数值校验和允许手动覆盖的美元售价计算。</summary>
public sealed partial class ProductNeutralLineModel : BaseLineModel<ProductNeutralDto>
{
    /// <summary>获取或设置所属主型号标识。</summary>
    [ObservableProperty] public partial Guid ProductMasterId { get; set; }
    /// <summary>获取或设置中性产品编码。</summary>
    [ObservableProperty] public partial string ProductCode { get; set; } = string.Empty;
    /// <summary>获取或设置颜色标识。</summary>
    [ObservableProperty] public partial Guid ColorId { get; set; }
    /// <summary>获取或设置颜色显示名称。</summary>
    [ObservableProperty] public partial string ColorName { get; set; } = string.Empty;
    /// <summary>获取或设置默认美元销售价；允许手动覆盖，计算依赖再次变化时重新计算。</summary>
    [ObservableProperty] public partial decimal? DefaultSalesPriceUsd { get; set; }
    /// <summary>获取或设置默认汇率。</summary>
    [ObservableProperty] public partial decimal? DefaultExchangeRate { get; set; }
    /// <summary>获取或设置小数形式的利润率，例如 0.2 表示 20%。</summary>
    [ObservableProperty] public partial decimal? ProfitMargin { get; set; }
    /// <summary>获取或设置默认人民币采购价。</summary>
    [ObservableProperty] public partial decimal? DefaultPurchasePriceCny { get; set; }
    /// <summary>获取或设置图片路径。</summary>
    [ObservableProperty] public partial string ImagePath { get; set; } = string.Empty;
    /// <summary>获取或设置当前图片是否作为所属主型号的主图。</summary>
    [ObservableProperty] public partial bool IsMainImage { get; set; }

    partial void OnDefaultPurchasePriceCnyChanged(decimal? value) => RecalculateSalesPrice();

    partial void OnProfitMarginChanged(decimal? value) => RecalculateSalesPrice();

    partial void OnDefaultExchangeRateChanged(decimal? value) => RecalculateSalesPrice();

    /// <summary>计算依赖被编辑时按商业四舍五入更新售价；加载、回填与撤销不得覆盖已接纳的手动售价。</summary>
    private void RecalculateSalesPrice()
    {
        if (IsSuppressed
            || DefaultPurchasePriceCny is not decimal purchasePrice || purchasePrice < 0
            || ProfitMargin is not decimal margin || margin == 1m
            || DefaultExchangeRate is not decimal exchangeRate || exchangeRate <= 0)
            return;

        try
        {
            DefaultSalesPriceUsd = decimal.Round(
                purchasePrice / (1m - margin) / exchangeRate, 2, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            // 极端输入超出 decimal 范围时保留原售价，不能让编辑单元格抛异常。
        }
    }

    /// <summary>验证中性产品编码、颜色、价格和汇率。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(ProductCode)) return "产品编码不能为空。";
        if (ColorId == Guid.Empty) return "请选择产品颜色。";
        if (DefaultSalesPriceUsd < 0 || DefaultPurchasePriceCny < 0) return "价格不能小于 0。";
        if (DefaultExchangeRate.HasValue && DefaultExchangeRate.Value <= 0) return "汇率必须大于 0。";
        return null;
    }

    /// <summary>转换为中性产品写入数据。</summary>
    public ProductNeutralWriteDto ToWriteDto() => this.Adapt<ProductNeutralWriteDto>();

    /// <summary>从详情契约载入可编辑字段。</summary>
    protected override void LoadFromDto(ProductNeutralDto dto) => dto.Adapt(this);

    /// <summary>转换为详情契约，用于基线和回滚。</summary>
    public override ProductNeutralDto ToDto() => this.Adapt<ProductNeutralDto>();
}
