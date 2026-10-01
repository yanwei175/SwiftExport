using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Systems.ExcelReadProfiles;

/// <summary>保存配置内一个目标字段的工作表、起始单元格和读取方式；只能随主配置事务维护。</summary>
[Table("ExcelReadField")]
[LogicalReference(nameof(ProfileUid), typeof(ExcelReadProfile), IsRequired = true)]
[DbIndex("UX_ExcelReadField_Profile_Key", nameof(ProfileUid), nameof(TargetFieldKey), IsUnique = true, Filter = "[IsDeleted] = 0")]
public sealed class ExcelReadField : BaseEntity
{
    /// <summary>所属主配置的逻辑主键。</summary>
    public Guid ProfileUid { get; set; }

    /// <summary>客户端认可的稳定字段路径；服务端仅校验格式，不绑定 ViewModel 类型。</summary>
    [DbColumn(StoreType = "nvarchar(100)")]
    public required string TargetFieldKey { get; set; }

    /// <summary>指定位置时的可选工作表名称，仅供参考；客户端实际读取使用本次捕获的活动工作表。</summary>
    [DbColumn(StoreType = "nvarchar(31)")]
    public string? WorksheetName { get; set; }

    /// <summary>规范化的单个 A1 地址；向下读取时代表该列的首条明细位置。</summary>
    [DbColumn(StoreType = "nvarchar(10)")]
    public required string CellAddress { get; set; }

    /// <summary>是否从起始单元格逐行向下读取；同一配置的向下字段必须共用起始行，名称不决定读取源。</summary>
    [DbColumn(DefaultExpression = "0")]
    public bool IsDownward { get; set; }
}

