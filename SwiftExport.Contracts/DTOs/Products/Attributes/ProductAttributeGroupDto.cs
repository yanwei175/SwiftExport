using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>标签组及其完整有效选项，用于只读定义快照。</summary>
    public class ProductAttributeGroupDto : BaseDto
    {
        /// <summary>所属分类。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>组编码。</summary>
        public string GroupCode { get; set; }
        /// <summary>组名称。</summary>
        public string GroupName { get; set; }
        /// <summary>单选或多选规则。</summary>
        public ProductAttributeSelectionMode SelectionMode { get; set; }
        /// <summary>显示顺序。</summary>
        public int SortOrder { get; set; }
        /// <summary>全部有效选项；没有选项时返回空集合。</summary>
        public List<ProductAttributeOptionDto> Options { get; set; } = new List<ProductAttributeOptionDto>();
    }
}
