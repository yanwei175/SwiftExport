using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Application.Services.Products.Attributes;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>主型号保存中的标签校验和关系维护；沿用主型号外层事务与分类父锁。</summary>
public sealed partial class ProductMasterMaintenanceService
{
    /// <summary>先观察旧、新分类的祖先路径，再按 Uid 锁定完整路径；变化时拒绝重试，不能追加逆序锁。</summary>
    private async Task<Dictionary<Guid, Guid>> ObserveAndLockCategoriesAsync(IEnumerable<Guid> masterIds,
        IEnumerable<Guid> targetCategories, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = masterIds.Distinct().OrderBy(id => id).ToArray();
        var observed = ids.Length == 0 ? [] : await masterRepository.GetByIdListAsync(ids, false);
        var targets = targetCategories.Where(id => id != Guid.Empty).ToHashSet();
        var roots = observed.Select(row => row.CategoryId).Concat(targets)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        if (roots.Length > 0)
        {
            var before = (await categoryRepository.GetAllAsync()).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid, row => row.ParentId);
            Dictionary<Guid, Guid[]> paths;
            try { paths = roots.ToDictionary(id => id, id => ProductAttributeApplicability.GetAncestorIds(id, before).ToArray()); }
            catch (InvalidOperationException exception) { throw new ProductMasterWriteException(ServiceErrorCodes.Validation, exception.Message); }
            var categoryIds = paths.Values.SelectMany(path => path).Distinct().OrderBy(id => id).ToArray();
            // 通用 ANY 查询没有行锁顺序保证，必须逐个按身份取锁，和全树维护保持相同顺序。
            var locked = new List<ProductCategory>();
            foreach (var categoryId in categoryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var category = await categoryRepository.GetByIdAsync(categoryId, true);
                if (category is not null && !category.IsDeleted) locked.Add(category);
            }
            var after = (await categoryRepository.GetAllAsync()).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid, row => row.ParentId);
            try
            {
                if (locked.Count != categoryIds.Length || roots.Any(id => !paths[id].SequenceEqual(ProductAttributeApplicability.GetAncestorIds(id, after))))
                    throw new InvalidOperationException("产品分类祖先路径已变化，请刷新后重试。");
            }
            catch (InvalidOperationException exception) { throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, exception.Message); }
        }
        return observed.ToDictionary(row => row.Uid, row => row.CategoryId);
    }

    /// <summary>拒绝锁前后分类不一致的记录，不能带着不完整父锁继续写入。</summary>
    private static void EnsureObservedCategories(IReadOnlyDictionary<Guid, Guid> observed, IEnumerable<ProductMaster> masters)
    {
        if (masters.Any(row => !observed.TryGetValue(row.Uid, out var categoryId) || categoryId != row.CategoryId))
            throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号分类已变化，请刷新后重试。");
    }

    /// <summary>按主型号身份逐个取得锁，避免通用 ANY 查询以及分批编码锁改变聚合锁顺序。</summary>
    private async Task<IReadOnlyList<ProductMaster>> LockMastersAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = new List<ProductMaster>();
        foreach (var id in ids.Distinct().OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await masterRepository.GetByIdAsync(id, true);
            if (row is not null && !row.IsDeleted) rows.Add(row);
        }
        return rows;
    }

    /// <summary>分类锁后、主型号锁前锁定旧选择及本次新选择涉及的组，阻止定义删除或改成单选竞争。</summary>
    private async Task LockAttributeGroupsAsync(IEnumerable<Guid> masterIds, IEnumerable<Guid>? newOptionIds, CancellationToken cancellationToken)
    {
        var selections = await attributeRepository.GetSelectionsAsync(masterIds, cancellationToken);
        var optionIds = selections.Select(row => row.OptionId).Concat(newOptionIds ?? []).Distinct().ToArray();
        if (optionIds.Length == 0) return;
        var groupIds = await attributeRepository.GetGroupIdsForOptionsAsync(optionIds, cancellationToken);
        await attributeRepository.LockGroupsForSelectionAsync(groupIds, cancellationToken);
    }

    /// <summary>无显式最终选择的入口仅允许保留全部已有值的归类，不隐式丢失关系。</summary>
    private async Task ValidateRetainedSelectionsAsync(ProductMaster master, Guid targetCategoryId, CancellationToken cancellationToken)
    {
        var selectedIds = (await attributeRepository.GetSelectionsAsync([master.Uid], cancellationToken)).Select(row => row.OptionId).ToArray();
        try { await ValidateAttributeSelectionsAsync(targetCategoryId, selectedIds, cancellationToken); }
        catch (ProductMasterWriteException exception) when (exception.Code == ServiceErrorCodes.Validation)
        {
            throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked,
                $"主型号 {master.MasterSku} 的部分标签在目标分类不适用，请到主型号详情调整分类并明确处理标签。");
        }
    }

    /// <summary>校验最终选择属于分类自身或祖先提供的有效组且满足单选规则，不使用客户端缓存作授权依据。</summary>
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
