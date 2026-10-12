using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>仅在全批事务提交后返回回执。</summary>
    public class ProductMasterImportResult
    {
        /// <summary>全批最终身份。</summary>
        public List<ProductMasterImportSavedRowDto> Rows { get; set; } = new List<ProductMasterImportSavedRowDto>();
    }
}
