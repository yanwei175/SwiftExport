namespace SwiftExportClient.Resources.Controls.Filter.Models;

public sealed class ActiveFilterItem
{
    /// <summary>
    /// 筛选器标识，例如 Color、Supplier。
    /// </summary>
    public string FilterKey { get; set; } = string.Empty;

    /// <summary>
    /// 筛选器名称，例如“颜色”。
    /// </summary>
    public string FilterName { get; set; } = string.Empty;

    /// <summary>
    /// 实际筛选值，例如颜色 Uid。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 展示文字，例如“哑光黑”。
    /// </summary>
    public string DisplayText { get; set; } = string.Empty;
}