using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>组内有效选项及版本，用于候选展示和定义维护。</summary>
    public class ProductAttributeOptionDto : BaseDto
    {
        /// <summary>所属标签组。</summary>
        public Guid AttributeGroupId { get; set; }
        /// <summary>选项编码。</summary>
        public string OptionCode { get; set; }
        /// <summary>选项名称。</summary>
        public string OptionName { get; set; }
        /// <summary>显示顺序。</summary>
        public int SortOrder { get; set; }
    }
}
