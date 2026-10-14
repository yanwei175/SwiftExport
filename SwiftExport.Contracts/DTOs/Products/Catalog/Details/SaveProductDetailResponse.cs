
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>成功提交后的完整物料详情回执。</summary>
    public class SaveProductDetailResponse
    {
        /// <summary>服务端接纳后的详情快照。</summary>
        public ProductDetailDto Detail { get; set; } = new ProductDetailDto();
    }
}
