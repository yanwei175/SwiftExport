namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>旧入库来源分类，仅供旧 DTO 与历史 SQL 兼容；新入库实体不再使用。</summary>
    public enum InboundSourceTypeEnum
    {
        PurchaseOrderLine = 10,
        NetDemandAllocation = 11,
        PackingListContribution = 12
    }
}

