using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Products.Attributes;

/// <summary>分类标签的持久化边界；共用调用方事务，分类锁由应用服务持有。</summary>
public interface IProductAttributeRepository
{
    /// <summary>以同一查询快照读取有效分类的组和选项，保留没有选项的组。</summary>
    Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken);
    /// <summary>读取有效标签组；空分类表示全部，维护时按 Uid 锁定组。</summary>
    Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? categoryId, bool forUpdate, CancellationToken cancellationToken);
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
    /// <summary>检查指定分类是否仍有有效标签组。</summary>
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
    /// <summary>新增主型号选择。</summary>
    Task<RepositoryWriteResult> AddSelectionAsync(ProductMasterAttributeSelection entity);
    /// <summary>按版本软删除主型号选择。</summary>
    Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version);
}
