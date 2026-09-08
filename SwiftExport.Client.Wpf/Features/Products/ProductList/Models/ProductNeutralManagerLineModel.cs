using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>中性产品管理页可编辑行，负责关系显示、脏状态、数值校验和建议售价计算。</summary>
public sealed partial class ProductNeutralManagerLineModel : BaseLineModel<ProductNeutralManagerListItemDto>
{
    /// <summary>获取客户端稳定行键，用于批量保存结果原位回填。</summary>
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    /// <summary>获取或设置所属主型号标识。</summary>
    [ObservableProperty] public partial Guid ProductMasterId { get; set; }
    /// <summary>获取或设置所属主型号编码。</summary>
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    /// <summary>获取或设置主型号中文描述。</summary>
    [ObservableProperty] public partial string ProductMasterDescriptionZh { get; set; } = string.Empty;
    /// <summary>获取或设置所属主型号分类标识。</summary>
    [ObservableProperty] public partial Guid CategoryId { get; set; }
    /// <summary>获取或设置所属主型号分类名称。</summary>
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    /// <summary>获取或设置中性产品编码。</summary>
    private string _productCode = string.Empty;
    public string ProductCode
    {
        get => _productCode;
        set => SetProperty(ref _productCode, value?.Trim().ToUpperInvariant() ?? string.Empty);
    }
    /// <summary>获取或设置颜色标识。</summary>
    [ObservableProperty] public partial Guid ColorId { get; set; }
    /// <summary>获取或设置颜色显示名称。</summary>
    [ObservableProperty] public partial string ColorName { get; set; } = string.Empty;
    /// <summary>获取或设置默认美元销售价；允许用户手工覆盖。</summary>
    [ObservableProperty] public partial decimal? DefaultSalesPriceUsd { get; set; }
    /// <summary>获取或设置默认汇率。</summary>
    [ObservableProperty] public partial decimal? DefaultExchangeRate { get; set; }
    /// <summary>获取或设置利润率，小数 0.2 表示 20%。</summary>
    [ObservableProperty] public partial decimal? ProfitMargin { get; set; }
    /// <summary>获取或设置默认人民币采购价。</summary>
    [ObservableProperty] public partial decimal? DefaultPurchasePriceCny { get; set; }
    /// <summary>获取或设置图片路径。</summary>
    [ObservableProperty] public partial string ImagePath { get; set; } = string.Empty;
    partial void OnDefaultPurchasePriceCnyChanged(decimal? value) => RecalculateSalesPrice();
    partial void OnProfitMarginChanged(decimal? value) => RecalculateSalesPrice();
    partial void OnDefaultExchangeRateChanged(decimal? value) => RecalculateSalesPrice();

    /// <summary>依赖价格变化时更新建议售价；无完整合法输入或溢出时保留用户现值。</summary>
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
            // 极端输入超出 decimal 范围时不破坏当前单元格编辑事务。
        }
    }

    /// <summary>保存前验证父主型号、编码、颜色及可空数值边界。</summary>
    public override string? ValidateRow()
    {
        if (ProductMasterId == Guid.Empty) return "请选择所属主型号。";
        if (string.IsNullOrWhiteSpace(ProductCode)) return "中性产品编码不能为空。";
        if (ColorId == Guid.Empty) return "请选择产品颜色。";
        if (DefaultSalesPriceUsd < 0 || DefaultPurchasePriceCny < 0) return "价格不能小于 0。";
        if (DefaultExchangeRate.HasValue && DefaultExchangeRate.Value <= 0) return "汇率必须大于 0。";
        return null;
    }

    /// <summary>转换为 Manager 专用写入数据。</summary>
    public ProductNeutralManagerWriteDto ToWriteDto() => this.Adapt<ProductNeutralManagerWriteDto>();

    /// <summary>从分页投影载入可编辑字段及关系显示字段。</summary>
    protected override void LoadFromDto(ProductNeutralManagerListItemDto dto) => dto.Adapt(this);

    /// <summary>转换为列表投影，用于基线、撤销和本地状态接纳。</summary>
    public override ProductNeutralManagerListItemDto ToDto() => this.Adapt<ProductNeutralManagerListItemDto>();
}
