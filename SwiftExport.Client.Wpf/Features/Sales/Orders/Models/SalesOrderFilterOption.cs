namespace SwiftExportClient.Features.Sales.Orders.Models;

public sealed class SalesOrderFilterOption<TValue>(TValue value, string displayText)
    where TValue : struct, Enum
{
    public TValue Value { get; } = value;

    public string DisplayText { get; } = displayText;

    public override string ToString() => DisplayText;
}
