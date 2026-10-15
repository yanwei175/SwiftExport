using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>将既有逐行重排验收转换为真实目标锚点，边界请求仍由业务命令拒绝。</summary>
internal static class RowReorderTestRequests
{
    /// <summary>构造相邻行请求；首尾没有相邻行时使用原地落点检验禁用状态。</summary>
    public static RowReorderRequest Step<T>(IReadOnlyList<T> items, T line, int offset) where T : class
    {
        var index = Enumerable.Range(0, items.Count).First(i => ReferenceEquals(items[i], line));
        var target = index + offset;
        return new(line, target >= 0 && target < items.Count ? items[target] : line,
            offset < 0 ? RowDropPosition.Before : RowDropPosition.After);
    }
}
