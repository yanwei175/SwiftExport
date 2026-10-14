using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Services.Caching;

/// <summary>按字典模板直接关联解析标签范围，不依赖分类层级，不包含产品选择状态。</summary>
public static class ProductAttributeProfileResolver
{
    /// <summary>验证模板存在且启用，再返回直接关联组；缺失模板不能解释成清空产品选择。</summary>
    public static ProductAttributeResolution Resolve(Guid profileId, IEnumerable<SysDictDataDto> profiles,
        IEnumerable<ProductAttributeGroupDto> groups, IEnumerable<ProductAttributeProfileGroupDto> relations)
    {
        if (profileId == Guid.Empty) return new(true, "请先选择产品属性模板。", [], []);
        if (!profiles.Any(row => row.Uid == profileId && !row.IsDeleted && row.IsEnabled
                && string.Equals(row.DictCode, DictCodes.ProductAttributeProfile, StringComparison.OrdinalIgnoreCase)))
            return new(false, "产品属性模板不存在或已停用，请刷新字典和标签后重试。", [], []);
        var sources = relations.Where(row => row.ProductAttributeProfileId == profileId)
            .OrderBy(row => row.AttributeGroupId).ThenBy(row => row.Uid).ToArray();
        var groupIds = sources.Select(row => row.AttributeGroupId).ToHashSet();
        var available = groups.Where(row => groupIds.Contains(row.Uid)).OrderBy(row => row.SortOrder).ThenBy(row => row.Uid).ToArray();
        return available.Length == groupIds.Count ? new(true, string.Empty, available, sources)
            : new(false, "属性模板关联的标签组缺失，请刷新标签后重试。", [], []);
    }
}
