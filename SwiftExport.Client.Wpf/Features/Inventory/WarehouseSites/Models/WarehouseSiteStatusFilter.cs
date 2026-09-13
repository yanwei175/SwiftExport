namespace SwiftExportClient.Features.Inventory.WarehouseSites.Models;

/// <summary>表示虚拟仓列表的活动状态筛选项；空值表示全部状态。</summary>
public sealed class WarehouseSiteStatusFilter
{
    /// <summary>创建一个状态筛选项。</summary>
    public WarehouseSiteStatusFilter(string displayText, bool? value)
    {
        DisplayText = displayText;
        Value = value;
    }

    /// <summary>页面显示文本。</summary>
    public string DisplayText { get; }

    /// <summary>提交到查询契约的可空状态。</summary>
    public bool? Value { get; }
}
