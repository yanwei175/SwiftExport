using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>提供来源需求可交付查询，并在调用方事务内锁定、复核交付额度。</summary>
public interface ISourceDemandDeliveryService
{
    /// <summary>查询销售与投诉补货来源的当前可交付要求，不依赖物料需求或 Allocation。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取单项来源需求的交付上下文和库存动作阻断原因。</summary>
    Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);

    /// <summary>按固定来源需求顺序加锁并复核版本、生命周期和剩余额度；不自行提交事务。</summary>
    Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDemandDeliveryClaim> claims,
        bool requireInventoryEligibility,
        CancellationToken cancellationToken = default);
}

/// <summary>PL 等消费者提交的来源额度声明；版本来自建立草稿时看到的来源需求事实。</summary>
public sealed class SourceDemandDeliveryClaim
{
    public Guid GrossDemandId { get; set; }
    public int ExpectedDemandVersion { get; set; }
    public int? ExpectedSourceVersion { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>来源交付复核失败；Code 供领域消费者稳定映射业务阻断。</summary>
public sealed class SourceDemandDeliveryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
