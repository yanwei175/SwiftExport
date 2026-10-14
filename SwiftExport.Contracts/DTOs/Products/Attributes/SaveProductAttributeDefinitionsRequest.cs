using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>全局组、选项和模板关联变更，作为一次原子保存。</summary>
    public class SaveProductAttributeDefinitionsRequest
    {
        /// <summary>保存协议版本；当前客户端明确提交 2，省略时为 0 并拒绝写入。</summary>
        public int ContractVersion { get; set; }
        /// <summary>组新增、修改、删除；修改子选项或关联时也需提交既有组版本。</summary>
        public BatchSaveRequestBase<ProductAttributeGroupWriteDto> GroupChanges { get; set; } = new BatchSaveRequestBase<ProductAttributeGroupWriteDto>();
        /// <summary>选项新增、修改、删除及客户端定位信息。</summary>
        public BatchSaveRequestBase<ProductAttributeOptionWriteDto> OptionChanges { get; set; } = new BatchSaveRequestBase<ProductAttributeOptionWriteDto>();
        /// <summary>模板直接关联的新增和删除；不接受修改身份。</summary>
        public BatchSaveRequestBase<ProductAttributeProfileGroupWriteDto> ProfileChanges { get; set; } = new BatchSaveRequestBase<ProductAttributeProfileGroupWriteDto>();
    }
}
