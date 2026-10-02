using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExportClient.Mvvm;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>实际补件编辑模型，保留稳定行身份和本地变更状态。</summary>
public sealed partial class AfterSalesLineModel : BaseLineModel<AfterSalesLineDto>, System.ComponentModel.IEditableObject
{
    private AfterSalesLineDto? _editSnapshot;

    /// <summary>记录当前行编辑事务，支持原生 Esc 取消本次编辑。</summary>
    public void BeginEdit() => _editSnapshot ??= ToDto();

    /// <summary>接受表格当前行编辑，保留公共模型的保存基线。</summary>
    public void EndEdit() => _editSnapshot = null;

    /// <summary>恢复本次行编辑前的数据，不撤销此前尚未保存的修改。</summary>
    public void CancelEdit()
    {
        if (_editSnapshot is not { } snapshot) return;
        _editSnapshot = null;
        snapshot.Adapt(this);
    }

    [ObservableProperty] public partial int LineNumber { get; set; }
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
    [ObservableProperty] public partial string UnitSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    /// <summary>补件图片路径，显示变化不构成业务修改。</summary>
    [ObservableProperty] public partial string? ImagePath { get; set; }

    /// <inheritdoc/>
    protected override void LoadFromDto(AfterSalesLineDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override AfterSalesLineDto ToDto() => this.Adapt<AfterSalesLineDto>();

    /// <summary>生成不含图片显示信息的保存契约。</summary>
    public AfterSalesEditLineDto ToEditDto() => this.Adapt<AfterSalesEditLineDto>();

    /// <inheritdoc/>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        propertyName is nameof(ImagePath) or nameof(IsEmptyPlaceholder) || base.IsTrackingIgnoredProperty(propertyName);

    /// <summary>创建稳定身份的新补件行，单位待选择产品或明确填写。</summary>
    public static AfterSalesLineModel CreateNew(int lineNumber)
    {
        var line = new AfterSalesLineModel();
        line.Load(new() { Uid = GuidSequentialGenerator.NewGuid(), Version = 1, LineNumber = lineNumber });
        line.MarkAsAdded();
        return line;
    }

    /// <summary>返回当前行的本地校验信息；正数量确认校验由详情页另行提升。</summary>
    public string? Validate(bool forConfirmation)
    {
        if (Quantity < 0) return "补发数量不能为负数。";
        if (Quantity is > 99999999999999.9999m || Quantity.HasValue && decimal.Round(Quantity.Value, 4) != Quantity.Value) return "补发数量最多十四位整数、四位小数。";
        if (IsNonStandard && ProductId.HasValue) return "非标补件不能同时选择标准产品。";
        if (!(Quantity > 0) || !forConfirmation) return null;
        if (IsNonStandard && string.IsNullOrWhiteSpace(DescriptionSnapshot)) return "非标补件必须填写补发描述。";
        if (!IsNonStandard && !ProductId.HasValue) return "标准补件必须选择产品。";
        if (DescriptionSnapshot?.Length > 300) return "确认时补发描述不能超过 300 字。";
        if (string.IsNullOrWhiteSpace(UnitSnapshot)) return "补发单位不能为空。";
        if (SpecificationSnapshot?.Length > 300) return "确认时补发规格不能超过 300 字。";
        return null;
    }

    /// <summary>判断新行是否仅含系统默认值；已有行永不被隐式删除。</summary>
    public bool IsEmptyPlaceholder => !ProductId.HasValue && !IsNonStandard && !ColorId.HasValue && !Quantity.HasValue
        && new[] {SkuSnapshot,CustomerSkuSnapshot,LinkSkuSnapshot,DescriptionSnapshot,ColorSnapshot,SpecificationSnapshot,Remark}.All(string.IsNullOrWhiteSpace)
        && string.IsNullOrWhiteSpace(UnitSnapshot);
}
