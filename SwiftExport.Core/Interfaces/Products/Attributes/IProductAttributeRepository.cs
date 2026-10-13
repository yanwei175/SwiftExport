using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Products.Attributes;

/// <summary>共享标签与分类关联的持久化边界；共用调用方事务，稳定锁集合由应用服务决定。</summary>
public interface IProductAttributeRepository
{
    /// <summary>以同一查询快照读取组、选项和直接关联；分类过滤包含祖先提供的组。</summary>
    Task<ProductAttributeDefinitionsDto> GetSnapshotAsync(Guid? categoryId, CancellationToken cancellationToken);
    /// <summary>读取有效组和选项，保留没有选项或没有分类关联的全局组。</summary>
    Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken);
    /// <summary>读取有效标签组；空分类表示全部，分类过滤包含祖先继承。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? categoryId, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>按 Uid 顺序精确锁定待维护组，避免全局锁定无关定义。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForUpdateAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken);
    /// <summary>按 Uid 顺序对产品选择涉及的组取共享锁，与定义维护串行。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForSelectionAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken);
    /// <summary>通过请求选项定位锁前组身份，包含软删选项；锁后仍须重新校验有效性。</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsForOptionsAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken);
    /// <summary>读取直接关联；null 表示全部、空集合返回空，维护时按 Uid 锁定。</summary>
    Task<IReadOnlyList<ProductCategoryAttributeGroup>> GetRelationsAsync(IEnumerable<Guid>? groupIds, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>读取有效主型号已有选择及其实际分类；null 表示全部、空集合返回空，用于最终适用性校验。</summary>
    Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectedReferencesAsync(IEnumerable<Guid>? groupIds, CancellationToken cancellationToken);
    /// <summary>读取所选组的有效选项，维护时按 Uid 锁定。</summary>
    Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>读取主型号的有效选择，保留原始选项身份。</summary>
    Task<IReadOnlyList<ProductMasterAttributeSelection>> GetSelectionsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken);
    /// <summary>批量读取选择和实时展示信息，失效选项仍保留选择记录。</summary>
    Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectionDetailsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken);
    /// <summary>统计有效主型号中引用给定选项的不同主型号数量。</summary>
    Task<int> CountReferencedMastersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken);
    /// <summary>统计某组同时选中多个选项的有效主型号数量。</summary>
    Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken);
    /// <summary>检查指定分类是否仍有有效直接标签关联，不因继承阻止删除。</summary>
    Task<bool> HasGroupsAsync(IEnumerable<Guid> categoryIds, CancellationToken cancellationToken);
    /// <summary>新增标签组，复用通用审计与服务端身份生成。</summary>
    Task<RepositoryWriteResult> AddGroupAsync(ProductAttributeGroup entity);
    /// <summary>按版本更新标签组。</summary>
    Task<RepositoryWriteResult> UpdateGroupAsync(ProductAttributeGroup entity);
    /// <summary>按版本软删除标签组。</summary>
    Task<RepositoryWriteResult> DeleteGroupAsync(Guid uid, int version);
    /// <summary>新增组内选项。</summary>
    Task<RepositoryWriteResult> AddOptionAsync(ProductAttributeOption entity);
    /// <summary>按版本更新组内选项。</summary>
    Task<RepositoryWriteResult> UpdateOptionAsync(ProductAttributeOption entity);
    /// <summary>按版本软删除组内选项。</summary>
    Task<RepositoryWriteResult> DeleteOptionAsync(Guid uid, int version);
    /// <summary>新增分类与标签组的直接关联。</summary>
    Task<RepositoryWriteResult> AddRelationAsync(ProductCategoryAttributeGroup entity);
    /// <summary>按版本软删除直接关联，身份不提供更新入口。</summary>
    Task<RepositoryWriteResult> DeleteRelationAsync(Guid uid, int version);
    /// <summary>新增主型号选择。</summary>
    Task<RepositoryWriteResult> AddSelectionAsync(ProductMasterAttributeSelection entity);
    /// <summary>按版本软删除主型号选择。</summary>
    Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version);
}
