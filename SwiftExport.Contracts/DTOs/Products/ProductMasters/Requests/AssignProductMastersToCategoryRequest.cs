using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>批量设置主型号分类的命令请求。</summary>
    public class AssignProductMastersToCategoryRequest
    {
        /// <summary>获取或设置目标分类标识。</summary>
        public Guid TargetCategoryId { get; set; }

        /// <summary>获取或设置待归类主型号及其预期版本。</summary>
        public List<AssignProductMasterToCategoryItem> Items { get; set; } = new List<AssignProductMasterToCategoryItem>();
    }
}
