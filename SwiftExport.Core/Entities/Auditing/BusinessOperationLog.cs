using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Auditing;

/// <summary>面向业务人员的持久化操作日志；只追加，不提供更新或删除。</summary>
[Table("business_operation_log")]
public sealed partial class BusinessOperationLog : BaseEntity
{
    /// <summary>产生本条业务审计的调用关联标识，用于关联技术日志，不用于业务幂等。</summary>
    public string CorrelationId { get; set; } = string.Empty;
    /// <summary>关联业务命令；本批迁移命令的原始结果由独立回执保存。</summary>
    public Guid CommandId { get; set; }
    /// <summary>该实体保存的业务模块。</summary>
    public string Module { get; set; } = string.Empty;
    /// <summary>该实体保存的业务聚合类型。</summary>
    public string AggregateType { get; set; } = string.Empty;
    /// <summary>关联的业务聚合唯一标识。</summary>
    public Guid AggregateUid { get; set; }
    /// <summary>单据或业务事实形成时冻结的业务聚合单号历史快照，不随主数据更新。</summary>
    public string? AggregateNoSnapshot { get; set; }
    /// <summary>该实体保存的业务操作类型。</summary>
    public string OperationType { get; set; } = string.Empty;
    /// <summary>该实体保存的业务操作显示名称。</summary>
    public string OperationName { get; set; } = string.Empty;
    /// <summary>该实体保存的业务操作原因。</summary>
    public string? Reason { get; set; }
    /// <summary>该实体保存的业务操作摘要。</summary>
    public string? Summary { get; set; }
    /// <summary>该实体保存的变更字段摘要 JSON。</summary>
    public string? ChangedFieldsJson { get; set; }
    /// <summary>该实体保存的关联业务对象摘要 JSON。</summary>
    public string? RelatedObjectsJson { get; set; }
    /// <summary>关联的操作用户唯一标识。</summary>
    public Guid? UserUid { get; set; }
    /// <summary>单据或业务事实形成时冻结的操作人名称历史快照，不随主数据更新。</summary>
    public string UserNameSnapshot { get; set; } = string.Empty;
    /// <summary>该实体保存的业务事实发生时间。</summary>
    public DateTime OccurredDate { get; set; }
}
