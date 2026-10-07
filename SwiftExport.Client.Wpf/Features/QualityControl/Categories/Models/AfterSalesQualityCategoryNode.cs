using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExportClient.Features.QualityControl.Categories.Mappings;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.QualityControl.Categories.Models;

/// <summary>分类编辑节点；持久化变更由行模型跟踪，展开和子节点只属于界面状态。</summary>
public sealed partial class AfterSalesQualityCategoryNode : BaseLineModel<AfterSalesQualityCategoryDto>
{
    [ObservableProperty] public partial Guid? ParentId { get; set; }
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string FakeName { get; set; } = string.Empty;
    [ObservableProperty] public partial string CategoryCode { get; set; } = string.Empty;
    [ObservableProperty] public partial int SortNo { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnabledText))]
    public partial bool IsEnabled { get; set; } = true;
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;

    /// <summary>当前树视图的直属子节点，不参与提交和脏状态判断。</summary>
    public ObservableCollection<AfterSalesQualityCategoryNode> Children { get; } = [];
    /// <summary>提供不依赖颜色的启用状态。</summary>
    public string EnabledText => IsEnabled ? "启用" : "停用";

    /// <inheritdoc/>
    protected override void LoadFromDto(AfterSalesQualityCategoryDto dto) => dto.Adapt(this, AfterSalesQualityCategoryMappings.Config);
    /// <inheritdoc/>
    public override AfterSalesQualityCategoryDto ToDto() => this.Adapt<AfterSalesQualityCategoryDto>(AfterSalesQualityCategoryMappings.Config);
    /// <summary>只输出允许编辑的字段，版本在批量更新外层提供。</summary>
    public AfterSalesQualityCategoryEditDto ToEditDto() => this.Adapt<AfterSalesQualityCategoryEditDto>(AfterSalesQualityCategoryMappings.Config);
    /// <inheritdoc/>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) => base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(IsExpanded) or nameof(Children) or nameof(EnabledText);
    /// <inheritdoc/>
    public override string? ValidateRow() => string.IsNullOrWhiteSpace(CategoryName)
        ? "分类名称不能为空。" : SortNo < 0 ? "排序不能为负数。" : null;
}
