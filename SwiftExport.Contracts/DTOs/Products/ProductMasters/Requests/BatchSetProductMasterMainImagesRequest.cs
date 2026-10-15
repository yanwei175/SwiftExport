using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>只为指定主型号补齐默认主图，已有默认值保持不变。</summary>
    public class BatchSetProductMasterMainImagesRequest
    {
        /// <summary>获取或设置勾选的主型号及预期版本，单次最多 200 项。</summary>
        public List<ProductMasterMainImageTarget> Items { get; set; } = new List<ProductMasterMainImageTarget>();
    }
}
