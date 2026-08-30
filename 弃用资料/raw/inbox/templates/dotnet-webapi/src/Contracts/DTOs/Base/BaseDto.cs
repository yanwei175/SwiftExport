namespace {{ProjectName}}.Contracts.DTOs.Base;

public abstract class BaseDto
{
    public Guid Uid { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
}
