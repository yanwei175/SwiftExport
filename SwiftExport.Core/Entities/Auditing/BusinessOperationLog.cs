using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Auditing;

/// <summary>面向业务人员的持久化操作日志；只追加，不提供更新或删除。</summary>
[Table("BusinessOperationLog")]
public sealed class BusinessOperationLog : BaseEntity
{
    public string CorrelationId { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateUid { get; set; }
    public string? AggregateNoSnapshot { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Summary { get; set; }
    public string? ChangedFieldsJson { get; set; }
    public string? RelatedObjectsJson { get; set; }
    public Guid? UserUid { get; set; }
    public string UserNameSnapshot { get; set; } = string.Empty;
    public DateTime OccurredDate { get; set; }
}
