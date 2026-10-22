namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>库存余额、流水及仓库筛选候选共用的查询输入边界。</summary>
    public static class InventoryQueryLimits
    {
        /// <summary>库存关键词与仓库筛选候选关键词的最大字符数。</summary>
        public const int KeywordMaxLength = 200;

        /// <summary>流水来源单号查询文本的最大字符数。</summary>
        public const int SourceDocumentNoMaxLength = 100;

        /// <summary>库存查询及追溯单页允许返回的最大条数。</summary>
        public const int MaxPageSize = 200;
    }
}
