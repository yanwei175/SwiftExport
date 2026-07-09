using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

[Table("ProductCategory")]
public class ProductCategory : BaseEntity
{
    public string CategoryName { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public string? FakeName { get; set; }
    public string? CategoryCode { get; set; }
    public int SortNo { get; set; } = default!;
}
