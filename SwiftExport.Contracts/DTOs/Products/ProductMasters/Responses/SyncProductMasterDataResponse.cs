namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>报告独立下发涉及的主型号数量和实际发生资料变更的产品数量。</summary>
    public class SyncProductMasterDataResponse
    {
        /// <summary>通过校验并参与下发的主型号数。</summary>
        public int ProductMasterCount { get; set; }

        /// <summary>字段值确实变化且已递增版本的产品数。</summary>
        public int UpdatedProductCount { get; set; }
    }
}
