using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>交付查询与额度复核边界；结案能力尚未接入时继续明确拒绝。</summary>
public interface ISourceDeliveryService
{
    /// <summary>查询认可来源的当前可交付要求。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取单项来源要求的交付上下文。</summary>
    Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);

    /// <summary>在调用方事务内复核来源版本、生命周期和交付余额。</summary>
    Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims,
        CancellationToken cancellationToken = default);

    /// <summary>在来源单据调用方事务内核验要求身份以及正式交付完成或合法关闭证据。</summary>
    Task VerifyClosureAsync(
        string sourceType,
        Guid sourceUid,
        IReadOnlyDictionary<Guid, decimal> expectedLines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在来源调用方事务内核验相关 PackingList 草稿及正式客户出库已安全退出；成功时无返回值。
    /// </summary>
    Task VerifySourceExitAsync(
        string sourceType,
        Guid sourceUid,
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);
}

/// <summary>PackingList 提交的来源交付额度声明。</summary>
public sealed class SourceDeliveryClaim
{
    /// <summary>被申报的 GrossDemand 标识。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>调用方已见的需求版本；未提供时只校验锁定后的当前事实。</summary>
    public int? ExpectedDemandVersion { get; set; }
    /// <summary>调用方已见的上游事实版本；未提供时不比较。</summary>
    public int? ExpectedSourceVersion { get; set; }
    /// <summary>本次命令针对该来源申报的数量。</summary>
    public decimal Quantity { get; set; }
}

/// <summary>交付复核失败；稳定代码供 PL 和来源单据调用方映射业务阻断。</summary>
public sealed class SourceDeliveryException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>稳定业务阻断代码。</summary>
    public string Code { get; } = code;
}
