using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>访问控制名单实体，用于保存允许使用 Excel 功能的主体范围。</summary>
public class AccessControlList : BaseEntity
{
    /// <summary>该实体保存的Excel 功能访问白名单。</summary>
    public string ExcelWhiteList { get; set; } = default!;
}
