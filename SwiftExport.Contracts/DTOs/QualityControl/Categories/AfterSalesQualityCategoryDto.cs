using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.QualityControl.Categories
{
    /// <summary>售后品质分类树读取模型，包含停用节点以支持维护和历史显示。</summary>
    public class AfterSalesQualityCategoryDto : BaseDto
    {
        public string CategoryName { get; set; }
        public Guid? ParentId { get; set; }
        public string FakeName { get; set; }
        public string CategoryCode { get; set; }
        public int SortNo { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
