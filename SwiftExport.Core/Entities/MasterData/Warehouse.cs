using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>
/// 仓库实体（对应 dbo.Warehouse）。
/// </summary>
[Table("Warehouse")]
public class Warehouse : BaseEntity
{
    /// <summary>
    /// 仓库代码，例如：V_WH。
    /// </summary>
    public string WhseCode { get; set; } = string.Empty;

    /// <summary>
    /// 仓库名称。
    /// </summary>
    public string WhseName { get; set; } = string.Empty;

    /// <summary>
    /// 是否虚拟仓。
    /// </summary>
    public bool IsVirtual { get; set; }
}
