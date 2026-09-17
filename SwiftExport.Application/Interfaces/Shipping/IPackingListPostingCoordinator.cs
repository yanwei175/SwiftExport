using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>箱单过账内部编排边界；HTTP 发布前必须完成外层并发及权限接线验收。</summary>
public interface IPackingListPostingCoordinator
{
    /// <summary>只读预览，使用服务器事实，不占用库存。</summary>
    Task<PackingListPostingPreviewBuilder.Preview> PreviewAsync(Guid uid, int expectedVersion, DateTime businessDate, CancellationToken token);
    /// <summary>一次事务完成入出库、执行证据、箱单状态和日志，失败整体回滚。</summary>
    Task<PackingListPostOutcome> PostAsync(PackingListPostRequest request, CancellationToken token);
    /// <summary>按原执行证据预览整单反向，不改变库存。</summary>
    Task<PackingListPostingPreviewBuilder.Preview> ReversePreviewAsync(Guid uid, int expectedVersion, DateTime businessDate, string reason, CancellationToken token);
    /// <summary>先反向出库再反向自动入库，同事务保存反向事实及箱单状态。</summary>
    Task<PackingListPostOutcome> ReverseAsync(PackingListReverseRequest request, CancellationToken token);
}

/// <summary>服务内部不可变命令，不接受用户身份和库存分配结果。</summary>
public sealed record PackingListPostRequest(Guid PackingListId, Guid CommandId, int ExpectedVersion,
    DateTime BusinessDate, string PreviewToken);

/// <summary>整单反向命令，不接受调用方覆盖原仓库、产品或数量。</summary>
public sealed record PackingListReverseRequest(Guid PackingListId, Guid CommandId, int ExpectedVersion,
    DateTime BusinessDate, string Reason, string PreviewToken);

/// <summary>原始命令结果，可完整重放；这里只保存查询 DTO，不暴露领域实体。</summary>
public sealed class PackingListPostOutcome
{
    public bool AlreadyApplied { get; set; }
    public Guid ExecutionId { get; set; }
    public PackingListDetailDto? Detail { get; set; }
    public IReadOnlyList<Guid> InboundDocumentIds { get; set; } = [];
    public IReadOnlyList<Guid> OutboundDocumentIds { get; set; } = [];
    /// <summary>实际端口返回的单据摘要，随原命令结果持久化以完整重放。</summary>
    public List<PackingListGeneratedDocumentDto> Documents { get; set; } = [];
}

/// <summary>内部过账失败及稳定错误码；调用方不得继续提交失败事务。</summary>
public sealed class PackingListPostingFailure(string code, string message) : Exception(message)
{
    /// <summary>计算器给出的全部来源阻断，避免 HTTP 只保留首条错误。</summary>
    public IReadOnlyList<PackingListPostingPreviewBuilder.Blocker> Blockers { get; init; } = [];
    /// <summary>稳定的失败定位代码。</summary>
    public string Code { get; } = code;
}
