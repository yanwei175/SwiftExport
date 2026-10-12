using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>返回全批预览，有错误时禁止提交。</summary>
    public class ProductMasterImportPreviewResult
    {
        /// <summary>按原行号排列的预览。</summary>
        public List<ProductMasterImportPreviewRowDto> Rows { get; set; } = new List<ProductMasterImportPreviewRowDto>();
    }
}
