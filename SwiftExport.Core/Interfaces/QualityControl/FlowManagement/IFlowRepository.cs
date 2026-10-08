using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Interfaces.QualityControl.FlowManagement;

/// <summary>流量测试组的 PostgreSQL 持久化端口，始终借用调用方会话和事务。</summary>
public interface IFlowRepository
{
    /// <summary>按稳定日期和身份排序返回分页摘要。</summary>
    Task<PagedResponseBase<FlowListItemDto>> GetPagedAsync(FlowSearchRequest request, CancellationToken token);
    /// <summary>在同一条查询中读取头及行，避免整组并发更新造成混合版本详情。</summary>
    Task<FlowDetailDto?> GetDetailAsync(Guid id, CancellationToken token);
    /// <summary>读取未删除头；维护时必须在事务内锁定。</summary>
    Task<FlowHeader?> ReadHeaderAsync(Guid id, bool forUpdate, CancellationToken token);
    /// <summary>按身份锁定并读取所属头的未删除行。</summary>
    Task<IReadOnlyList<FlowLine>> ReadLinesAsync(Guid headerId, CancellationToken token);
    /// <summary>在写入事务中共享锁定物料，供新增资格与 SKU 快照校验。</summary>
    Task<Product?> ReadMaterialAsync(Guid id, CancellationToken token);
    /// <summary>按身份顺序共享锁定未删除字典项及分类；没有有效分类的项不返回。</summary>
    Task<IReadOnlyList<SysDictData>> ReadDictionaryItemsAsync(IEnumerable<Guid> ids, CancellationToken token);
    /// <summary>新增头，复用元数据、顺序主键和审计打戳。</summary>
    Task<RepositoryWriteResult> AddHeaderAsync(FlowHeader entity, CancellationToken token);
    /// <summary>按版本更新已读取的头。</summary>
    Task<RepositoryWriteResult> UpdateHeaderAsync(FlowHeader entity, CancellationToken token);
    /// <summary>按版本软删除头。</summary>
    Task<RepositoryWriteResult> DeleteHeaderAsync(Guid id, int version, CancellationToken token);
    /// <summary>新增功能行并分配持久化身份。</summary>
    Task<RepositoryWriteResult> AddLineAsync(FlowLine entity, CancellationToken token);
    /// <summary>按版本更新已读取的功能行。</summary>
    Task<RepositoryWriteResult> UpdateLineAsync(FlowLine entity, CancellationToken token);
    /// <summary>按版本软删除功能行。</summary>
    Task<RepositoryWriteResult> DeleteLineAsync(Guid id, int version, CancellationToken token);
}

