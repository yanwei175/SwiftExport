using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>表达按原行号及稳定字段键定位的问题。</summary>
    public class ProductMasterImportIssueDto
    {
        /// <summary>原文件行号。</summary>
        public int SourceRowNumber { get; set; }
        /// <summary>输入属性的稳定字段键。</summary>
        public string FieldKey { get; set; }
        /// <summary>稳定错误分类。</summary>
        public string Code { get; set; }
        /// <summary>不含原始敏感值的原因。</summary>
        public string Message { get; set; }
    }
}
