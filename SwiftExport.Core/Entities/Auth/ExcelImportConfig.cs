using SwiftExport.Core.Common;


namespace SwiftExport.Core.Entities.Auth;

public class ExcelImportConfig : BaseEntity
{
    public string EffectiveScope { get; set; } = default!;
    public string SourceFields { get; set; } = default!;
    public string TargetFields { get; set; } = default!;
}

