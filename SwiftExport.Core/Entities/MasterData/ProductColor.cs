using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品颜色主数据实体，保存稳定颜色编码、中英文名称及全局显示顺序。</summary>
[Table("product_color")]
[DbCheck("CK_ProductColor_SortNo", "\"sort_no\" >= 0")]
public partial class ProductColor : BaseEntity
{
    /// <summary>该实体保存的颜色唯一编码。</summary>
    public string ColorCodeUnique { get; set; } = default!;
    /// <summary>该实体保存的颜色业务编码。</summary>
    public string ColorCode { get; set; } = default!;
    /// <summary>该实体保存的颜色英文名称。</summary>
    public string? ColorNameEn { get; set; }
    /// <summary>该实体保存的颜色中文名称。</summary>
    public string? ColorNameZh { get; set; }

    /// <summary>全局显示排序号，数值越小越靠前；允许零和重复值。</summary>
    [DbColumn(DefaultExpression = "0")]
    public int SortNo { get; set; }

}
