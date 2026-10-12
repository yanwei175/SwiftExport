using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>主型号保存中的标签校验和关系维护；沿用主型号外层事务与分类父锁。</summary>
public sealed partial class ProductMasterMaintenanceService
{
    /// <summary>先观察旧分类，再按 Uid 锁定旧、新分类；主型号加锁后必须重验观察结果。</summary>
    private async Task<Dictionary<Guid, Guid>> ObserveAndLockCategoriesAsync(IEnumerable<Guid> masterIds,
        IEnumerable<Guid> targetCategories, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = masterIds.Distinct().OrderBy(id => id).ToArray();
        var observed = ids.Length == 0 ? [] : await masterRepository.GetByIdListAsync(ids, false);
        var targets = targetCategories.Where(id => id != Guid.Empty).ToHashSet();
        var categoryIds = observed.Select(row => row.CategoryId).Concat(targets)
            .Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (categoryIds.Length > 0)
        {
            var locked = await categoryRepository.GetByIdListAsync(categoryIds, true);
            if (targets.Except(locked.Where(row => !row.IsDeleted).Select(row => row.Uid)).Any())
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "产品分类不存在或已删除。");
        }
        return observed.ToDictionary(row => row.Uid, row => row.CategoryId);
    }

    /// <summary>拒绝锁前后分类不一致的记录，不能带着不完整父锁继续写入。</summary>
    private static void EnsureObservedCategories(IReadOnlyDictionary<Guid, Guid> observed, IEnumerable<ProductMaster> masters)
    {
        if (masters.Any(row => !observed.TryGetValue(row.Uid, out var categoryId) || categoryId != row.CategoryId))
            throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号分类已变化，请刷新后重试。");
    }

    /// <summary>列表和导入不得隐式丢失已有标签；只有详情允许提交新的最终选择。</summary>
    private static void ThrowTaggedCategoryChange(string masterSku) => throw new ProductMasterWriteException(
        ServiceErrorCodes.BusinessBlocked, $"主型号 {masterSku} 已有标签，请到主型号详情调整分类并明确处理标签。");

    /// <summary>校验最终选择属于具体分类且满足每组的单选规则，不使用客户端缓存作授权依据。</summary>
    private async Task ValidateAttributeSelectionsAsync(Guid categoryId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (selectedIds.Any(id => id == Guid.Empty) || selectedIds.Distinct().Count() != selectedIds.Count)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "标签选项标识无效或重复。");
        if (selectedIds.Count == 0) return;
        if (categoryId == Guid.Empty) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "未分类的主型号不能选择分类标签。");
        var groups = await attributeRepository.GetDefinitionsAsync(categoryId, cancellationToken);
        var choices = groups.SelectMany(group => group.Options.Select(option => (Group: group, Option: option)))
            .Where(row => selectedIds.Contains(row.Option.Uid)).ToArray();
        if (choices.Length != selectedIds.Count)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "部分标签选项已失效或不属于最终分类，请刷新标签后明确处理。");
        if (choices.GroupBy(row => row.Group.Uid).Any(group => group.First().Group.SelectionMode == ProductAttributeSelectionMode.Single && group.Count() > 1))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "单选标签组只能选择一个值。");
    }

    /// <summary>在主型号锁下按差集保存选择，保留未变关系，所有写入失败由外层统一回滚。</summary>
    private async Task SaveAttributeSelectionsAsync(Guid masterId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        var current = await attributeRepository.GetSelectionsAsync([masterId], cancellationToken);
        var finalIds = selectedIds.ToHashSet();
        foreach (var row in current.Where(row => !finalIds.Contains(row.OptionId)).OrderBy(row => row.Uid))
            EnsureWrite(await attributeRepository.DeleteSelectionAsync(row.Uid, row.Version), "删除标签选择失败。");
        var currentIds = current.Select(row => row.OptionId).ToHashSet();
        foreach (var id in finalIds.Except(currentIds).OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWrite(await attributeRepository.AddSelectionAsync(new ProductMasterAttributeSelection { ProductMasterId = masterId, OptionId = id }), "保存标签选择失败。");
        }
    }

    /// <summary>删除主型号时同步软删除有效选择，不能留下继续占用定义的活动关系。</summary>
    private async Task RemoveAttributeSelectionsAsync(Guid masterId, CancellationToken cancellationToken)
    {
        foreach (var row in (await attributeRepository.GetSelectionsAsync([masterId], cancellationToken)).OrderBy(row => row.Uid))
            EnsureWrite(await attributeRepository.DeleteSelectionAsync(row.Uid, row.Version), "删除主型号标签失败。");
    }
}
