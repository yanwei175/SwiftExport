using SwiftExport.Contracts.SysEnums.Inventory;
using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>提供库存流水类型的完整只读选项集合。</summary>
    public static class InventoryTransactionTypeList
    {
        /// <summary>获取全部已发布库存流水类型，顺序与业务分组保持一致。</summary>
        public static IReadOnlyList<InventoryTransactionTypeEnum> All { get; } =
            new List<InventoryTransactionTypeEnum>
            {
                InventoryTransactionTypeEnum.PurchaseReceiptInbound,
                InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound,
                InventoryTransactionTypeEnum.PackingListAutoReceiptInbound,
                InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound,
                InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound,
                InventoryTransactionTypeEnum.StockIssueOutbound,
                InventoryTransactionTypeEnum.NegativeStockExceptionOutbound,
                InventoryTransactionTypeEnum.OpeningBalance,
                InventoryTransactionTypeEnum.Reversal
            }.AsReadOnly();
    }
}
