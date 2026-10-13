using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>维护标签组选项草稿，以组 Uid 关联持久记录或同批新增的临时组。</summary>
public sealed partial class ProductAttributeOptionLineModel : BaseLineModel<ProductAttributeOptionDto>
{
    [ObservableProperty] public partial Guid AttributeGroupId { get; set; }
    [ObservableProperty] public partial string OptionCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string OptionName { get; set; } = string.Empty;
    [ObservableProperty] public partial int SortOrder { get; set; }

    /// <inheritdoc/>
    public override string? ValidateRow() =>
        string.IsNullOrWhiteSpace(OptionCode) || OptionCode.Trim().Length > 100 ? "标签值编码必填且不能超过 100 个字符。" :
        string.IsNullOrWhiteSpace(OptionName) || OptionName.Trim().Length > 200 ? "标签值名称必填且不能超过 200 个字符。" : null;

    /// <inheritdoc/>
    protected override void LoadFromDto(ProductAttributeOptionDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override ProductAttributeOptionDto ToDto() => this.Adapt<ProductAttributeOptionDto>();
}

