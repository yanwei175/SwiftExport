namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>定义前后端共享的供应商虚拟仓输入边界，并与当前权威数据库列宽保持一致。</summary>
    public static class WarehouseSiteLimits
    {
        /// <summary>停用或删除原因最大字符数，对应 BusinessOperationLog.Reason nvarchar(1000)。</summary>
        public const int ReasonMaxLength = 1000;
    }
}
