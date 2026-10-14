using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Products.Attributes;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>既有无标签场景的测试仓储；只允许空读取，意外标签写入立即失败。</summary>
internal sealed class EmptyProductAttributeRepository : IProductAttributeRepository
{
    /// <inheritdoc />
    public Task<ProductAttributeDefinitionsDto> GetSnapshotAsync(Guid? profileId, CancellationToken cancellationToken) =>
        Task.FromResult(new ProductAttributeDefinitionsDto { ProfileId = profileId ?? Guid.Empty });
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? profileId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroupDto>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? profileId, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroup>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForUpdateAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroup>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForSelectionAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroup>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> GetGroupIdsForOptionsAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeProfileGroup>> GetRelationsAsync(IEnumerable<Guid>? groupIds, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeProfileGroup>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectedReferencesAsync(IEnumerable<Guid>? groupIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeSelectionDto>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeOption>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeSelection>> GetSelectionsAsync(ProductAttributeOwnerType ownerType, IEnumerable<Guid> ownerIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeSelection>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectionDetailsAsync(ProductAttributeOwnerType ownerType, IEnumerable<Guid> ownerIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeSelectionDto>>([]);
    /// <inheritdoc />
    public Task<int> CountReferencedOwnersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken) => Task.FromResult(0);
    /// <inheritdoc />
    public Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult(0);
    /// <inheritdoc />
    public Task<IReadOnlyList<SysDictData>> GetProfilesAsync(IEnumerable<Guid> profileIds, bool withShareLock, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SysDictData>>([]);
    /// <inheritdoc />
    public Task<int> CountProfileReferencesAsync(Guid profileId, CancellationToken cancellationToken) => Task.FromResult(0);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddGroupAsync(ProductAttributeGroup entity) => throw new InvalidOperationException("无标签夹具不允许写标签组。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateGroupAsync(ProductAttributeGroup entity) => throw new InvalidOperationException("无标签夹具不允许写标签组。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteGroupAsync(Guid uid, int version) => throw new InvalidOperationException("无标签夹具不允许删标签组。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddOptionAsync(ProductAttributeOption entity) => throw new InvalidOperationException("无标签夹具不允许写选项。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateOptionAsync(ProductAttributeOption entity) => throw new InvalidOperationException("无标签夹具不允许写选项。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteOptionAsync(Guid uid, int version) => throw new InvalidOperationException("无标签夹具不允许删选项。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddRelationAsync(ProductAttributeProfileGroup entity) => throw new InvalidOperationException("无标签夹具不允许新增模板关联。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteRelationAsync(Guid uid, int version) => throw new InvalidOperationException("无标签夹具不允许删除模板关联。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddSelectionAsync(ProductAttributeSelection entity) => throw new InvalidOperationException("无标签夹具不允许新增选择。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version) => throw new InvalidOperationException("无标签夹具不允许删除选择。");
}
