using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>只读匹配整个批次，不保存文件或业务资料。</summary>
    public class ProductMasterImportPreviewRequest
    {
        /// <summary>完整输入批次。</summary>
        public List<ProductMasterImportRowInput> Rows { get; set; } = new List<ProductMasterImportRowInput>();
    }
}
