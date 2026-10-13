namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>标签维护页的私有分类候选快照，完整路径用于展示和本地搜索，不含共享缓存勾选状态。</summary>
public sealed partial class ProductAttributeCategoryCandidate(Guid uid, string categoryPath, bool isAvailable = true)
{
    /// <summary>用于匹配直接关联的分类身份。</summary>
    public Guid Uid { get; } = uid;
    /// <summary>包含祖先的显示路径；缺失候选保留身份并明确提示，不静默移除。</summary>
    public string CategoryPath { get; } = categoryPath;
    /// <summary>当前分类缓存是否提供此候选，缺失项需刷新或显式解除关联。</summary>
    public bool IsAvailable { get; } = isAvailable;
}
