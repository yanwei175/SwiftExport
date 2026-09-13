namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存单据生命周期，与过账状态独立。</summary>
    public enum InventoryDocumentLifecycleStatusEnum
    {
        Active = 0,
        Closed = 8,
        Voided = 9
    }
}

