using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Files.ProductImages
{
    /// <summary>描述一次已正常解析完成、允许逐项部分成功的产品图片批次结果。</summary>
    public class ProductImageBatchUploadResultDto
    {
        /// <summary>获取或设置批次内已接收的文件项目总数。</summary>
        public int Total { get; set; }

        /// <summary>获取或设置完成三份发布的项目数。</summary>
        public int Succeeded { get; set; }

        /// <summary>获取或设置逐项失败的项目数。</summary>
        public int Failed { get; set; }

        /// <summary>获取或设置严格按输入顺序排列的逐项结果。</summary>
        public List<ProductImageUploadItemResultDto> Items { get; set; } = new List<ProductImageUploadItemResultDto>();
    }
}
