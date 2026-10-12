using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>表达匹配及拟保存资料，不具有提交授权。</summary>
    public class ProductMasterImportPreviewRowDto
    {
        /// <summary>原文件行号。</summary>
        public int SourceRowNumber { get; set; }
        /// <summary>Create、Update、Unchanged 或 Invalid。</summary>
        public string Operation { get; set; }
        /// <summary>已有身份，新增为空。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>预览版本，新增为 0。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>已有资料，新增为空。</summary>
        public ProductMasterTransferData CurrentData { get; set; }
        /// <summary>空白合并和引用解析后的拟保存资料。</summary>
        public ProductMasterTransferData ProposedData { get; set; }
        /// <summary>原分类完整路径，仅供差异展示。</summary>
        public string CurrentCategoryPath { get; set; }
        /// <summary>拟保存分类完整路径，仅供差异展示。</summary>
        public string ProposedCategoryPath { get; set; }
        /// <summary>原供应商编码，仅供差异展示。</summary>
        public string CurrentSupplierCode { get; set; }
        /// <summary>拟保存供应商编码，仅供差异展示。</summary>
        public string ProposedSupplierCode { get; set; }
        /// <summary>本行问题。</summary>
        public List<ProductMasterImportIssueDto> Issues { get; set; } = new List<ProductMasterImportIssueDto>();
    }
}
