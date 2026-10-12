using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>已应用的导出筛选，不含页码或未保存编辑。</summary>
    public class ProductMasterExportRequest
    {
        /// <summary>已应用关键词。</summary>
        public string Keyword { get; set; }
        /// <summary>展开的分类身份。</summary>
        public List<Guid> CategoryIds { get; set; } = new List<Guid>();
        /// <summary>是否包含未分类。</summary>
        public bool IncludeUncategorized { get; set; }
        /// <summary>排除分类，空 Guid 表示无排除。</summary>
        public Guid ExcludeCategoryId { get; set; }
    }
}
