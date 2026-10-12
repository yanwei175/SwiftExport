using SwiftExport.Contracts.DTOs.Base;
using System.Collections.Generic;
using System;
using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>主型号详情，包含头部、有效中性产品以及已保存标签身份和展示信息。</summary>
    public class ProductMasterDetailDto : BaseDto
    {
        /// <summary>获取或设置主型号可写数据。</summary>
        public ProductMasterWriteDto Master { get; set; } = new ProductMasterWriteDto();
        /// <summary>获取或设置实时分类名称。</summary>
        public string CategoryName { get; set; }
        /// <summary>获取或设置实时默认供应商名称。</summary>
        public string DefaultSupplierName { get; set; }
        /// <summary>获取或设置中性产品明细。</summary>
        public List<ProductNeutralDto> Neutrals { get; set; } = new List<ProductNeutralDto>();
        /// <summary>当前保存的标签选项身份；失效引用仍保留，供详情编辑明确处理。</summary>
        public List<Guid> SelectedOptionIds { get; set; } = new List<Guid>();
        /// <summary>当前标签关系的实时名称、排序和有效状态。</summary>
        public List<ProductMasterAttributeSelectionDto> AttributeSelections { get; set; } = new List<ProductMasterAttributeSelectionDto>();
    }
}
