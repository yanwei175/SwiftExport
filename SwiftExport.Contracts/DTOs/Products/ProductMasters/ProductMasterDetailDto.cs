using SwiftExport.Contracts.DTOs.Base;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>主型号详情，包含头部资料和全部有效中性产品。</summary>
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
    }
}
