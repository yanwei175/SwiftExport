namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存单据生命周期，与过账状态独立。</summary>
    public enum InventoryDocumentLifecycleStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已关闭")]
        Closed = 8,
        [System.ComponentModel.Description("已作废")]
        Voided = 9
    }
}
