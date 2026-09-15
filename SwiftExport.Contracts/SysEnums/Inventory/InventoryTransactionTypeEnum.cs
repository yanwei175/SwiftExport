namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存流水类型；已发布值不得重排。</summary>
    public enum InventoryTransactionTypeEnum
    {
        [System.ComponentModel.Description("采购入库")]
        PurchaseReceiptInbound = 10,
        [System.ComponentModel.Description("客供件入库")]
        CustomerSuppliedReceiptInbound = 11,
        [System.ComponentModel.Description("装箱单自动入库")]
        PackingListAutoReceiptInbound = 12,
        [System.ComponentModel.Description("客户发货出库")]
        FinalCustomerShipmentOutbound = 20,
        [System.ComponentModel.Description("客供件消耗出库")]
        CustomerSuppliedConsumptionOutbound = 21,
        /// <summary>普通库存出库，仅允许负向库存变化，不计客户交付。</summary>
        [System.ComponentModel.Description("普通库存出库")]
        StockIssueOutbound = 22,
        [System.ComponentModel.Description("负库存异常出库")]
        NegativeStockExceptionOutbound = 90,
        [System.ComponentModel.Description("期初余额")]
        OpeningBalance = 100,
        [System.ComponentModel.Description("反向流水")]
        Reversal = 900
    }
}
