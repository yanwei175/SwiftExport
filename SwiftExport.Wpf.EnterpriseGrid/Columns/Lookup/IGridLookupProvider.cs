namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;

/// <summary>
/// 由宿主 ViewModel/Service 实现的远程 Lookup 提供器。框架不直接调用 API，也不缓存业务字典。
/// </summary>
public interface IGridLookupProvider
{
    /// <summary>按搜索文本异步返回候选项；实现必须尊重取消令牌。</summary>
    ValueTask<IReadOnlyList<object>> SearchAsync(string searchText, CancellationToken cancellationToken);

    /// <summary>按已保存值回填一个显示项；找不到时返回 null。</summary>
    ValueTask<object?> ResolveAsync(object selectedValue, CancellationToken cancellationToken);
}
