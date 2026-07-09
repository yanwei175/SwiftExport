using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

[Table("ProductColor")]
public class ProductColor : BaseEntity
{
    public string ColorCodeUnique { get; set; } = default!;
    public string ColorCode { get; set; } = default!;
    public string? ColorNameEn { get; set; }
    public string? ColorNameZh { get; set; }

}
