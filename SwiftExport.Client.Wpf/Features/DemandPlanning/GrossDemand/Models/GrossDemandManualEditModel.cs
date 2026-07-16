using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

public sealed partial class GrossDemandManualEditModel : ObservableValidator
{
    private Snapshot _baseline = new();

    public Guid Uid { get; private set; }
    public int Version { get; private set; }
    public bool IsNew => Uid == Guid.Empty;
    public bool IsDirty => !CreateSnapshot().Equals(_baseline);
    public bool CanSelectProduct => !IsNonStandard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial Guid? ProductId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(CanSelectProduct))]
    public partial bool IsNonStandard { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Sku { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string LinkSku { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string CustomerSku { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Color { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Specification { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string ImagePath { get; set; } = string.Empty;

    [ObservableProperty]
    [Range(typeof(decimal), "0.0001", "99999999999999", ErrorMessage = "需求数量必须大于 0。")]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial decimal DemandQty { get; set; } = 1m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial DateTime? RequiredDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Remark { get; set; } = string.Empty;

    public void ResetForCreate()
    {
        Uid = Guid.Empty;
        Version = 0;
        ProductId = null;
        IsNonStandard = false;
        Sku = string.Empty;
        LinkSku = string.Empty;
        CustomerSku = string.Empty;
        Description = string.Empty;
        Color = string.Empty;
        Specification = string.Empty;
        ImagePath = string.Empty;
        DemandQty = 1m;
        RequiredDate = null;
        Remark = string.Empty;
        ClearErrors();
        AcceptChanges();
    }

    public void Load(GrossDemandDetailDto dto)
    {
        Uid = dto.Uid;
        Version = dto.Version;
        ProductId = dto.ProductId;
        IsNonStandard = dto.IsNonStandard;
        Sku = dto.Sku ?? string.Empty;
        LinkSku = dto.LinkSku ?? string.Empty;
        CustomerSku = dto.CustomerSku ?? string.Empty;
        Description = dto.Description ?? string.Empty;
        Color = dto.Color ?? string.Empty;
        Specification = dto.Specification ?? string.Empty;
        ImagePath = dto.ImagePath ?? string.Empty;
        DemandQty = dto.DemandQty;
        RequiredDate = dto.RequiredDate;
        Remark = dto.Remark ?? string.Empty;
        ClearErrors();
        AcceptChanges();
    }

    public void ApplyProduct(ProductLookupDto product)
    {
        ProductId = product.Uid;
        IsNonStandard = false;
        Sku = product.Sku ?? string.Empty;
        LinkSku = product.LinkSku ?? string.Empty;
        Description = !string.IsNullOrWhiteSpace(product.DescriptionEn)
            ? product.DescriptionEn
            : product.DescriptionZh ?? string.Empty;
        Color = product.Finish ?? string.Empty;
        Specification = !string.IsNullOrWhiteSpace(product.FeaturesEn)
            ? product.FeaturesEn
            : product.FeaturesZh ?? string.Empty;
        ImagePath = product.MainImage ?? string.Empty;
    }

    public bool ValidateForSave(out string message)
    {
        ValidateAllProperties();
        if (!IsNonStandard && ProductId is null)
        {
            message = "标准件必须选择产品。";
            return false;
        }

        if (IsNonStandard && ProductId is not null)
        {
            message = "非标件不能关联产品。";
            return false;
        }

        if (IsNonStandard && string.IsNullOrWhiteSpace(Description))
        {
            message = "非标件必须填写描述；临时 SKU 将由系统生成。";
            return false;
        }

        if (HasErrors)
        {
            message = string.Join(" ", GetErrors().Select(error => error.ErrorMessage));
            return false;
        }

        message = string.Empty;
        return true;
    }

    public CreateManualGrossDemandRequest ToCreateRequest() => new()
    {
        ProductId = ProductId,
        IsNonStandard = IsNonStandard,
        Sku = Sku.Trim(),
        LinkSku = LinkSku.Trim(),
        CustomerSku = CustomerSku.Trim(),
        Description = Description.Trim(),
        Color = Color.Trim(),
        Specification = Specification.Trim(),
        ImagePath = ImagePath.Trim(),
        DemandQty = DemandQty,
        RequiredDate = RequiredDate,
        Remark = Remark.Trim(),
    };

    public UpdateManualGrossDemandRequest ToUpdateRequest() => new()
    {
        Uid = Uid,
        Version = Version,
        ProductId = ProductId,
        IsNonStandard = IsNonStandard,
        Sku = Sku.Trim(),
        LinkSku = LinkSku.Trim(),
        CustomerSku = CustomerSku.Trim(),
        Description = Description.Trim(),
        Color = Color.Trim(),
        Specification = Specification.Trim(),
        ImagePath = ImagePath.Trim(),
        DemandQty = DemandQty,
        RequiredDate = RequiredDate,
        Remark = Remark.Trim(),
    };

    public void AcceptChanges()
    {
        _baseline = CreateSnapshot();
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnIsNonStandardChanged(bool value)
    {
        if (value)
        {
            ProductId = null;
        }
    }

    private Snapshot CreateSnapshot() => new(
        Uid, Version, ProductId, IsNonStandard, Sku, LinkSku, CustomerSku, Description,
        Color, Specification, ImagePath, DemandQty, RequiredDate, Remark);

    private sealed record Snapshot(
        Guid Uid = default,
        int Version = 0,
        Guid? ProductId = null,
        bool IsNonStandard = false,
        string Sku = "",
        string LinkSku = "",
        string CustomerSku = "",
        string Description = "",
        string Color = "",
        string Specification = "",
        string ImagePath = "",
        decimal DemandQty = 0,
        DateTime? RequiredDate = null,
        string Remark = "");
}
