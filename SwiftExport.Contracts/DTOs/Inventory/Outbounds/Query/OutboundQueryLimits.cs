namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>出库列表和库存选品共享的查询输入上限，供客户端与服务端一致校验。</summary>
    public static class OutboundQueryLimits
    {
        /// <summary>查询关键词最大字符数。</summary>
        public const int KeywordMaxLength = 200;

        /// <summary>单次查询允许的最大页大小。</summary>
        public const int PageSizeMax = 200;

        /// <summary>库存选品允许排除的最大产品数量。</summary>
        public const int ExcludedProductIdsMax = 500;
    }
}
