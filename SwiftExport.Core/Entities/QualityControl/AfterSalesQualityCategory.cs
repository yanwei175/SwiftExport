using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.QualityControl;

/// <summary>售后品质分类，独立保存层级、显示名称、别名和活动资格；父级与末级均可被售后单引用。</summary>
[Table("after_sales_quality_category")]
[DbIndex("UX_AfterSalesQualityCategory_CategoryCode", nameof(CategoryCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"category_code\" IS NOT NULL")]
[DbIndex("IX_AfterSalesQualityCategory_ParentId", nameof(ParentId))]
[DbCheck("CK_AfterSalesQualityCategory_SortNo", "\"sort_no\" >= 0")]
[LogicalReference(nameof(ParentId), typeof(AfterSalesQualityCategory))]
public sealed class AfterSalesQualityCategory : BaseEntity
{
    /// <summary>分类显示名称。</summary>
    public string CategoryName { get; set; } = string.Empty;
    /// <summary>上级分类身份；空值表示根节点。</summary>
    public Guid? ParentId { get; set; }
    /// <summary>分类别名，迁移时保留原英文名称。</summary>
    public string? FakeName { get; set; }
    /// <summary>可空业务编码；旧字典英文名称不作为编码迁入。</summary>
    public string? CategoryCode { get; set; }
    /// <summary>非负显示排序号。</summary>
    public int SortNo { get; set; }
    /// <summary>是否允许新增引用；停用不抹除既有单据的历史显示。</summary>
    [DbColumn(DefaultExpression = "TRUE")]
    public bool IsEnabled { get; set; } = true;
}
