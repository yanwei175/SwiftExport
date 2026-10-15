using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>当前物料的包装结果编辑行，不把包材档案自重或尺寸映射为装箱结果。</summary>
public sealed partial class ProductPackageLineModel : BaseLineModel<ProductPackageDto>
{
    private (Guid? Id, string? Sku, string? Description, string? Unit, bool Valid)? _materialBaseline;
    /// <summary>只读归属身份，保存由外层物料请求决定。</summary>
    [ObservableProperty] public partial Guid OwnerProductId { get; set; }
    /// <summary>包装字典稳定代码。</summary>
    [ObservableProperty] public partial string PackageType { get; set; } = ProductPackageTypeCodes.Carton;
    /// <summary>可空的包材物料身份。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MaterialStatus))] public partial Guid? PackagingMaterialId { get; set; }
    /// <summary>成品基础单位装率。</summary>
    [ObservableProperty] public partial int PcsPerPackage { get; set; } = 1;
    /// <summary>包装结果长度 CM。</summary>
    [ObservableProperty] public partial decimal? Length_CM { get; set; }
    /// <summary>包装结果宽度 CM。</summary>
    [ObservableProperty] public partial decimal? Width_CM { get; set; }
    /// <summary>包装结果高度 CM。</summary>
    [ObservableProperty] public partial decimal? Height_CM { get; set; }
    /// <summary>包装结果净重 KG。</summary>
    [ObservableProperty] public partial decimal? Nw_KG { get; set; }
    /// <summary>包装结果毛重 KG。</summary>
    [ObservableProperty] public partial decimal? Gw_KG { get; set; }
    /// <summary>手工包装体积 CBM。</summary>
    [ObservableProperty] public partial decimal? Cbm { get; set; }
    /// <summary>规格备注。</summary>
    [ObservableProperty] public partial string? Note { get; set; }
    /// <summary>包材当前编号，只读显示。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MaterialStatus))] public partial string? PackagingMaterialSku { get; set; }
    /// <summary>包材当前中文描述，只读显示。</summary>
    [ObservableProperty] public partial string? PackagingMaterialDescription { get; set; }
    /// <summary>包材当前基础单位，与成品装率口径无关。</summary>
    [ObservableProperty] public partial string? PackagingMaterialUnit { get; set; }
    /// <summary>失效引用仍保留，允许用户显式清除或重新选择。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MaterialStatus))] public partial bool IsPackagingMaterialValid { get; set; } = true;
    /// <summary>非颜色的引用状态反馈。</summary>
    public string MaterialStatus => !PackagingMaterialId.HasValue ? "未选择包材" : IsPackagingMaterialValid ? PackagingMaterialSku ?? "已关联包材"
        : $"包材引用已失效：{PackagingMaterialSku ?? PackagingMaterialId.ToString()}";

    partial void OnPackageTypeChanged(string value)
    { if (IsNew && value == ProductPackageTypeCodes.ShippingBox) PcsPerPackage = 1; }
    /// <summary>基类撤销身份时同步恢复只读展示；展示本身保持不参与业务脏跟踪。</summary>
    partial void OnPackagingMaterialIdChanged(Guid? value)
    {
        if (_materialBaseline is not { } baseline || baseline.Id != value) return;
        PackagingMaterialSku = baseline.Sku; PackagingMaterialDescription = baseline.Description;
        PackagingMaterialUnit = baseline.Unit; IsPackagingMaterialValid = baseline.Valid;
    }
    /// <summary>本地保存验证负责首错摘要，数据库字典及并发仍由服务端权威重验。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(PackageType)) return "请选择包装类型。";
        if (PcsPerPackage <= 0) return "装率必须为正整数。";
        if (new[] { Length_CM, Width_CM, Height_CM, Nw_KG, Gw_KG, Cbm }.Any(value => value < 0)) return "包装尺寸、重量和体积不能为负数。";
        if (Nw_KG.HasValue && Gw_KG.HasValue && Gw_KG < Nw_KG) return "包装毛重不能小于净重。";
        if (PackagingMaterialId == Guid.Empty) return "包材身份无效，请重新选择或清除。";
        return Note?.Length > 2000 ? "包装备注不能超过 2000 个字符。" : null;
    }
    /// <summary>展示元数据不参与脏跟踪，关系身份和包装结果仍由基类跟踪。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) => base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(PackagingMaterialSku) or nameof(PackagingMaterialDescription) or nameof(PackagingMaterialUnit)
            or nameof(IsPackagingMaterialValid) or nameof(MaterialStatus);
    /// <inheritdoc />
    protected override void LoadFromDto(ProductPackageDto dto)
    {
        dto.Adapt(this);
        _materialBaseline = (dto.Data.PackagingMaterialId, dto.PackagingMaterialSku, dto.PackagingMaterialDescription, dto.PackagingMaterialUnit, dto.IsPackagingMaterialValid);
    }
    /// <inheritdoc />
    public override ProductPackageDto ToDto() => this.Adapt<ProductPackageDto>();
}
