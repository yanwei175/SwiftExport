using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>父级可写资料与可重新导入的引用文本，不含中性价格或主图。</summary>
    public class ProductMasterExportRowDto
    {
        /// <summary>已保存父级资料。</summary>
        public ProductMasterTransferData Data { get; set; }
        /// <summary>完整路径，未分类为空。</summary>
        public string CategoryPath { get; set; }
        /// <summary>供应商编码，未设置为空。</summary>
        public string DefaultSupplierCode { get; set; }
    }
}
