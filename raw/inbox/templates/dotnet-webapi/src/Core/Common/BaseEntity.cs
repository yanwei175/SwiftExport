using {{ProjectName}}.Contracts.Common;

namespace {{ProjectName}}.Core.Common;

public abstract class BaseEntity
{
    public Guid Uid { get; set; } = GuidSequentialGenerator.NewGuid();
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
}
