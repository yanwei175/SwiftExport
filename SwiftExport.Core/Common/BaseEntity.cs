
namespace SwiftExport.Core.Common;

public class BaseEntity
{
    public int ID { get; set; } = default!;
    public string? Even { get; set; }
    public string? Status { get; set; }
    public short IsDeleted { get; set; } = 0;
    public DateTime? CreatedTime { get; set; }
    public DateTime? UpdatedTime { get; set; }

}

