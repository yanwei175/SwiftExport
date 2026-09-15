using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>装箱单物理行编辑模型，来源身份不参与采购分摊。</summary>
public sealed partial class PackingListLineModel : BaseLineModel<PackingListLineDto>
{
    /// <summary>所属装箱单头 Uid，逻辑关联。</summary>
    [ObservableProperty]
    public partial Guid PackingListHeadId { get; set; }

    /// <summary>单据内行号，同来源允许多行。</summary>
    [ObservableProperty]
    public partial int LineNumber { get; set; }

    /// <summary>唯一交付来源锚点，不表示采购或物料分摊。</summary>
    [ObservableProperty]
    public partial Guid GrossDemandId { get; set; }

    /// <summary>本次交付的产品标识。</summary>
    [ObservableProperty]
    public partial Guid ProductId { get; set; }

    /// <summary>产品编码快照。</summary>
    [ObservableProperty]
    public partial string? SkuSnapshot { get; set; }

    /// <summary>关联型号快照。</summary>
    [ObservableProperty]
    public partial string? LinkSkuSnapshot { get; set; }

    /// <summary>客户型号快照。</summary>
    [ObservableProperty]
    public partial string? CustomerSkuSnapshot { get; set; }

    /// <summary>英文描述快照。</summary>
    [ObservableProperty]
    public partial string? DescriptionEnSnapshot { get; set; }

    /// <summary>英文颜色名称快照。</summary>
    [ObservableProperty]
    public partial string? ColorNameEnSnapshot { get; set; }

    /// <summary>库存及交付数量的计量单位快照。</summary>
    [ObservableProperty]
    public partial string? UnitSnapshot { get; set; }

    /// <summary>本行计划交付数量，草稿不形成正式交付事实。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedPackageCount))]
    public partial decimal Qty { get; set; }

    /// <summary>每包数量；未知为空，已知时须大于零。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedPackageCount))]
    public partial decimal? PcsPerPackage { get; set; }

    /// <summary>实际整包装数；默认建议向上取整，允许人工指定，未知为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubTotalNw))]
    [NotifyPropertyChangedFor(nameof(SubTotalGw))]
    [NotifyPropertyChangedFor(nameof(SubTotalCbm))]
    public partial decimal? PackageCount { get; set; }

    /// <summary>每包净重。未知为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubTotalNw))]
    public partial decimal? NwPerPackage { get; set; }

    /// <summary>每包毛重。未知为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubTotalGw))]
    public partial decimal? GwPerPackage { get; set; }

    /// <summary>每包体积。未知为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubTotalCbm))]
    public partial decimal? CbmPerPackage { get; set; }

    /// <summary>客户采购编号文本快照，不作为关系主键。</summary>
    [ObservableProperty]
    public partial string? CustomerPoNo { get; set; }

    /// <summary>行上手工填写的发票号文本，不关联财务发票。</summary>
    [ObservableProperty]
    public partial string? InvoiceNo { get; set; }

    /// <summary>本行箱唛。</summary>
    [ObservableProperty]
    public partial string? Marks { get; set; }

    /// <summary>本行备注。</summary>
    [ObservableProperty]
    public partial string? Note { get; set; }

    /// <summary>本次库存操作仓库，草稿允许未确定。</summary>
    [ObservableProperty]
    public partial Guid? WarehouseSiteId { get; set; }

    /// <summary>采用的仓库编码快照。</summary>
    [ObservableProperty]
    public partial string? WarehouseCodeSnapshot { get; set; }

    /// <summary>采用的仓库名称快照。</summary>
    [ObservableProperty]
    public partial string? WarehouseNameSnapshot { get; set; }

    /// <summary>查询展示图片，不持久化。</summary>
    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    /// <summary>接纳服务端字段；映射差异由集中配置负责。</summary>
    protected override void LoadFromDto(PackingListLineDto dto) => dto.Adapt(this);

    /// <summary>导出当前模型快照，不代表保存命令。</summary>
    public override PackingListLineDto ToDto() => this.Adapt<PackingListLineDto>();

    /// <summary>建议包装数，不自动覆盖用户指定的实际包装数。</summary>
    public decimal? SuggestedPackageCount
    {
        get
        {
            if (!(PcsPerPackage > 0)) return null;
            try { return decimal.Ceiling(Qty / PcsPerPackage.Value); }
            catch (OverflowException) { return null; }
        }
    }

    /// <summary>按实际包装数计算净重，未知输入保持为空。</summary>
    public decimal? SubTotalNw => CalculateSubtotal(NwPerPackage);

    /// <summary>按实际包装数计算毛重，未知输入保持为空。</summary>
    public decimal? SubTotalGw => CalculateSubtotal(GwPerPackage);

    /// <summary>按实际包装数计算体积，未知输入保持为空。</summary>
    public decimal? SubTotalCbm => CalculateSubtotal(CbmPerPackage);

    /// <summary>本地同步校验不替代后端来源、库存和权限校验。</summary>
    public override string? ValidateRow()
    {
        if (Qty <= 0) return "交付数量必须大于零。";
        if (PcsPerPackage.HasValue && PcsPerPackage <= 0) return "每包数量必须大于零。";
        if (PackageCount.HasValue && (PackageCount < 0 || decimal.Truncate(PackageCount.Value) != PackageCount))
            return "实际包装数必须为非负整数。";
        if (NwPerPackage < 0 || GwPerPackage < 0 || CbmPerPackage < 0)
            return "重量和体积不得为负数。";
        if (PcsPerPackage > 0 && SuggestedPackageCount is null)
            return "建议包装数超出允许的数值范围。";
        if (PackageCount.HasValue && ((NwPerPackage.HasValue && SubTotalNw is null)
            || (GwPerPackage.HasValue && SubTotalGw is null) || (CbmPerPackage.HasValue && SubTotalCbm is null)))
            return "包装小计超出允许的数值范围。";
        return null;
    }

    /// <summary>展示计算和图片变化不生成持久化修改。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(SuggestedPackageCount) or nameof(SubTotalNw)
            or nameof(SubTotalGw) or nameof(SubTotalCbm) or nameof(ImagePath);

    // 小计采用约定精度；极端输入溢出保持未知，校验时报告。
    private decimal? CalculateSubtotal(decimal? perPackage)
    {
        if (!PackageCount.HasValue || !perPackage.HasValue) return null;
        try { return decimal.Round(PackageCount.Value * perPackage.Value, 4, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return null; }
    }
}
