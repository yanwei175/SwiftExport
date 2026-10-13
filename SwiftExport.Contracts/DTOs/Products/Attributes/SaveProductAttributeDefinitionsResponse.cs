using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>定义保存成功后的组、选项、关联回执及全局完整快照。</summary>
    public class SaveProductAttributeDefinitionsResponse
    {
        /// <summary>组逐项保存回执。</summary>
        public BatchSaveResponseBase GroupResults { get; set; } = new BatchSaveResponseBase();
        /// <summary>选项逐项保存回执。</summary>
        public BatchSaveResponseBase OptionResults { get; set; } = new BatchSaveResponseBase();
        /// <summary>分类直接关联逐项保存回执。</summary>
        public BatchSaveResponseBase CategoryResults { get; set; } = new BatchSaveResponseBase();
        /// <summary>提交后的完整定义快照。</summary>
        public ProductAttributeDefinitionsDto Definitions { get; set; } = new ProductAttributeDefinitionsDto();
    }
}
