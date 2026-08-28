namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>
/// 表示采购工作台状态多选框中的一个枚举选项，并提供稳定的业务值和中文显示文本。
/// </summary>
public sealed class PurchaseOrderFilterOption<TValue>(TValue value, string displayText)
    where TValue : struct, Enum
{
    /// <summary>获取提交到采购查询契约的枚举值。</summary>
    public TValue Value { get; } = value;

    /// <summary>获取界面显示文本。</summary>
    public string DisplayText { get; } = displayText;

    /// <summary>返回界面显示文本，供控件回退显示使用。</summary>
    public override string ToString() => DisplayText;
}
