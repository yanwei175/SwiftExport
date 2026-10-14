using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>主型号的模板适用性校验和选择维护；共享外层事务，按模板、组、主型号取得稳定锁。</summary>
public sealed partial class ProductMasterMaintenanceService
{
    // 请求级事务已锁组范围；上游取得主型号锁后只能重验，不能追加逆序组锁。
    private HashSet<Guid> _lockedAttributeGroupIds = [];
    /// <summary>分类只保护直接产品引用，不再遍历标签祖先；旧、新分类按身份排序锁定。</summary>
    private async Task<Dictionary<Guid, Guid>> ObserveAndLockCategoriesAsync(IEnumerable<Guid> masterIds,
        IEnumerable<Guid> targetCategories, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().OrderBy(id => id).ToArray();
        var observed = ids.Length == 0 ? [] : await masterRepository.GetByIdListAsync(ids, false);
        foreach (var id in observed.Select(row => row.CategoryId).Concat(targetCategories)
            .Where(id => id != Guid.Empty).Distinct().OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await categoryRepository.GetByIdAsync(id, true) is not { IsDeleted: false })
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "产品分类不存在或已删除。");
        }
        return observed.ToDictionary(row => row.Uid, row => row.CategoryId);
    }

    /// <summary>旧、新模板及系列合并按字典明细 Uid 取得共享锁，防止跨类型逆序锁和字典停用竞争。</summary>
    private async Task<Dictionary<Guid, (Guid? ProfileId, Guid? SeriesId)>> ObserveAndLockDictionaryReferencesAsync(IEnumerable<Guid> masterIds,
        IEnumerable<Guid?> targetProfiles, IEnumerable<Guid?> targetSeries, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().OrderBy(id => id).ToArray();
        var observed = ids.Length == 0 ? [] : await masterRepository.GetByIdListAsync(ids, false);
        var targets = targetProfiles.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var seriesTargets = targetSeries.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var dictionaryIds = observed.SelectMany(row => new[] { row.ProductAttributeProfileId, row.ProductSeriesId })
            .Where(id => id.HasValue).Select(id => id!.Value).Concat(targets).Concat(seriesTargets).Distinct().OrderBy(id => id).ToArray();
        // 现有读取不按类型过滤，统一锁定完整字典事实后分别校验模板和系列归属。
        var profiles = (await attributeRepository.GetProfilesAsync(dictionaryIds, true, cancellationToken)).ToDictionary(row => row.Uid);
        if (targets.Any(id => id == Guid.Empty || !profiles.TryGetValue(id, out var profile) || profile.IsDeleted || !profile.IsEnabled
            || !string.Equals(profile.DictCode?.Trim(), DictCodes.ProductAttributeProfile, StringComparison.OrdinalIgnoreCase)))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "产品属性模板不存在、已停用或属于其他字典类型。");
        if (seriesTargets.Any(id => id == Guid.Empty || !profiles.TryGetValue(id, out var series) || series.IsDeleted || !series.IsEnabled
            || !string.Equals(series.DictCode?.Trim(), DictCodes.ProductSeries, StringComparison.OrdinalIgnoreCase)))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "产品系列不存在、已停用或属于其他字典类型，请选择有效系列或清空。");
        return observed.ToDictionary(row => row.Uid, row => (row.ProductAttributeProfileId, row.ProductSeriesId));
    }

    /// <summary>拒绝锁前后分类、模板或系列变化，不能带着不完整父锁继续保存。</summary>
    private static void EnsureObservedReferences(IReadOnlyDictionary<Guid, Guid> categories,
        IReadOnlyDictionary<Guid, (Guid? ProfileId, Guid? SeriesId)> references, IEnumerable<ProductMaster> masters)
    {
        if (masters.Any(row => !categories.TryGetValue(row.Uid, out var categoryId) || categoryId != row.CategoryId
            || !references.TryGetValue(row.Uid, out var dictionary) || dictionary.ProfileId != row.ProductAttributeProfileId
            || dictionary.SeriesId != row.ProductSeriesId))
            throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号分类、属性模板或系列已变化，请刷新后重试。");
    }

    /// <summary>按主型号身份逐个取得锁，避免通用批量读取改变聚合锁顺序。</summary>
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

    /// <summary>模板锁后、主型号锁前锁定旧选择及本次新选择涉及的组，防止定义删除或改单选竞争。</summary>
    private async Task LockAttributeGroupsAsync(IEnumerable<Guid> masterIds, IEnumerable<Guid>? newOptionIds, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().ToArray();
        var selections = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.ProductMaster, ids, cancellationToken);
        var productIds = ids.Length == 0 ? [] : await productRepository.GetIdsByProductMasterIdsAsync(ids, cancellationToken);
        var products = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, productIds, cancellationToken);
        var optionIds = selections.Select(row => row.OptionId).Concat(products.Select(row => row.OptionId)).Concat(newOptionIds ?? []).Distinct().ToArray();
        _lockedAttributeGroupIds = [];
        if (optionIds.Length == 0) return;
        var groupIds = await attributeRepository.GetGroupIdsForOptionsAsync(optionIds, cancellationToken);
        await attributeRepository.LockGroupsForSelectionAsync(groupIds, cancellationToken);
        _lockedAttributeGroupIds = groupIds.ToHashSet();
    }

    /// <summary>上游模板变更或删除不能清空物料自身标签；重验锁范围，避免关联维护与新增选择竞争。</summary>
    private async Task ValidateLinkedProductSelectionsAsync(Guid masterId, Guid? targetProfileId, bool deleting, CancellationToken cancellationToken)
    {
        var productIds = await productRepository.GetIdsByProductMasterIdsAsync([masterId], cancellationToken);
        var selections = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, productIds, cancellationToken);
        if (selections.Count == 0) return;
        var requiredGroups = await attributeRepository.GetGroupIdsForOptionsAsync(selections.Select(row => row.OptionId), cancellationToken);
        if (requiredGroups.Any(id => !_lockedAttributeGroupIds.Contains(id)))
            throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "关联物料的标签选择已变化，请刷新后重试。");
        if (deleting)
            throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, $"有 {selections.Select(row => row.OwnerId).Distinct().Count()} 个关联物料保存了独立标签，不能删除来源主型号，请先处理这些物料标签。");
        var unavailableCount = 0;
        foreach (var owner in selections.GroupBy(row => row.OwnerId))
        {
            try { await ValidateAttributeSelectionsAsync(targetProfileId, owner.Select(row => row.OptionId).ToArray(), cancellationToken); }
            catch (ProductMasterWriteException exception) when (exception.Code == ServiceErrorCodes.Validation) { unavailableCount++; }
        }
        if (unavailableCount > 0)
            throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, $"目标模板会使 {unavailableCount} 个关联物料的独立标签失效，请先处理这些物料标签后再更换模板。");
    }

    /// <summary>管理列表只能保留全部已有值的模板变更，不能通过普通头部保存隐式删除选择。</summary>
    private async Task ValidateRetainedSelectionsAsync(ProductMaster master, Guid? targetProfileId, CancellationToken cancellationToken)
    {
        var selectedIds = (await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.ProductMaster, [master.Uid], cancellationToken))
            .Select(row => row.OptionId).ToArray();
        try { await ValidateAttributeSelectionsAsync(targetProfileId, selectedIds, cancellationToken); }
        catch (ProductMasterWriteException exception) when (exception.Code == ServiceErrorCodes.Validation)
        {
            throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked,
                $"主型号 {master.MasterSku} 的部分标签在目标属性模板不适用，请到主型号详情明确处理这些选择。");
        }
    }

    /// <summary>服务端重验最终模板提供的有效选项及单选规则，不以客户端候选缓存作为写入依据。</summary>
    private async Task ValidateAttributeSelectionsAsync(Guid? profileId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (selectedIds.Any(id => id == Guid.Empty) || selectedIds.Distinct().Count() != selectedIds.Count)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "标签选项标识无效或重复。");
        if (selectedIds.Count == 0) return;
        if (!profileId.HasValue || profileId == Guid.Empty)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "未设置产品属性模板的主型号不能选择标签。");
        var groups = await attributeRepository.GetDefinitionsAsync(profileId, cancellationToken);
        var choices = groups.SelectMany(group => group.Options.Select(option => (Group: group, Option: option)))
            .Where(row => selectedIds.Contains(row.Option.Uid)).ToArray();
        if (choices.Length != selectedIds.Count)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "部分标签选项已失效或不属于最终属性模板，请刷新标签后明确处理。");
        if (choices.GroupBy(row => row.Group.Uid).Any(group => group.First().Group.SelectionMode == ProductAttributeSelectionMode.Single && group.Count() > 1))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "单选标签组只能选择一个值。");
    }

    /// <summary>在主型号锁下按差集维护本对象类型的选择，其他对象即使具有相同 Uid 也不会被修改。</summary>
    private async Task SaveAttributeSelectionsAsync(Guid masterId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        var current = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.ProductMaster, [masterId], cancellationToken);
        var finalIds = selectedIds.ToHashSet();
        foreach (var row in current.Where(row => !finalIds.Contains(row.OptionId)).OrderBy(row => row.Uid))
            EnsureWrite(await attributeRepository.DeleteSelectionAsync(row.Uid, row.Version), "删除标签选择失败。");
        var currentIds = current.Select(row => row.OptionId).ToHashSet();
        foreach (var id in finalIds.Except(currentIds).OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWrite(await attributeRepository.AddSelectionAsync(new ProductAttributeSelection
            { OwnerType = ProductAttributeOwnerType.ProductMaster, OwnerId = masterId, OptionId = id }), "保存标签选择失败。");
        }
    }

    /// <summary>删除主型号时只软删除该类型的有效选择，避免活动关系继续占用标签定义。</summary>
    private async Task RemoveAttributeSelectionsAsync(Guid masterId, CancellationToken cancellationToken)
    {
        foreach (var row in (await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.ProductMaster, [masterId], cancellationToken)).OrderBy(row => row.Uid))
            EnsureWrite(await attributeRepository.DeleteSelectionAsync(row.Uid, row.Version), "删除主型号标签失败。");
    }
}
