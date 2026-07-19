using SwiftExport.Core.Common;


namespace SwiftExport.Core.Entities.Auth;

/// <summary>Excel 导入映射配置实体，定义适用范围以及源字段到目标字段的对应关系。</summary>
public class ExcelImportConfig : BaseEntity
{
    /// <summary>该实体保存的导入配置适用范围。</summary>
    public string EffectiveScope { get; set; } = default!;
    /// <summary>该实体保存的Excel 源字段集合。</summary>
    public string SourceFields { get; set; } = default!;
    /// <summary>该实体保存的系统目标字段集合。</summary>
    public string TargetFields { get; set; } = default!;
}
