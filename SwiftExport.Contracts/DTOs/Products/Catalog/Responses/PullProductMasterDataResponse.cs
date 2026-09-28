namespace SwiftExport.Contracts.DTOs.Products.Responses
{
    /// <summary>报告所选产品、无可用主型号而跳过的产品及实际更新数量。</summary>
    public class PullProductMasterDataResponse
    {
        /// <summary>本次请求中去重后的产品数。</summary>
        public int SelectedProductCount { get; set; }

        /// <summary>未关联或关联主型号不存在、已删除的产品数。</summary>
        public int SkippedProductCount { get; set; }

        /// <summary>字段值变化并已递增版本的产品数。</summary>
        public int UpdatedProductCount { get; set; }
    }
}
