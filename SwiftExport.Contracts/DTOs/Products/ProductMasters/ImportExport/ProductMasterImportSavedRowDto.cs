using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>表示提交后的身份和版本，无变化行不递增版本。</summary>
    public class ProductMasterImportSavedRowDto
    {
        /// <summary>原文件行号。</summary>
        public int SourceRowNumber { get; set; }
        /// <summary>保存后身份。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>保存后版本。</summary>
        public int Version { get; set; }
        /// <summary>Create、Update 或 Unchanged。</summary>
        public string Operation { get; set; }
    }
}
