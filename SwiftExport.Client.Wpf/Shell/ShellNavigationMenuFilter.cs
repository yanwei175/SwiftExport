using SwiftExportClient.Navigation;

namespace SwiftExportClient.Shell;

/// <summary>先按统一路由权限裁剪 Shell 菜单，再执行文字筛选，避免搜索泄漏未授权入口。</summary>
public sealed class ShellNavigationMenuFilter(INavigationAccessService accessService)
{
    /// <summary>返回当前用户可见且匹配搜索词的菜单树；没有可见子项的父组会被移除。</summary>
    public IReadOnlyList<ShellNavigationItem> Filter(
        IEnumerable<ShellNavigationItem> items,
        string? searchText)
    {
        var authorizedItems = items
            .Select(FilterAuthorizedItem)
            .Where(item => item is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();

        var query = searchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return authorizedItems;
        }

        return authorizedItems
            .Select(item => FilterSearchItem(item, query))
            .Where(item => item is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();
    }

    private ShellNavigationItem? FilterAuthorizedItem(ShellNavigationItem item)
    {
        if (!item.HasChildren)
        {
            return accessService.CanAccess(item.RouteKey) ? item : null;
        }

        var authorizedChildren = item.Children
            .Select(FilterAuthorizedItem)
            .Where(child => child is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();

        return authorizedChildren.Length == 0
            ? null
            : item.CloneForFilter(authorizedChildren, forceExpanded: false);
    }

    private static ShellNavigationItem? FilterSearchItem(ShellNavigationItem item, string query)
    {
        var selfMatches = item.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matchingChildren = item.Children
            .Select(child => FilterSearchItem(child, query))
            .Where(child => child is not null)
            .Cast<ShellNavigationItem>()
            .ToArray();

        if (selfMatches)
        {
            return item.CloneForFilter(item.Children, forceExpanded: true);
        }

        return matchingChildren.Length == 0
            ? null
            : item.CloneForFilter(matchingChildren, forceExpanded: true);
    }
}
