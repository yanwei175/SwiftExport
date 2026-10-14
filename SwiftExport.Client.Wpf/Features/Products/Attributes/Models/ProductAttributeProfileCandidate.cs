namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>标签维护页的私有模板候选，中文名和英文代码参与本地搜索，不保存勾选状态。</summary>
public sealed partial class ProductAttributeProfileCandidate(Guid uid, string profileName, string profileCode = "", bool isAvailable = true)
{
    /// <summary>用于匹配直接关联的字典明细身份。</summary>
    public Guid Uid { get; } = uid;
    /// <summary>模板显示名称；缺失候选保留身份并明确提示。</summary>
    public string ProfileName { get; } = profileName;
    /// <summary>模板英文名称或代码，用于本地搜索。</summary>
    public string ProfileCode { get; } = profileCode;
    /// <summary>当前有效模板缓存是否提供此候选，缺失项需刷新或显式解除关联。</summary>
    public bool IsAvailable { get; } = isAvailable;
}
