using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>携带原始输入及确认依据，服务端仍需重新匹配。</summary>
    public class ProductMasterImportCommitRow
    {
        /// <summary>原始输入。</summary>
        public ProductMasterImportRowInput Input { get; set; }
        /// <summary>预览身份，新增为空。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>预览版本，新增为 0。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>预览分类身份。</summary>
        public Guid ExpectedCategoryId { get; set; }
        /// <summary>预览供应商身份。</summary>
        public Guid ExpectedSupplierId { get; set; }
    }
}
