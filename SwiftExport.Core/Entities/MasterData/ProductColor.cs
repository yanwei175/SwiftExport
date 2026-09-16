using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品颜色主数据实体，保存稳定颜色编码及中英文名称。</summary>
[Table("ProductColor")]
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

}
