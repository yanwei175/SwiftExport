using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>批量设置主型号分类的命令结果。</summary>
    public class AssignProductMastersToCategoryResult
    {
        /// <summary>获取或设置目标分类标识。</summary>
        public Guid TargetCategoryId { get; set; }

        /// <summary>获取或设置成功更新数量。</summary>
        public int UpdatedCount { get; set; }

        /// <summary>获取或设置逐项版本结果。</summary>
        public List<AssignedProductMasterVersionResult> Items { get; set; } = new List<AssignedProductMasterVersionResult>();
    }
}
