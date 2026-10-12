using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Attributes;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>既有无标签场景的测试仓储；只允许空读取，意外标签写入立即失败。</summary>
internal sealed class EmptyProductAttributeRepository : IProductAttributeRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroupDto>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? categoryId, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeGroup>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductAttributeOption>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductMasterAttributeSelection>> GetSelectionsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductMasterAttributeSelection>>([]);
    /// <inheritdoc />
    public Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectionDetailsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductMasterAttributeSelectionDto>>([]);
    /// <inheritdoc />
    public Task<int> CountReferencedMastersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken) => Task.FromResult(0);
    /// <inheritdoc />
    public Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult(0);
    /// <inheritdoc />
    public Task<bool> HasGroupsAsync(IEnumerable<Guid> categoryIds, CancellationToken cancellationToken) => Task.FromResult(false);
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
    public Task<RepositoryWriteResult> AddSelectionAsync(ProductMasterAttributeSelection entity) => throw new InvalidOperationException("无标签夹具不允许新增选择。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version) => throw new InvalidOperationException("无标签夹具不允许删除选择。");
}
