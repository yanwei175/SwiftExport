using System;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>产品对象已保存选择及实时展示信息；保留失效身份，不作为写入模型。</summary>
    public class ProductAttributeSelectionDto
    {
        /// <summary>选择所属的对象类型。</summary>
        public ProductAttributeOwnerType OwnerType { get; set; }
        /// <summary>选择所属的对象身份。</summary>
        public Guid OwnerId { get; set; }
        /// <summary>已选选项身份，失效时仍保留用于提示。</summary>
        public Guid OptionId { get; set; }
        /// <summary>选项所属组。</summary>
        public Guid AttributeGroupId { get; set; }
        /// <summary>所属对象当前采用的模板；缺失时为空。</summary>
        public Guid? ProductAttributeProfileId { get; set; }
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
        /// <summary>对象模板和关联定义当前是否有效；失效选择不能静默丢弃。</summary>
        public bool IsAvailable { get; set; }
    }
}
