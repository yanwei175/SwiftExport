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
        [System.ComponentModel.Description("供应商虚拟仓")]
        SupplierVirtual = 0
    }

    /// <summary>入库业务类型；数值已冻结，不得重排。</summary>
    public enum InboundDocumentTypeEnum
    {
        [System.ComponentModel.Description("采购收货")]
        PurchaseReceipt = 10,

        [System.ComponentModel.Description("客供件收货")]
        CustomerSuppliedReceipt = 11,

        [System.ComponentModel.Description("装箱单自动")]
        PackingListAutoReceipt = 12,

        [System.ComponentModel.Description("期初余额")]
        OpeningBalance = 100
    }

    /// <summary>入库单过账状态；生命周期状态由单据实体独立维护。</summary>
    public enum InboundPostingStatusEnum
    {
        [System.ComponentModel.Description("未过账")]
        Unposted = 0,
        [System.ComponentModel.Description("已过账")]
        Posted = 1,
        [System.ComponentModel.Description("反过账")]
        Reversed = 8
    }

    public enum InboundSourceTypeEnum
    {
        PurchaseOrderLine = 10,
        NetDemandAllocation = 11,
        PackingListContribution = 12
    }
}
