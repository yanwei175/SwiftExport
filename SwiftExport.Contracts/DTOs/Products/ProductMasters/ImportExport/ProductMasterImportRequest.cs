using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>原子提交完整批次，不支持删除或下发。</summary>
    public class ProductMasterImportRequest
    {
        /// <summary>确认后的完整批次。</summary>
        public List<ProductMasterImportCommitRow> Rows { get; set; } = new List<ProductMasterImportCommitRow>();
    }
}
