using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>主型号已保存选择及实时展示信息；不作为写入模型。</summary>
    public class ProductMasterAttributeSelectionDto
    {
        /// <summary>所属主型号身份。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>已选选项身份，失效时仍保留用于提示。</summary>
        public Guid OptionId { get; set; }
        /// <summary>选项所属组。</summary>
        public Guid AttributeGroupId { get; set; }
        /// <summary>选项所属分类。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>当前组名称。</summary>
        public string GroupName { get; set; }
        /// <summary>当前选项名称。</summary>
        public string OptionName { get; set; }
        /// <summary>当前组的选择规则。</summary>
        public ProductAttributeSelectionMode SelectionMode { get; set; }
        /// <summary>组显示顺序。</summary>
        public int GroupSortOrder { get; set; }
        /// <summary>选项显示顺序。</summary>
        public int OptionSortOrder { get; set; }
        /// <summary>关联定义当前是否有效；失效选择不能静默丢弃。</summary>
        public bool IsAvailable { get; set; }
    }
}
