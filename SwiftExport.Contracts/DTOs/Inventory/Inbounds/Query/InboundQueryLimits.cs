namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query
{
    /// <summary>入库管理、采购选源及收货仓库候选共用的查询输入边界。</summary>
    public static class InboundQueryLimits
    {
        /// <summary>查询关键词允许的最大字符数。</summary>
        public const int KeywordMaxLength = 200;

        /// <summary>单页允许返回的最大条数。</summary>
        public const int MaxPageSize = 200;
    }
}
