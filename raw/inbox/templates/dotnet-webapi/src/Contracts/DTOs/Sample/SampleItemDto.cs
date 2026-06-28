using {{ProjectName}}.Contracts.DTOs.Base;

namespace {{ProjectName}}.Contracts.DTOs.Sample;

public class SampleItemDto : BaseDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
