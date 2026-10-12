using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Systems.SystemParameters;

/// <summary>保存全局系统参数值及版本；键由代码注册，不提供删除或更名入口。</summary>
[Table("system_parameter")]
[DbIndex("UX_SystemParameter_Key", nameof(ParameterKey), IsUnique = true)]
[DbCheck("CK_SystemParameter_Value", "length(btrim(\"parameter_value\")) > 0")]
public sealed class SystemParameter : BaseEntity
{
    /// <summary>不可变参数键，唯一约束同时防止首次配置并发覆盖。</summary>
    [DbColumn(StoreType = "varchar(100)")]
    public required string ParameterKey { get; set; }

    /// <summary>经过应用层校验的非空参数值。</summary>
    [DbColumn(StoreType = "varchar(2048)")]
    public required string ParameterValue { get; set; }
}
