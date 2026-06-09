using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductCategory : BaseEntity
{
    public string CategoryName { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public string? FakeName { get; set; }
    public string? CategoryCode { get; set; }
    public int SortNo { get; set; } = default!;
}
