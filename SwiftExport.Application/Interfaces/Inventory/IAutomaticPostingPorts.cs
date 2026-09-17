using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>在调用方已准备的事务中创建并过账 PackingList 自动采购收货；不承接需求分配。</summary>
public interface IInboundPostingPort
{
    /// <summary>批量创建自动入库单，返回完整行及流水关联；不提交事务。</summary>
    Task<AutomaticPostingResult> CreateAndPostAsync(AutomaticPostingRequest request, CancellationToken token = default);
    /// <summary>按原单原流水完整反向自动入库，不允许负库存。</summary>
    Task<AutomaticPostingResult> ReverseAsync(AutomaticReverseRequest request, CancellationToken token = default);
}

/// <summary>在调用方事务中创建客户发货或客供件耗用单，不要求客供件采购关系。</summary>
public interface IOutboundPostingPort
{
    /// <summary>按仓库及类型创建出库单；相同产品不同依据保留物理行。</summary>
    Task<AutomaticPostingResult> CreateAndPostAsync(AutomaticPostingRequest request, CancellationToken token = default);
    /// <summary>完整恢复原出库实际仓及数量，不读取当前 BOM。</summary>
    Task<AutomaticPostingResult> ReverseAsync(AutomaticReverseRequest request, CancellationToken token = default);
}

/// <summary>声明整个外层事务的资源集合；准备后不允许追加资源或复用于其他事务。</summary>
public interface IAutomaticPostingScope
{
    /// <summary>在首次写入前按固定顺序取得本次全部资源锁。</summary>
    Task PrepareAsync(AutomaticPostingResources resources, CancellationToken token = default);
}

/// <summary>整个外层操作的稳定资源声明，含入出库及反向子命令。</summary>
public sealed class AutomaticPostingResources
{
    public Guid CorrelationId { get; set; }
    public Guid PackingListId { get; set; }
    public IReadOnlyList<Guid> CommandIds { get; set; } = [];
    public IReadOnlyList<Guid> PurchaseLineIds { get; set; } = [];
    public IReadOnlyList<Guid> InboundDocumentIds { get; set; } = [];
    public IReadOnlyList<Guid> OutboundDocumentIds { get; set; } = [];
    public IReadOnlyList<Guid> LedgerIds { get; set; } = [];
    public IReadOnlyList<Guid> CommandItemIds { get; set; } = [];
    public IReadOnlyList<AutomaticStockKey> StockKeys { get; set; } = [];
}

/// <summary>库存资源身份，不包含采购或客户维度。</summary>
public sealed record AutomaticStockKey(Guid WarehouseSiteId, Guid ProductId);

/// <summary>自动单据批次；身份由调用方稳定生成，人员由服务读取当前上下文。</summary>
public sealed class AutomaticPostingRequest
{
    public Guid CommandId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid PackingListId { get; set; }
    public string PackingListNo { get; set; } = "";
    public DateTime BusinessDate { get; set; }
    public IReadOnlyList<AutomaticDocumentGroup> Groups { get; set; } = [];
}

/// <summary>单个目标仓的单据；入库时 OutboundType 必须为空，出库只允许 20 或 21。</summary>
public sealed class AutomaticDocumentGroup
{
    public Guid GroupId { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public OutboundDocumentTypeEnum? OutboundType { get; set; }
    public IReadOnlyList<AutomaticDocumentLine> Lines { get; set; } = [];
}

/// <summary>已分配的业务明细；LineId 同时作为新单据行及正向流水幂等身份。</summary>
public sealed class AutomaticDocumentLine
{
    public Guid LineId { get; set; }
    public Guid BasisId { get; set; }
    public Guid? PurchaseLineId { get; set; }
    public Guid ProductId { get; set; }
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
}

/// <summary>原自动单据整单反向请求；反向行键由命令和原流水确定性派生。</summary>
public sealed class AutomaticReverseRequest
{
    public Guid CommandId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid PackingListId { get; set; }
    public DateTime BusinessDate { get; set; }
    public string Reason { get; set; } = "";
    public IReadOnlyList<AutomaticReverseDocument> Documents { get; set; } = [];
}

/// <summary>明确指定整单反向目标与期望版本，不接收产品数量覆盖。</summary>
public sealed record AutomaticReverseDocument(Guid DocumentId, int ExpectedVersion);

/// <summary>自动操作完整结果，可持久化重放；不是 PackingList 整体提交确认。</summary>
public sealed class AutomaticPostingResult
{
    public bool AlreadyApplied { get; set; }
    public List<AutomaticDocumentResult> Documents { get; set; } = [];
}

/// <summary>生成或反向的单据及其请求明细对应关系。</summary>
public sealed class AutomaticDocumentResult
{
    public Guid DocumentId { get; set; }
    public string DocumentNo { get; set; } = "";
    public int Version { get; set; }
    public int PostingStatus { get; set; }
    public List<AutomaticLineResult> Lines { get; set; } = [];
}

/// <summary>一条请求明细与正式行、流水的稳定关联。</summary>
public sealed record AutomaticLineResult(Guid RequestLineId, Guid DocumentLineId, Guid LedgerId, Guid? BasisId = null);

/// <summary>自动业务失败，调用方不得继续提交当前事务。</summary>
public sealed class AutomaticPostingException(string code, string message, Guid? itemId = null) : Exception(message)
{
    public string Code { get; } = code;
    public Guid? ItemId { get; } = itemId;
}
