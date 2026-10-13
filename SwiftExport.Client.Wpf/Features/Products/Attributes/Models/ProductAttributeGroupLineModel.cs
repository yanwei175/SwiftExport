using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>维护全局共享标签组草稿；分类适用范围由独立关联草稿维护。</summary>
public sealed partial class ProductAttributeGroupLineModel : BaseLineModel<ProductAttributeGroupDto>
{
    [ObservableProperty] public partial string GroupCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string GroupName { get; set; } = string.Empty;
    [ObservableProperty] public partial ProductAttributeSelectionMode SelectionMode { get; set; }
    [ObservableProperty] public partial int SortOrder { get; set; }

    /// <inheritdoc/>
    public override string? ValidateRow() =>
        string.IsNullOrWhiteSpace(GroupCode) || GroupCode.Trim().Length > 100 ? "标签组编码必填且不能超过 100 个字符。" :
        string.IsNullOrWhiteSpace(GroupName) || GroupName.Trim().Length > 200 ? "标签组名称必填且不能超过 200 个字符。" :
        !Enum.IsDefined(SelectionMode) ? "请选择单选或多选。" : null;

    /// <inheritdoc/>
    protected override void LoadFromDto(ProductAttributeGroupDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override ProductAttributeGroupDto ToDto() => this.Adapt<ProductAttributeGroupDto>();
}

