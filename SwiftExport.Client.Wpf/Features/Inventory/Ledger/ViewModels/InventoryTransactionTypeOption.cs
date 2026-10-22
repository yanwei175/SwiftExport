using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExportClient.Features.Inventory.Ledger.ViewModels;

/// <summary>为流水筛选提供非空候选对象，空枚举值表示全部，避免收起下拉时丢失显示内容。</summary>
public sealed partial class InventoryTransactionTypeOption(InventoryTransactionTypeEnum? value)
{
    /// <summary>查询使用的交易类型；空值表示不限制类型。</summary>
    public InventoryTransactionTypeEnum? Value { get; } = value;
}
