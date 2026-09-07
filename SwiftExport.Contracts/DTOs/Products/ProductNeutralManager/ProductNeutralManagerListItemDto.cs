using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products.ProductNeutralManager
{
    /// <summary>中性产品管理页列表投影，组合可写数据和主型号、分类、颜色的实时名称。</summary>
    public class ProductNeutralManagerListItemDto : BaseDto
    {
        /// <summary>获取或设置可写中性产品数据。</summary>
        public ProductNeutralManagerWriteDto Data { get; set; } = new ProductNeutralManagerWriteDto();
        /// <summary>获取或设置所属主型号编码。</summary>
        public string MasterSku { get; set; }
        /// <summary>获取或设置所属主型号中文描述。</summary>
        public string ProductMasterDescriptionZh { get; set; }
        /// <summary>获取或设置所属主型号分类标识。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>获取或设置所属主型号分类名称。</summary>
        public string CategoryName { get; set; }
        /// <summary>获取或设置实时颜色名称。</summary>
        public string ColorName { get; set; }
    }
}
