using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Systems.ExcelFieldMappings;

/// <summary>保存业务分组内一个 Excel 表头别名到目标字段的对应关系；停用且表头为空时表示待配置字段。</summary>
[Table("ExcelFieldMapping")]
[DbIndex("UX_ExcelFieldMapping_Group_Header", nameof(GroupCode), nameof(NormalizedHeader), IsUnique = true, Filter = "[IsDeleted] = 0 AND [NormalizedHeader] <> N''")]
public sealed class ExcelFieldMapping : BaseEntity
{
    /// <summary>稳定业务分组编码，不随显示名称变化。</summary>
    [DbColumn(StoreType = "nvarchar(40)")]
    public required string GroupCode { get; set; }

    /// <summary>管理员录入的原始表头别名；待配置种子行为空字符串。</summary>
    [DbColumn(StoreType = "nvarchar(200)")]
    public required string ExcelHeader { get; set; }

    /// <summary>统一规范化表头；空值种子行不进入唯一索引或客户端匹配。</summary>
    [DbColumn(StoreType = "nvarchar(200)")]
    public required string NormalizedHeader { get; set; }

    /// <summary>业务分组字段目录中的稳定目标路径。</summary>
    [DbColumn(StoreType = "nvarchar(100)")]
    public required string TargetFieldKey { get; set; }

    /// <summary>停用时保留配置，普通读取默认不返回。</summary>
    [DbColumn(DefaultExpression = "1")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>管理员备注，不参与匹配。</summary>
    [DbColumn(StoreType = "nvarchar(500)")]
    public string? Remark { get; set; }
}
