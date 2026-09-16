using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>交付查询与复核边界；来源退出核验已接入，尚未接入的查询、额度与结案能力继续明确拒绝。</summary>
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

    /// <summary>在调用方事务内复核来源版本、生命周期、交付余额和实际库存动作所需条件。</summary>
    Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims,
        bool requireInventoryContext,
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

/// <summary>PL 提交的来源交付额度声明；订单池要求只在该行实际消耗客供件时生效。</summary>
public sealed class SourceDeliveryClaim
{
    public Guid GrossDemandId { get; set; }
    public int ExpectedDemandVersion { get; set; }
    public int? ExpectedSourceVersion { get; set; }
    public decimal Quantity { get; set; }
    public bool RequiresOrderPoolOwnership { get; set; }
}

/// <summary>交付复核失败；稳定代码供 PL 和来源单据调用方映射业务阻断。</summary>
public sealed class SourceDeliveryException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>稳定业务阻断代码。</summary>
    public string Code { get; } = code;
}
