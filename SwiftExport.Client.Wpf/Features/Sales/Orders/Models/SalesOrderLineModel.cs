using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.Sales.Orders.Models;

public sealed partial class SalesOrderLineModel : BaseLineModel<SalesOrderDocumentLineDto>, IDataErrorInfo
{
    [ObservableProperty] public partial int LineNumber { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTypeText))]
    public partial bool IsNonStandard { get; set; }
    [ObservableProperty] public partial Guid? ProductId { get; set; }
    [ObservableProperty] public partial Guid? CustomerProductId { get; set; }
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string ColorNameEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesEn { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CnyAmount))]
    [NotifyPropertyChangedFor(nameof(UsdAmount))]
    public partial decimal Qty { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CnyAmount))]
    public partial decimal? CnyPrice { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsdAmount))]
    public partial decimal? UsdPrice { get; set; }
    [ObservableProperty] public partial decimal? PcsOfCarton { get; set; }
    [ObservableProperty] public partial decimal? NwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? GwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? CbmOfCarton { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal ShippedQty { get; set; }
    [ObservableProperty] public partial decimal UnshippedQty { get; set; }

    public string LineTypeText => IsNonStandard ? "非标" : "标准";
    public decimal? CnyAmount => CnyPrice * Qty;
    public decimal? UsdAmount => UsdPrice * Qty;
    public string Error => ValidateRow() ?? string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(Qty) when Qty <= 0 => "数量必须大于零。",
        nameof(DescriptionEn) when IsNonStandard && string.IsNullOrWhiteSpace(DescriptionEn) => "非标行必须填写产品描述。",
        _ => string.Empty,
    };

    protected override void LoadFromDto(SalesOrderDocumentLineDto dto) => dto.Adapt(this);

    public override SalesOrderDocumentLineDto ToDto() => this.Adapt<SalesOrderDocumentLineDto>();

    public SalesOrderEditLineDto ToEditDto()
    {
        var dto = this.Adapt<SalesOrderEditLineDto>();
        if (dto.IsNonStandard)
        {
            dto.ProductId = null;
            dto.CustomerProductId = null;
            dto.Sku = NonStandardSkuGenerator.Create(dto.Uid);
        }

        return dto;
    }

    public void ApplyProduct(SalesOrderProductLookupDto product)
    {
        if (IsNonStandard)
        {
            return;
        }

        ProductId = product.ProductId;
        CustomerProductId = product.CustomerProductId;
        CustomerSku = product.CustomerSku ?? string.Empty;
        Sku = product.Sku ?? string.Empty;
        LinkSku = product.LinkSku ?? string.Empty;
        DescriptionEn = product.DescriptionEn ?? string.Empty;
        ColorNameEn = product.ColorNameEn ?? string.Empty;
        FeaturesEn = product.FeaturesEn ?? string.Empty;
        MainImage = product.MainImage ?? string.Empty;
        CnyPrice = product.CnyPrice;
        UsdPrice = product.UsdPrice;
    }

    public override string? ValidateRow()
    {
        if (Qty <= 0)
        {
            return "数量必须大于零。";
        }

        if (IsNonStandard)
        {
            if (ProductId is not null)
            {
                return "非标行不能关联产品主数据。";
            }

            if (!NonStandardSkuGenerator.IsGeneratedNonStandardSku(Sku))
            {
                return "非标行编号不正确。";
            }

            if (string.IsNullOrWhiteSpace(DescriptionEn))
            {
                return "非标行必须填写产品描述。";
            }

            return null;
        }

        if (ProductId is null || ProductId == Guid.Empty || string.IsNullOrWhiteSpace(Sku))
        {
            return "标准行必须通过产品 Lookup 选择产品。";
        }

        return null;
    }

    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
            || propertyName is nameof(LineTypeText)
                or nameof(CnyAmount)
                or nameof(UsdAmount)
                or nameof(MainImage)
                or nameof(ShippedQty)
                or nameof(UnshippedQty);
    }
}
