using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Interfaces.Products.Attributes;

/// <summary>共享标签与模板关联的持久化边界；共用调用方事务，稳定锁集合由应用服务决定。</summary>
public interface IProductAttributeRepository
{
    /// <summary>以同一查询快照读取组、选项和有效模板关联；null 表示全局定义。</summary>
    Task<ProductAttributeDefinitionsDto> GetSnapshotAsync(Guid? profileId, CancellationToken cancellationToken);
    /// <summary>读取有效组和选项，保留没有选项或没有模板关联的全局组。</summary>
    Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? profileId, CancellationToken cancellationToken);
    /// <summary>读取有效标签组；null 模板表示全部，模板过滤只读取直接适用组。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? profileId, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>按 Uid 顺序精确锁定待维护组，避免全局锁定无关定义。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForUpdateAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken);
    /// <summary>按 Uid 顺序对产品选择涉及的组取共享锁，与定义维护串行。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForSelectionAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken);
    /// <summary>通过请求选项定位锁前组身份，包含软删选项；锁后仍须重新校验有效性。</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsForOptionsAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken);
    /// <summary>读取直接关联；null 表示全部、空集合返回空，维护时按 Uid 锁定。</summary>
    Task<IReadOnlyList<ProductAttributeProfileGroup>> GetRelationsAsync(IEnumerable<Guid>? groupIds, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>读取所有有效产品对象已有选择及其当前模板；null 表示全部、空集合返回空。</summary>
    Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectedReferencesAsync(IEnumerable<Guid>? groupIds, CancellationToken cancellationToken);
    /// <summary>读取所选组的有效选项，维护时按 Uid 锁定。</summary>
    Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>按对象类型及身份读取有效选择，保留原始选项身份。</summary>
    Task<IReadOnlyList<ProductAttributeSelection>> GetSelectionsAsync(ProductAttributeOwnerType ownerType, IEnumerable<Guid> ownerIds, CancellationToken cancellationToken);
    /// <summary>批量读取选择和实时展示信息，失效选项仍保留选择记录。</summary>
    Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectionDetailsAsync(ProductAttributeOwnerType ownerType, IEnumerable<Guid> ownerIds, CancellationToken cancellationToken);
    /// <summary>按对象类型及身份统计引用给定选项的不同有效对象数量。</summary>
    Task<int> CountReferencedOwnersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken);
    /// <summary>统计某组同时选中多个选项的不同有效对象数量，覆盖所有对象类型。</summary>
    Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken);
    /// <summary>读取模板字典明细的完整事实；需要时按 Uid 取共享锁，软删和停用仍供应用层判断。</summary>
    Task<IReadOnlyList<SysDictData>> GetProfilesAsync(IEnumerable<Guid> profileIds, bool withShareLock, CancellationToken cancellationToken);
    /// <summary>统计模板被有效主型号和有效标签组关联引用的记录数。</summary>
    Task<int> CountProfileReferencesAsync(Guid profileId, CancellationToken cancellationToken);
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
    /// <summary>新增模板与标签组的直接关联。</summary>
    Task<RepositoryWriteResult> AddRelationAsync(ProductAttributeProfileGroup entity);
    /// <summary>按版本软删除直接关联，身份不提供更新入口。</summary>
    Task<RepositoryWriteResult> DeleteRelationAsync(Guid uid, int version);
    /// <summary>新增对象选择；所属对象的准入校验由应用服务负责。</summary>
    Task<RepositoryWriteResult> AddSelectionAsync(ProductAttributeSelection entity);
    /// <summary>按版本软删除对象选择。</summary>
    Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version);
}
