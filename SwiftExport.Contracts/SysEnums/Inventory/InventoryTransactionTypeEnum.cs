namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存流水类型；已发布值不得重排。</summary>
    public enum InventoryTransactionTypeEnum
    {
        PurchaseReceiptInbound = 10,
        CustomerSuppliedReceiptInbound = 11,
        PackingListAutoReceiptInbound = 12,
        FinalCustomerShipmentOutbound = 20,
        CustomerSuppliedConsumptionOutbound = 21,
        /// <summary>普通库存出库，仅允许负向库存变化，不计客户交付。</summary>
        StockIssueOutbound = 22,
        NegativeStockExceptionOutbound = 90,
        OpeningBalance = 100,
        Reversal = 900
    }
}
