using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductCategory : BaseEntity
{
    public string CategoryName { get; set; } = default!;
    public int? ParentId { get; set; }
    public string? FakeName { get; set; }
    public string? CategoryCode { get; set; }
    public int SortNo { get; set; } = default!;
}
