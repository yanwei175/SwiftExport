using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>中性产品详情项，包含并发字段和颜色展示名称。</summary>
    public class ProductNeutralDto : BaseDto
    {
        /// <summary>获取或设置所属主型号标识。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>获取或设置可写中性产品数据。</summary>
        public ProductNeutralWriteDto Data { get; set; } = new ProductNeutralWriteDto();
        /// <summary>获取或设置实时颜色名称。</summary>
        public string ColorName { get; set; }
    }
}
