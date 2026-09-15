namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>旧入库来源分类，仅供旧 DTO 与历史 SQL 兼容；新入库实体不再使用。</summary>
    public enum InboundSourceTypeEnum
    {
        [System.ComponentModel.Description("采购订单行")]
        PurchaseOrderLine = 10,
        [System.ComponentModel.Description("净需求分配")]
        NetDemandAllocation = 11,
        [System.ComponentModel.Description("装箱单组成")]
        PackingListContribution = 12
    }
}
