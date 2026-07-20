namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存业务单据生命周期；与过账状态独立。</summary>
    public enum InventoryDocumentLifecycleStatusEnum
    {
        Active = 0,
        Closed = 8,
        Voided = 9
    }

    /// <summary>仓库站点类型；第一阶段只启用供应商虚拟仓。</summary>
    public enum WarehouseSiteTypeEnum
    {
        SupplierVirtual = 0
    }

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
