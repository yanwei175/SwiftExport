namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>主型号详情保存结果，返回服务端接纳后的完整聚合。</summary>
    public class SaveProductMasterDetailResponse
    {
        /// <summary>获取或设置保存后的主型号详情。</summary>
        public ProductMasterDetailDto Detail { get; set; }
    }
}
