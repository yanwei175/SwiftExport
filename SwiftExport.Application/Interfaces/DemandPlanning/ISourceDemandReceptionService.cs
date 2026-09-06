namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>接收来源领域认可的实物需求，并在调用方事务内维护来源需求一致性。</summary>
public interface ISourceDemandReceptionService
{
    /// <summary>幂等接收或更新明确提交的来源需求行；未包含的历史行不会被隐式终止。</summary>
    Task<SourceDemandReceptionResult> ReceiveAsync(SourceDemandReceptionCommand command);
    /// <summary>按既有历史安全规则退出来源确认，并返回删除与作废数量。</summary>
    Task<SourceDemandExitResult> CancelConfirmationAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason);
    /// <summary>关闭尚未进入下游执行的活动来源需求。</summary>
    Task<int> ClosePendingAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason);
    /// <summary>判断来源是否仍存在活动需求，用于保护来源草稿删除。</summary>
    Task<bool> HasActiveAsync(string sourceType, Guid sourceUid);
}

/// <summary>来源需求接收命令；SourceVersion 是来源单据头的事实版本。</summary>
public sealed class SourceDemandReceptionCommand
{
    public Guid CommandId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public string SourceNo { get; set; } = string.Empty;
    public int SourceVersion { get; set; }
    public Guid CustomerId { get; set; }
    /// <summary>要求当前活动需求与本次提交行完全一致；销售确认等全量事实提交使用此保护。</summary>
    public bool RejectUnsubmittedActiveLines { get; set; }
    public IReadOnlyList<SourceDemandReceptionItem> Items { get; set; } = [];
}

/// <summary>来源领域提交的一项独立实物需求事实。</summary>
public sealed class SourceDemandReceptionItem
{
    public Guid SourceLineId { get; set; }
    public int? SourceLineNumber { get; set; }
    public Guid? ProductId { get; set; }
    public bool IsNonStandard { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? LinkSkuSnapshot { get; set; }
    public string? CustomerSkuSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public string? ColorSnapshot { get; set; }
    public string? SpecificationSnapshot { get; set; }
    public string? ImagePathSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal DemandQty { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? Remark { get; set; }
}

/// <summary>来源需求接收结果，区分新增、更新和无变化的需求行。</summary>
public sealed class SourceDemandReceptionResult
{
    public int SourceVersion { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int TotalCount => CreatedCount + UpdatedCount + UnchangedCount;
}

/// <summary>来源确认退出结果，保留物理删除和作废留痕两种既有路径。</summary>
public sealed class SourceDemandExitResult
{
    public int HardDeletedCount { get; set; }
    public int VoidedCount { get; set; }
}

/// <summary>来源需求接收失败；Code 是调用方可稳定识别的业务错误码。</summary>
public sealed class SourceDemandReceptionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
