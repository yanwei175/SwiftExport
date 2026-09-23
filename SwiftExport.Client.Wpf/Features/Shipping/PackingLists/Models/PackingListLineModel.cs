using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
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

    /// <summary>拼箱归属；为空保持普通包装，不改变来源身份。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSplitTail))]
    [NotifyPropertyChangedFor(nameof(IsMixedPacking))]
    public partial Guid? PackingGroupId { get; set; }

    /// <summary>当前行是否使用共享箱级指标。</summary>
    public bool IsMixedPacking => PackingGroupId.HasValue;

    /// <summary>仅用于集合视图分组，不参与保存和脏跟踪。</summary>
    [ObservableProperty]
    public partial PackingListPackingGroupModel? DisplayGroup { get; set; }

    /// <summary>唯一交付来源锚点，不表示采购或物料分摊。</summary>
    [ObservableProperty]
    public partial Guid GrossDemandId { get; set; }

    /// <summary>标准行产品标识；非标行为空。</summary>
    [ObservableProperty]
    public partial Guid? ProductId { get; set; }

    /// <summary>是否为不产生库存事实的非标来源快照。</summary>
    [ObservableProperty]
    public partial bool IsNonStandardSnapshot { get; set; }

    /// <summary>来源类型快照。</summary>
    [ObservableProperty]
    public partial string? SourceTypeSnapshot { get; set; }

    /// <summary>来源单号快照。</summary>
    [ObservableProperty]
    public partial string? SourceDocumentNoSnapshot { get; set; }

    /// <summary>来源行号快照。</summary>
    [ObservableProperty]
    public partial int? SourceLineNumberSnapshot { get; set; }

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
    [NotifyPropertyChangedFor(nameof(PackageCount))]
    [NotifyPropertyChangedFor(nameof(CanSplitTail))]
    [NotifyPropertyChangedFor(nameof(SubTotalNw))]
    [NotifyPropertyChangedFor(nameof(SubTotalGw))]
    [NotifyPropertyChangedFor(nameof(SubTotalCbm))]
    public partial decimal Qty { get; set; }

    /// <summary>每包数量；未知为空，已知时须大于零。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackageCount))]
    [NotifyPropertyChangedFor(nameof(CanSplitTail))]
    [NotifyPropertyChangedFor(nameof(SubTotalNw))]
    [NotifyPropertyChangedFor(nameof(SubTotalGw))]
    [NotifyPropertyChangedFor(nameof(SubTotalCbm))]
    public partial decimal? PcsPerPackage { get; set; }


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





    /// <summary>查询展示图片，不持久化。</summary>
    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    /// <summary>接纳服务端字段；映射差异由集中配置负责。</summary>
    protected override void LoadFromDto(PackingListLineDto dto) => dto.Adapt(this);

    /// <summary>导出当前模型快照，不代表保存命令。</summary>
    public override PackingListLineDto ToDto() => this.Adapt<PackingListLineDto>();

    /// <summary>箱数实时由数量除以每箱数量计算，四位小数；不接受独立手工编辑。</summary>
    public decimal? PackageCount
    {
        get
        {
            if (!(PcsPerPackage > 0))
                return null;
            try
            {
                return decimal.Round(
                    Qty / PcsPerPackage.Value,
                    4,
                    MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }

    /// <summary>用原始数量余数判断是否有尾箱，不受显示舍入影响。</summary>
    public bool CanSplitTail => !IsMixedPacking && Qty > 0 && PcsPerPackage > 0 && Qty % PcsPerPackage.Value != 0;

    /// <summary>按实际包装数计算净重，未知输入保持为空。</summary>
    public decimal? SubTotalNw => CalculateSubtotal(NwPerPackage);

    /// <summary>按实际包装数计算毛重，未知输入保持为空。</summary>
    public decimal? SubTotalGw => CalculateSubtotal(GwPerPackage);

    /// <summary>按实际包装数计算体积，未知输入保持为空。</summary>
    public decimal? SubTotalCbm => CalculateSubtotal(CbmPerPackage);

    /// <summary>本地同步校验不替代后端来源、库存和权限校验。</summary>
    public override string? ValidateRow()
    {
        if (GrossDemandId == Guid.Empty) return "来源身份不能为空。";
        if (IsNonStandardSnapshot && ProductId.HasValue)
            return "非标行不能关联标准产品。";
        if (!IsNonStandardSnapshot && (!ProductId.HasValue || ProductId == Guid.Empty))
            return "标准行必须关联产品。";
        if (Qty <= 0) return "交付数量必须大于零。";
        if (PcsPerPackage.HasValue && PcsPerPackage <= 0) return "每包数量必须大于零。";
        // 每箱数量按完整件数填写，不能超过本行实际出货数量；未知值仍允许待填。
        if (!IsMixedPacking && PcsPerPackage.HasValue && decimal.Truncate(PcsPerPackage.Value) != PcsPerPackage.Value)
            return "每箱数量必须是整数。";
        if (PcsPerPackage > Qty)
            return "每箱数量不能大于出货数量。";
        // 每箱数量按完整件数填写，不能超过本行实际出货数量；未知值仍允许待填。
        if (!IsMixedPacking && PcsPerPackage.HasValue && decimal.Truncate(PcsPerPackage.Value) != PcsPerPackage.Value)
            return "每箱数量必须是整数。";
        if (PcsPerPackage > Qty)
            return "每箱数量不能大于出货数量。";
        if (NwPerPackage < 0 || GwPerPackage < 0 || CbmPerPackage < 0)
            return "重量和体积不得为负数。";
        // 尾箱指标允许待填；只有两者已知时比较，不能把空值当作零毛重。
        if (NwPerPackage.HasValue && GwPerPackage.HasValue && NwPerPackage > GwPerPackage)
            return "单箱净重不能大于单箱毛重。";
        // 尾箱指标允许待填；只有两者已知时比较，不能把空值当作零毛重。
        if (NwPerPackage.HasValue && GwPerPackage.HasValue && NwPerPackage > GwPerPackage)
            return "单箱净重不能大于单箱毛重。";
        if (PcsPerPackage > 0 && PackageCount is null)
            return "箱数超出允许的数值范围。";
        if (PackageCount.HasValue && ((NwPerPackage.HasValue && SubTotalNw is null)
            || (GwPerPackage.HasValue && SubTotalGw is null) || (CbmPerPackage.HasValue && SubTotalCbm is null)))
            return "包装小计超出允许的数值范围。";
        return null;
    }

    /// <summary>展示计算和图片变化不生成持久化修改。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(DisplayGroup) or nameof(IsMixedPacking)
        || propertyName is nameof(PackageCount) or nameof(CanSplitTail) or nameof(SubTotalNw)
            or nameof(SubTotalGw) or nameof(SubTotalCbm) or nameof(ImagePath);

    // 小计采用约定精度；极端输入溢出保持未知，校验时报告。
    private decimal? CalculateSubtotal(decimal? perPackage)
    {
        if (!PackageCount.HasValue || !perPackage.HasValue) return null;
        try { return decimal.Round(PackageCount.Value * perPackage.Value, 4, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return null; }
    }
}
