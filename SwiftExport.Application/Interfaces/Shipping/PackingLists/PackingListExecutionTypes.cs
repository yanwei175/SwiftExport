using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Interfaces.Shipping;

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
