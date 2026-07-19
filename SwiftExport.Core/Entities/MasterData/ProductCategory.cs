using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品分类实体，保存产品分类的层级、编码和显示顺序。</summary>
[Table("ProductCategory")]
public class ProductCategory : BaseEntity
{
    /// <summary>该实体保存的分类名称。</summary>
    public string CategoryName { get; set; } = default!;
    /// <summary>关联的上级记录唯一标识。</summary>
    public Guid? ParentId { get; set; }
    /// <summary>该实体保存的分类显示别名。</summary>
    public string? FakeName { get; set; }
    /// <summary>该实体保存的分类编码。</summary>
    public string? CategoryCode { get; set; }
    /// <summary>该实体保存的分类显示顺序。</summary>
    public int SortNo { get; set; } = default!;
}
