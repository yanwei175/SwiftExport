using System.ComponentModel.DataAnnotations.Schema;
using {{ProjectName}}.Core.Common;

namespace {{ProjectName}}.Core.Entities.Sample;

[Table("SampleItem")]
public class SampleItem : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
