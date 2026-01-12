using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductColor : BaseEntity
{
    public string ColorCodeUnique { get; set; } = default!;
    public string ColorCode { get; set; } = default!;
    public string? ColorNameEn { get; set; }
    public string? ColorNameZh { get; set; }

}
