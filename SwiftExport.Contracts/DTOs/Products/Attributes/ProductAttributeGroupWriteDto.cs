using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>全局标签组的可维护字段；模板适用范围由独立关联维护。</summary>
    public class ProductAttributeGroupWriteDto
    {
        /// <summary>新增组的临时身份，用于同批新增选项及模板关联映射。</summary>
        public Guid ClientUid { get; set; }
        /// <summary>组编码。</summary>
        public string GroupCode { get; set; }
        /// <summary>组名称。</summary>
        public string GroupName { get; set; }
        /// <summary>单选或多选规则。</summary>
        public ProductAttributeSelectionMode SelectionMode { get; set; }
        /// <summary>显示顺序。</summary>
        public int SortOrder { get; set; }
    }
}
