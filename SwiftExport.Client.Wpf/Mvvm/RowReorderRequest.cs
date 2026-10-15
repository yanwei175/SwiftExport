using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Mvvm;

/// <summary>传递源行及目标锚点，不暴露 Gong 类型或依赖虚拟化容器索引。</summary>
public sealed record RowReorderRequest(object SourceItem, object? TargetItem, RowDropPosition Position)
{
    /// <summary>为平面列表解析移除源行后的最终索引，拒绝过期对象、非法落点和原地移动。</summary>
    public bool TryResolve<T>(IReadOnlyList<T> items, out T line, out int finalIndex) where T : class
    {
        line = null!;
        finalIndex = -1;
        if (SourceItem is not T source) return false;
        var oldIndex = IndexOf(items, source);
        if (oldIndex < 0) return false;
        int slot;
        if (Position == RowDropPosition.End && TargetItem is null) slot = items.Count;
        else
        {
            if (TargetItem is not T target || Position is not (RowDropPosition.Before or RowDropPosition.After)) return false;
            slot = IndexOf(items, target);
            if (slot < 0) return false;
            if (Position == RowDropPosition.After) slot++;
        }
        finalIndex = slot > oldIndex ? slot - 1 : slot;
        line = source;
        return finalIndex >= 0 && finalIndex < items.Count && finalIndex != oldIndex;
    }

    /// <summary>判断视图是否保持业务集合顺序；只有拥有分组重排规则的宿主才能显式允许分组。</summary>
    public static bool IsViewInSourceOrder(ICollectionView view, bool allowBusinessGrouping = false) => view.Filter is null
        && view.SortDescriptions.Count == 0 && (allowBusinessGrouping || view.GroupDescriptions.Count == 0)
        && view is not ListCollectionView { CustomSort: not null };

    /// <summary>仅按当前模型对象定位，不能把相同 Uid 的旧快照误认为当前编辑行。</summary>
    private static int IndexOf<T>(IReadOnlyList<T> items, T item) where T : class
    {
        for (var i = 0; i < items.Count; i++) if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }
}
