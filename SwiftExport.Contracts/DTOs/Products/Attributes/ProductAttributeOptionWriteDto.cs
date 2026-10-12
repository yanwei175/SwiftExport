using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>标签选项的可维护字段；已有选项禁止改变所属组。</summary>
    public class ProductAttributeOptionWriteDto
    {
        /// <summary>已有组身份或本批新增组 ClientUid。</summary>
        public Guid AttributeGroupId { get; set; }
        /// <summary>选项编码。</summary>
        public string OptionCode { get; set; }
        /// <summary>选项名称。</summary>
        public string OptionName { get; set; }
        /// <summary>显示顺序。</summary>
        public int SortOrder { get; set; }
    }
}
