using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>售后问题与实际补件同行编辑模型，保留稳定行身份和本地变更状态。</summary>
public sealed partial class AfterSalesLineModel : ObservableObject
{
    private bool _loading;

    /// <summary>建立售后行变更跟踪。</summary>
    public AfterSalesLineModel() => PropertyChanged += (_, args) =>
    {
        if (!_loading && args.PropertyName is not (nameof(IsDirty) or nameof(IsDeleted) or nameof(IsSelected))) IsDirty = true;
    };

    [ObservableProperty] public partial Guid Uid { get; set; }
    [ObservableProperty] public partial int Version { get; set; } = 1;
    [ObservableProperty] public partial int LineNumber { get; set; }
    [ObservableProperty] public partial Guid? OriginalSalesOrderLineId { get; set; }
    [ObservableProperty] public partial Guid? ResponsibleSupplierId { get; set; }
    /// <summary>获取或设置当前通过缓存选择的责任供应商。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedResponsibleSupplier { get; set; }
    [ObservableProperty] public partial Guid? IssueProductId { get; set; }
    [ObservableProperty] public partial string IssueSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueCustomerSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueLinkSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? IssueColorId { get; set; }
    /// <summary>获取或设置当前通过缓存选择的问题颜色。</summary>
    [ObservableProperty] public partial ProductColorDto? SelectedIssueColor { get; set; }
    [ObservableProperty] public partial string IssueColorSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueProductDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? QualityCategoryId { get; set; }
    [ObservableProperty] public partial string CauseAnalysis { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? ProductId { get; set; }
    [ObservableProperty] public partial bool IsNonStandard { get; set; }
    [ObservableProperty] public partial string SkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? ColorId { get; set; }
    [ObservableProperty] public partial string ColorSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string SpecificationSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? Quantity { get; set; }
    [ObservableProperty] public partial string UnitSnapshot { get; set; } = "Pcs";
    [ObservableProperty] public partial DateTime? RequiredDate { get; set; }
    [ObservableProperty] public partial decimal? ServiceAmountCny { get; set; }
    [ObservableProperty] public partial DateTime? ReturnedReceivedDate { get; set; }
    [ObservableProperty] public partial string TreatmentDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsNew { get; private set; }
    [ObservableProperty] public partial bool IsDirty { get; private set; }
    [ObservableProperty] public partial bool IsDeleted { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>从服务端行读取编辑基线。</summary>
    public void Load(AfterSalesLineDto dto)
    {
        _loading = true;
        dto.Adapt(this);
        IsNew = false;
        IsDeleted = false;
        _loading = false;
        IsDirty = false;
    }

    /// <summary>创建稳定身份的新投诉行，单位先使用 Pcs。</summary>
    public static AfterSalesLineModel CreateNew(int lineNumber) => new()
    {
        Uid = GuidSequentialGenerator.NewGuid(), Version = 1, LineNumber = lineNumber, UnitSnapshot = "Pcs", IsNew = true, IsDirty = true
    };

    /// <summary>生成服务端售后行写入契约。</summary>
    public AfterSalesEditLineDto ToDto() => this.Adapt<AfterSalesEditLineDto>();

    /// <summary>同步缓存 DTO 选择而不改变当前行的业务字段或脏状态。</summary>
    public void SynchronizeLookupSelections(ProductColorDto? color, SupplierDto? supplier)
    {
        var wasLoading = _loading;
        var wasDirty = IsDirty;
        _loading = true;
        SelectedIssueColor = color;
        SelectedResponsibleSupplier = supplier;
        _loading = wasLoading;
        IsDirty = wasDirty;
    }

    partial void OnSelectedIssueColorChanged(ProductColorDto? value)
    {
        if (_loading) return;
        IssueColorId = value?.Uid;
        IssueColorSnapshot = value?.ColorNameZh ?? string.Empty;
    }

    partial void OnSelectedResponsibleSupplierChanged(SupplierDto? value)
    {
        if (!_loading) ResponsibleSupplierId = value?.Uid;
    }

    /// <summary>返回当前行的本地校验信息；正数量确认校验由详情页另行提升。</summary>
    public string? Validate(bool forConfirmation)
    {
        if (string.IsNullOrWhiteSpace(IssueDescription)) return "投诉描述不能为空。";
        if (Quantity < 0) return "补发数量不能为负数。";
        if (Quantity is > 99999999999999.9999m || Quantity.HasValue && decimal.Round(Quantity.Value, 4) != Quantity.Value) return "补发数量最多十四位整数、四位小数。";
        if (ServiceAmountCny < 0) return "人民币行金额不能为负数。";
        if (ServiceAmountCny is > 9999999999999999.99m || ServiceAmountCny.HasValue && decimal.Round(ServiceAmountCny.Value, 2) != ServiceAmountCny.Value) return "人民币行金额最多十六位整数、两位小数。";
        if (!(Quantity > 0) || !forConfirmation) return null;
        if (!ResponsibleSupplierId.HasValue) return "正数量行必须选择责任供应商。";
        if (!ServiceAmountCny.HasValue) return "正数量行必须填写人民币行金额，免费填 0。";
        if (IsNonStandard && ProductId.HasValue) return "非标补件不能同时选择标准产品。";
        if (IsNonStandard && string.IsNullOrWhiteSpace(DescriptionSnapshot)) return "非标补件必须填写补发描述。";
        if (!IsNonStandard && !ProductId.HasValue) return "标准补件必须选择产品。";
        if (DescriptionSnapshot.Length > 300) return "确认时补发描述不能超过 300 字。";
        if (SpecificationSnapshot.Length > 300) return "确认时补发规格不能超过 300 字。";
        return null;
    }
}
