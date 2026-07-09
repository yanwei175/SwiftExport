using SwiftExport.Contracts.DTOs.DependentDemands;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 销售订单拆解中间表仓储接口。
    /// </summary>
    public interface IDependentDemandRepository : IRepository<DependentDemand>
    {
        /// <summary>
        /// 根据来源类型与来源单号查询中间表记录。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetListBySourceTypeAndSourceUidAsync(string sourceType, Guid sourceUid);
        /// <summary>
        /// 根据来源类型与来源单号查询需求池记录。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 根据来源类型与来源销售单行 Uid 查询中间表记录（可能多行）。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetListBySourceLineIdAsync(string sourceType, Guid sourceSoLineId);


        Task<IReadOnlyList<DependentDemand>> GetListByTargetLineIdsAsync(IEnumerable<Guid> targetIdList, bool withUpdLock = false);

        Task<int> ResetByTargetLineIdsAsync(IEnumerable<Guid> targetIdList);
        /// <summary>
        /// 删除来源类型与来源单号对应的需求池记录。
        /// </summary>
        Task<int> DeleteBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按业务维度分页查询（用于管理页）。
        /// </summary>
        Task<PagedResponseBase<DependentDemandDto>> GetPagedAsync(DependentDemandSearchRequest request);
    }
}
