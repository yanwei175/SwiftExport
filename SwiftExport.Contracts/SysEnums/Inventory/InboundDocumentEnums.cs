namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>入库业务类型；数值已冻结，不得重排。</summary>
    public enum InboundDocumentTypeEnum
    {
        PurchaseReceipt = 10,
        CustomerSuppliedReceipt = 11,
        PackingListAutoReceipt = 12,
        OpeningBalance = 100
    }

    /// <summary>入库单过账状态；生命周期状态由单据实体独立维护。</summary>
    public enum InboundPostingStatusEnum
    {
        Unposted = 0,
        Posted = 1,
        Reversed = 8
    }

    public enum InboundSourceTypeEnum
    {
        PurchaseOrderLine = 10,
        NetDemandAllocation = 11,
        PackingListContribution = 12
    }
}
