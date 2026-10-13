using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExportClient.Services.Caching;

/// <summary>分类继承解析的只读结果；失败与正常空候选区分，不保存任何产品勾选状态。</summary>
public sealed class ProductAttributeResolution(bool success, string message,
    IReadOnlyList<ProductAttributeGroupDto> groups, IReadOnlyList<ProductCategoryAttributeGroupDto> categoryRelations)
{
    /// <summary>分类链与定义快照是否完整，失败时调用方不得据此清空选择。</summary>
    public bool Success { get; } = success;
    /// <summary>解析失败原因或成功说明。</summary>
    public string Message { get; } = message;
    /// <summary>当前分类及全部祖先提供的组，按组身份去重且稳定排序。</summary>
    public IReadOnlyList<ProductAttributeGroupDto> Groups { get; } = groups;
    /// <summary>提供这些组的直接关联，保留来源分类身份。</summary>
    public IReadOnlyList<ProductCategoryAttributeGroupDto> CategoryRelations { get; } = categoryRelations;
}
