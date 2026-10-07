using System;

namespace SwiftExport.Contracts.DTOs.QualityControl.Categories
{
    /// <summary>售后品质分类可编辑字段；更新版本由批量请求外层提供，不接受审计字段写入。</summary>
    public class AfterSalesQualityCategoryEditDto
    {
        /// <summary>本批新增节点的临时标识，供同批子节点引用；新增结果返回持久化标识。</summary>
        public Guid Uid { get; set; }
        public string CategoryName { get; set; }
        public Guid? ParentId { get; set; }
        public string FakeName { get; set; }
        public string CategoryCode { get; set; }
        public int SortNo { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
