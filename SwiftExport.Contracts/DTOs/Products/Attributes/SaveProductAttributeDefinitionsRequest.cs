using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>同一分类的组和选项变更，作为一次原子保存。</summary>
    public class SaveProductAttributeDefinitionsRequest
    {
        /// <summary>组新增、修改、删除及客户端定位信息。</summary>
        public BatchSaveRequestBase<ProductAttributeGroupWriteDto> GroupChanges { get; set; } = new BatchSaveRequestBase<ProductAttributeGroupWriteDto>();
        /// <summary>选项新增、修改、删除及客户端定位信息。</summary>
        public BatchSaveRequestBase<ProductAttributeOptionWriteDto> OptionChanges { get; set; } = new BatchSaveRequestBase<ProductAttributeOptionWriteDto>();
    }
}
