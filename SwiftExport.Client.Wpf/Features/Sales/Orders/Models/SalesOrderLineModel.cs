using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExportClient.Mvvm;
using System.ComponentModel;
using System.Globalization;

namespace SwiftExportClient.Features.Sales.Orders.Models;

public sealed partial class SalesOrderLineModel : BaseLineModel<SalesOrderDocumentLineDto>, IDataErrorInfo
{
    private bool _synchronizingEditorText;

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
    [ObservableProperty] public partial string QtyText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CnyPriceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string UsdPriceText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpectedDeliveryDateDisplayText))]
    public partial string ExpectedDeliveryDateText { get; set; } = string.Empty;

    public string LineTypeText => IsNonStandard ? "非标" : "标准";
    public decimal? CnyAmount => CnyPrice * Qty;
    public decimal? UsdAmount => UsdPrice * Qty;
    public string ExpectedDeliveryDateDisplayText => FormatDateDisplayText(ExpectedDeliveryDateText);
    public string Error => ValidateRow() ?? string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(QtyText) => GetQtyTextError() ?? string.Empty,
        nameof(CnyPriceText) => GetNullableDecimalTextError(CnyPriceText, "CNY 单价") ?? string.Empty,
        nameof(UsdPriceText) => GetNullableDecimalTextError(UsdPriceText, "USD 单价") ?? string.Empty,
        nameof(ExpectedDeliveryDateText) => GetDateTextError() ?? string.Empty,
        nameof(DescriptionEn) when IsNonStandard && string.IsNullOrWhiteSpace(DescriptionEn) => "非标行必须填写产品描述。",
        _ => string.Empty,
    };

    protected override void LoadFromDto(SalesOrderDocumentLineDto dto)
    {
        dto.Adapt(this);
        SynchronizeEditorTexts();
    }

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
        var editorError = GetQtyTextError()
            ?? GetNullableDecimalTextError(CnyPriceText, "CNY 单价")
            ?? GetNullableDecimalTextError(UsdPriceText, "USD 单价")
            ?? GetDateTextError();
        if (!string.IsNullOrWhiteSpace(editorError))
        {
            return editorError;
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

    partial void OnQtyTextChanged(string value)
    {
        if (TryParseDecimal(value, out var parsedValue))
        {
            SetStrongValue(() => Qty = parsedValue);
        }
    }

    partial void OnCnyPriceTextChanged(string value) =>
        ApplyNullableDecimalText(value, parsedValue => CnyPrice = parsedValue);

    partial void OnUsdPriceTextChanged(string value) =>
        ApplyNullableDecimalText(value, parsedValue => UsdPrice = parsedValue);

    partial void OnExpectedDeliveryDateTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStrongValue(() => ExpectedDeliveryDate = null);
            return;
        }

        if (DateTime.TryParse(
                value.Trim(),
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDate))
        {
            SetStrongValue(() => ExpectedDeliveryDate = parsedDate.Date);
        }
    }

    partial void OnQtyChanged(decimal value) =>
        SynchronizeEditorText(() => QtyText = FormatDecimal(value));

    partial void OnCnyPriceChanged(decimal? value) =>
        SynchronizeEditorText(() => CnyPriceText = FormatNullableDecimal(value));

    partial void OnUsdPriceChanged(decimal? value) =>
        SynchronizeEditorText(() => UsdPriceText = FormatNullableDecimal(value));

    partial void OnExpectedDeliveryDateChanged(DateTime? value) =>
        SynchronizeEditorText(() => ExpectedDeliveryDateText = value?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? string.Empty);

    private string? GetQtyTextError()
    {
        if (!TryParseDecimal(QtyText, out var parsedValue))
        {
            return "请输入有效数量。";
        }

        return parsedValue <= 0 ? "数量必须大于零。" : null;
    }

    private string? GetDateTextError()
    {
        return string.IsNullOrWhiteSpace(ExpectedDeliveryDateText)
            || DateTime.TryParse(
                ExpectedDeliveryDateText.Trim(),
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out _)
                ? null
                : "请输入有效预计交期。";
    }

    private static string? GetNullableDecimalTextError(string value, string displayName)
    {
        return string.IsNullOrWhiteSpace(value) || TryParseDecimal(value, out _)
            ? null
            : $"请输入有效的 {displayName}。";
    }

    private void ApplyNullableDecimalText(string value, Action<decimal?> setter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStrongValue(() => setter(null));
            return;
        }

        if (TryParseDecimal(value, out var parsedValue))
        {
            SetStrongValue(() => setter(parsedValue));
        }
    }

    private void SetStrongValue(Action setter)
    {
        _synchronizingEditorText = true;
        try
        {
            setter();
        }
        finally
        {
            _synchronizingEditorText = false;
        }
    }

    private void SynchronizeEditorText(Action setter)
    {
        if (_synchronizingEditorText)
        {
            return;
        }

        _synchronizingEditorText = true;
        try
        {
            setter();
        }
        finally
        {
            _synchronizingEditorText = false;
        }
    }

    private void SynchronizeEditorTexts()
    {
        SynchronizeEditorText(() =>
        {
            QtyText = FormatDecimal(Qty);
            CnyPriceText = FormatNullableDecimal(CnyPrice);
            UsdPriceText = FormatNullableDecimal(UsdPrice);
            ExpectedDeliveryDateText = ExpectedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? string.Empty;
        });
    }

    private static bool TryParseDecimal(string value, out decimal parsedValue) =>
        decimal.TryParse(
            value?.Trim(),
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out parsedValue);

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.CurrentCulture);

    private static string FormatNullableDecimal(decimal? value) =>
        value.HasValue ? FormatDecimal(value.Value) : string.Empty;

    private static string FormatDateDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return DateTime.TryParse(
            value.Trim(),
            CultureInfo.CurrentCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
            : value;
    }

    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
            || propertyName is nameof(LineTypeText)
                or nameof(CnyAmount)
                or nameof(UsdAmount)
                or nameof(ExpectedDeliveryDateDisplayText)
                or nameof(MainImage)
                or nameof(ShippedQty)
                or nameof(UnshippedQty);
    }
}
