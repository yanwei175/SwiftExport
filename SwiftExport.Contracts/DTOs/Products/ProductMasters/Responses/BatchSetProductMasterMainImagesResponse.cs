using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>报告已提交的主图更新和跳过原因；批次失败时不返回部分成功。</summary>
    public class BatchSetProductMasterMainImagesResponse
    {
        /// <summary>获取或设置已有默认主图而跳过的数量。</summary>
        public int AlreadySetCount { get; set; }
        /// <summary>获取或设置无有效颜色及非空图片地址候选而跳过的数量。</summary>
        public int NoCandidateCount { get; set; }
        /// <summary>获取或设置实际更新的主型号回执。</summary>
        public List<ProductMasterMainImageResult> Items { get; set; } = new List<ProductMasterMainImageResult>();
    }
}
