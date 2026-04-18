using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

public class AccessControlList : BaseEntity
{
    public string ExcelWhiteList { get; set; } = default!;
}

