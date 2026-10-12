using System.Text.RegularExpressions;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.ProductMasters.Import;

/// <summary>按现有分类路径显示口径构造引用文本，环及断链不伪装为根分类。</summary>
internal static class ProductMasterReferencePaths
{
    /// <summary>规范化分隔符附近空白，保留分类名称内部文字。</summary>
    internal static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s*/\s*", " / ");

    /// <summary>每个路径都由真实祖先构造；失效的链保留为 null，匹配时明确拒绝。</summary>
    internal static IReadOnlyDictionary<Guid, string?> Build(IReadOnlyList<ProductCategory> categories)
    {
        var nodes = categories.Where(item => !item.IsDeleted).ToDictionary(item => item.Uid);
        var result = new Dictionary<Guid, string?>();
        foreach (var category in nodes.Values)
        {
            var parts = new List<string>();
            var seen = new HashSet<Guid>();
            var current = category;
            var valid = true;
            while (true)
            {
                if (!seen.Add(current.Uid) || string.IsNullOrWhiteSpace(current.CategoryName)) { valid = false; break; }
                parts.Add(current.CategoryName.Trim());
                if (current.ParentId is null || current.ParentId == Guid.Empty) break;
                if (!nodes.TryGetValue(current.ParentId.Value, out current!)) { valid = false; break; }
            }
            parts.Reverse();
            result[category.Uid] = valid ? Normalize(string.Join(" / ", parts)) : null;
        }
        return result;
    }
}
