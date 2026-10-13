using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.Attributes;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Categories;

namespace SwiftExport.Application.Services.Products.Attributes;

/// <summary>维护全局标签定义及分类直接关联；聚合组版本和稳定父锁保护产品选择适用性。</summary>
public sealed class ProductAttributeDefinitionService(IProductAttributeRepository repository,
    IProductCategoryRepository categories, ICurrentUser currentUser, IPermissionEvaluationService permissions,
    ServiceBaseContext context) : IProductAttributeDefinitionService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<ProductAttributeDefinitionService>();

    /// <inheritdoc />
    public async Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken = default)
    {
        if (categoryId.HasValue)
        {
            var parents = (await categories.GetAllAsync()).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid, row => row.ParentId);
            try { ProductAttributeApplicability.GetAncestorIds(categoryId.Value, parents); }
            catch (InvalidOperationException failure) { return ServiceResult<ProductAttributeDefinitionsDto>.Fail(ServiceErrorCodes.Validation, failure.Message); }
        }
        try { return ServiceResult<ProductAttributeDefinitionsDto>.Ok(await repository.GetSnapshotAsync(categoryId, cancellationToken)); }
        catch (InvalidOperationException failure) when (categoryId.HasValue)
        { return ServiceResult<ProductAttributeDefinitionsDto>.Fail(ServiceErrorCodes.Validation, failure.Message); }
    }

    /// <inheritdoc />
    public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId,
        SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.BusinessBlocked,
            "产品标签已改为共享定义及分类继承，请升级客户端后在产品标签页面维护。"));

    /// <inheritdoc />
    public async Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(
        SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var projection = currentUser.IsAuthenticated && userId != Guid.Empty
            ? await permissions.GetProjectionAsync(userId, cancellationToken) : null;
        if (projection is null) return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效。");
        if (!projection.IsAdmin && !projection.Permissions.Contains(PermissionKeys.ProductsCategoryEdit, StringComparer.Ordinal))
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Forbidden, "缺少产品分类编辑权限。");
        if (request is null) return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Validation, "请提交标签定义变更。");
        if (context.Session.Transaction is not null)
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已有未完成事务。");
        try
        {
            Normalize(request);
            if (!request.GroupChanges.HasChanges && !request.OptionChanges.HasChanges && !request.CategoryChanges.HasChanges)
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "没有需要保存的标签定义变更。");
            cancellationToken.ThrowIfCancellationRequested();
            context.Session.Transaction = context.Session.Connection.BeginTransaction();
            // 只读定位父身份；关联变化先分类再组，纯定义不反向取分类锁。
            var initialGroups = (await repository.GetGroupsAsync(null, false, cancellationToken)).ToDictionary(row => row.Uid);
            var initialOptions = (await repository.GetOptionsAsync(initialGroups.Keys, false, cancellationToken)).ToDictionary(row => row.Uid);
            var initialRelations = (await repository.GetRelationsAsync(null, false, cancellationToken)).ToDictionary(row => row.Uid);
            var parents = await LockRelationCategoriesAsync(request, initialRelations, cancellationToken);
            var groupIds = GetTouchedGroupIds(request, initialOptions, initialRelations);
            await repository.LockGroupsForUpdateAsync(groupIds, cancellationToken);
            var allGroups = (await repository.GetGroupsAsync(null, false, cancellationToken)).ToDictionary(row => row.Uid);
            var options = (await repository.GetOptionsAsync(groupIds, true, cancellationToken)).ToDictionary(row => row.Uid);
            var relations = (await repository.GetRelationsAsync(groupIds, true, cancellationToken)).ToDictionary(row => row.Uid);
            var prepared = await PrepareAsync(request, allGroups, options, relations, parents, cancellationToken);
            var response = await WriteAsync(request, prepared, cancellationToken);
            response.Definitions = await repository.GetSnapshotAsync(null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            context.Session.Transaction.Commit();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(response, "标签定义已保存。");
        }
        catch (OperationCanceledException) { Rollback(); throw; }
        catch (DefinitionFailure failure) { Rollback(); return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(failure.Code, failure.Message); }
        catch (Exception exception) when (context.Session.Errors.Classify(exception) == DatabaseFailureKind.UniqueConflict)
        {
            Rollback();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Validation, "组编码、组内选项编码或分类关联重复，请刷新后检查。");
        }
        catch (Exception exception) when (context.Session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure)
        {
            Rollback();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.VersionConflict, "分类或标签定义正在被其他用户修改，请刷新后重试。");
        }
        catch (Exception exception)
        {
            Rollback();
            _logger.LogError(exception, "共享标签定义保存失败，整页变更已回滚");
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Unexpected, "标签定义保存失败，所有变更已回滚。");
        }
        finally { context.Session.Transaction?.Dispose(); context.Session.Transaction = null; }
    }

    /// <summary>关联先锁来源分类；与分类移动全树锁及产品祖先路径锁形成一致串行边界。</summary>
    private async Task<IReadOnlyDictionary<Guid, Guid?>> LockRelationCategoriesAsync(SaveProductAttributeDefinitionsRequest request,
        IReadOnlyDictionary<Guid, ProductCategoryAttributeGroup> initialRelations, CancellationToken cancellationToken)
    {
        if (!request.CategoryChanges.HasChanges) return new Dictionary<Guid, Guid?>();
        ValidateTargets(request.CategoryChanges, initialRelations.ToDictionary(row => row.Key, row => row.Value.Version));
        var ids = request.CategoryChanges.CreateItems.Select(row => row.Data.CategoryId)
            .Concat(request.CategoryChanges.DeleteItems.Select(row => initialRelations[row.Uid].CategoryId)).Distinct().OrderBy(id => id).ToArray();
        if (ids.Contains(Guid.Empty)) throw new DefinitionFailure(ServiceErrorCodes.Validation, "关联分类身份不能为空。");
        var before = (await categories.GetAllAsync()).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid, row => row.ParentId);
        try { foreach (var id in ids) ProductAttributeApplicability.GetAncestorIds(id, before); }
        catch (InvalidOperationException failure) { throw new DefinitionFailure(ServiceErrorCodes.Validation, failure.Message); }
        cancellationToken.ThrowIfCancellationRequested();
        // 通用批量读取没有 ORDER BY，逐个取得已排序身份保证实际取锁顺序。
        foreach (var id in ids)
            if (await categories.GetByIdAsync(id, true) is not { IsDeleted: false })
                throw new DefinitionFailure(ServiceErrorCodes.VersionConflict, "关联分类已变化，请刷新后重试。");
        var after = (await categories.GetAllAsync()).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid, row => row.ParentId);
        // 等待期间层级变化时拒绝过期来源，不能再追加逆序父锁。
        foreach (var id in ids)
        {
            try
            {
                if (!ProductAttributeApplicability.GetAncestorIds(id, before).SequenceEqual(ProductAttributeApplicability.GetAncestorIds(id, after)))
                    throw new DefinitionFailure(ServiceErrorCodes.VersionConflict, "分类层级已变化，请刷新后重试。");
            }
            catch (InvalidOperationException failure) { throw new DefinitionFailure(ServiceErrorCodes.VersionConflict, failure.Message); }
        }
        return after;
    }

    /// <summary>从子项定位聚合父组；缺失子项不能以空组锁绕过验证。</summary>
    private static IReadOnlyList<Guid> GetTouchedGroupIds(SaveProductAttributeDefinitionsRequest request,
        IReadOnlyDictionary<Guid, ProductAttributeOption> options, IReadOnlyDictionary<Guid, ProductCategoryAttributeGroup> relations)
    {
        var ids = request.GroupChanges.UpdateItems.Select(row => row.Uid).Concat(request.GroupChanges.DeleteItems.Select(row => row.Uid)).ToHashSet();
        foreach (var item in request.OptionChanges.CreateItems) ids.Add(item.Data.AttributeGroupId);
        foreach (var id in request.OptionChanges.UpdateItems.Select(row => row.Uid).Concat(request.OptionChanges.DeleteItems.Select(row => row.Uid)))
        {
            if (!options.TryGetValue(id, out var option)) throw new DefinitionFailure(ServiceErrorCodes.Validation, "修改或删除的选项不存在或已删除。");
            ids.Add(option.AttributeGroupId);
        }
        foreach (var item in request.CategoryChanges.CreateItems) ids.Add(item.Data.AttributeGroupId);
        foreach (var item in request.CategoryChanges.DeleteItems)
        {
            if (!relations.TryGetValue(item.Uid, out var relation)) throw new DefinitionFailure(ServiceErrorCodes.Validation, "删除的分类关联不存在或已删除。");
            ids.Add(relation.AttributeGroupId);
        }
        ids.ExceptWith(request.GroupChanges.CreateItems.Select(row => row.Data.ClientUid));
        return ids.OrderBy(id => id).ToArray();
    }

    /// <summary>在稳定组锁内构建最终图，统一检查聚合版本、编码、引用及选择适用性。</summary>
    private async Task<PreparedDefinitions> PrepareAsync(SaveProductAttributeDefinitionsRequest request,
        Dictionary<Guid, ProductAttributeGroup> existingGroups, Dictionary<Guid, ProductAttributeOption> existingOptions,
        Dictionary<Guid, ProductCategoryAttributeGroup> existingRelations, IReadOnlyDictionary<Guid, Guid?> parents, CancellationToken cancellationToken)
    {
        ValidateTargets(request.GroupChanges, existingGroups.ToDictionary(row => row.Key, row => row.Value.Version));
        ValidateTargets(request.OptionChanges, existingOptions.ToDictionary(row => row.Key, row => row.Value.Version));
        ValidateTargets(request.CategoryChanges, existingRelations.ToDictionary(row => row.Key, row => row.Value.Version));
        var touched = GetTouchedGroupIds(request, existingOptions, existingRelations);
        var updatedOrDeleted = request.GroupChanges.UpdateItems.Select(row => row.Uid).Concat(request.GroupChanges.DeleteItems.Select(row => row.Uid)).ToHashSet();
        if (touched.Any(id => !updatedOrDeleted.Contains(id)))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "修改选项或分类关联必须同时提交所属标签组的当前版本。");
        var groups = existingGroups.ToDictionary(row => row.Key, row => row.Value.Adapt<ProductAttributeGroup>());
        var options = existingOptions.ToDictionary(row => row.Key, row => row.Value.Adapt<ProductAttributeOption>());
        var relations = existingRelations.ToDictionary(row => row.Key, row => row.Value.Adapt<ProductCategoryAttributeGroup>());
        var removedGroups = request.GroupChanges.DeleteItems.Select(row => row.Uid).ToHashSet();
        var removedOptions = request.OptionChanges.DeleteItems.Select(row => row.Uid)
            .Concat(existingOptions.Values.Where(row => removedGroups.Contains(row.AttributeGroupId)).Select(row => row.Uid)).ToHashSet();
        var removedRelations = request.CategoryChanges.DeleteItems.Select(row => row.Uid)
            .Concat(existingRelations.Values.Where(row => removedGroups.Contains(row.AttributeGroupId)).Select(row => row.Uid)).ToHashSet();
        var referenced = await repository.CountReferencedMastersAsync(removedOptions, cancellationToken);
        if (referenced > 0) throw new DefinitionFailure(ServiceErrorCodes.BusinessBlocked, $"删除失败：标签仍被 {referenced} 个主型号使用，请先在详情中解除选择。");
        foreach (var id in removedGroups) groups.Remove(id);
        foreach (var id in removedOptions) options.Remove(id);
        foreach (var id in removedRelations) relations.Remove(id);
        foreach (var item in request.GroupChanges.CreateItems)
        {
            if (item.Data.ClientUid == Guid.Empty || existingGroups.ContainsKey(item.Data.ClientUid) || groups.ContainsKey(item.Data.ClientUid))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "新增组临时标识为空、重复或与已有组冲突。");
            var group = item.Data.Adapt<ProductAttributeGroup>();
            group.Uid = item.Data.ClientUid;
            groups.Add(group.Uid, group);
        }
        foreach (var item in request.GroupChanges.UpdateItems)
        {
            var group = groups[item.Uid];
            item.Data.Adapt(group);
            if (group.SelectionMode == ProductAttributeSelectionMode.Single && await repository.CountMultipleSelectionsAsync(group.Uid, cancellationToken) > 0)
                throw new DefinitionFailure(ServiceErrorCodes.BusinessBlocked, "该组仍有主型号选择了多个值，请先调整产品选择再改为单选。");
        }
        foreach (var item in request.OptionChanges.UpdateItems)
        {
            var existing = existingOptions[item.Uid];
            if (item.Data.AttributeGroupId != existing.AttributeGroupId || !groups.ContainsKey(existing.AttributeGroupId))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "选项不能迁移到另一组，也不能修改被删除组的选项。");
            item.Data.Adapt(options[item.Uid]);
        }
        var createdOptions = request.OptionChanges.CreateItems.Select(row => row.Data.Adapt<ProductAttributeOption>()).ToArray();
        if (createdOptions.Any(row => !groups.ContainsKey(row.AttributeGroupId)))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "新增选项的组不存在或已删除。");
        var createdRelations = request.CategoryChanges.CreateItems.Select(row => row.Data.Adapt<ProductCategoryAttributeGroup>()).ToArray();
        if (createdRelations.Any(row => !groups.ContainsKey(row.AttributeGroupId)))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "关联的标签组不存在或已删除。");
        if (groups.Values.GroupBy(row => row.GroupCode, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)
            || options.Values.Concat(createdOptions).GroupBy(row => row.AttributeGroupId)
                .Any(group => group.GroupBy(row => row.OptionCode, StringComparer.OrdinalIgnoreCase).Any(code => code.Count() > 1)))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "全局组编码或同一组的选项编码重复。");
        if (relations.Values.Concat(createdRelations).GroupBy(row => (row.CategoryId, row.AttributeGroupId)).Any(group => group.Count() > 1))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "同一分类不能重复关联同一标签组。");
        if (request.CategoryChanges.HasChanges)
        {
            var remaining = relations.Values.Concat(createdRelations).Adapt<List<ProductCategoryAttributeGroupDto>>();
            var relationGroupIds = request.CategoryChanges.DeleteItems.Select(row => existingRelations[row.Uid].AttributeGroupId).Distinct().ToArray();
            var references = await repository.GetSelectedReferencesAsync(relationGroupIds, cancellationToken);
            var invalid = ProductAttributeApplicability.CountUnavailableMasters(parents, remaining, references);
            if (invalid > 0) throw new DefinitionFailure(ServiceErrorCodes.BusinessBlocked,
                $"分类关联调整会使 {invalid} 个主型号的已有标签失去适用性，请先在产品详情处理这些选择。");
        }
        return new(groups, options, existingOptions.Values.Where(row => removedOptions.Contains(row.Uid)).ToArray(),
            existingRelations.Values.Where(row => removedRelations.Contains(row.Uid)).ToArray());
    }

    /// <summary>映射临时父身份并写入已验证实体；逐项成功回执只在整体提交后返回。</summary>
    private async Task<SaveProductAttributeDefinitionsResponse> WriteAsync(SaveProductAttributeDefinitionsRequest request,
        PreparedDefinitions prepared, CancellationToken cancellationToken)
    {
        var response = new SaveProductAttributeDefinitionsResponse();
        var groupMap = new Dictionary<Guid, Guid>();
        foreach (var relation in prepared.DeletedRelations.OrderBy(row => row.Uid))
        {
            var write = await repository.DeleteRelationAsync(relation.Uid, relation.Version);
            RequireWrite(write);
            var item = request.CategoryChanges.DeleteItems.FirstOrDefault(row => row.Uid == relation.Uid);
            if (item is not null) response.CategoryResults.Items.Add(SaveItemResultFactory.FromDelete(item, write));
        }
        foreach (var option in prepared.DeletedOptions.OrderBy(row => row.Uid))
        {
            var write = await repository.DeleteOptionAsync(option.Uid, option.Version);
            RequireWrite(write);
            var item = request.OptionChanges.DeleteItems.FirstOrDefault(row => row.Uid == option.Uid);
            if (item is not null) response.OptionResults.Items.Add(SaveItemResultFactory.FromDelete(item, write));
        }
        foreach (var item in request.GroupChanges.DeleteItems.OrderBy(row => row.Uid))
        {
            var write = await repository.DeleteGroupAsync(item.Uid, item.Version);
            RequireWrite(write);
            response.GroupResults.Items.Add(SaveItemResultFactory.FromDelete(item, write));
        }
        foreach (var item in request.GroupChanges.UpdateItems.OrderBy(row => row.Uid))
        {
            var write = await repository.UpdateGroupAsync(prepared.Groups[item.Uid]);
            RequireWrite(write);
            // 请求版本才是本次提交前版本；通用仓储会原地推进实体版本，不能用于回执旧版本。
            var receipt = SaveItemResultFactory.FromUpdate(item, write);
            receipt.OldVersion = item.Version;
            response.GroupResults.Items.Add(receipt);
        }
        foreach (var item in request.GroupChanges.CreateItems)
        {
            var write = await repository.AddGroupAsync(prepared.Groups[item.Data.ClientUid]);
            RequireWrite(write);
            groupMap.Add(item.Data.ClientUid, write.Uid);
            response.GroupResults.Items.Add(SaveItemResultFactory.FromCreate(item, write));
        }
        foreach (var item in request.OptionChanges.UpdateItems.OrderBy(row => row.Uid))
        {
            var write = await repository.UpdateOptionAsync(prepared.Options[item.Uid]);
            RequireWrite(write);
            var receipt = SaveItemResultFactory.FromUpdate(item, write);
            receipt.OldVersion = item.Version;
            response.OptionResults.Items.Add(receipt);
        }
        foreach (var item in request.OptionChanges.CreateItems)
        {
            var entity = item.Data.Adapt<ProductAttributeOption>();
            if (groupMap.TryGetValue(entity.AttributeGroupId, out var actualId)) entity.AttributeGroupId = actualId;
            var write = await repository.AddOptionAsync(entity);
            RequireWrite(write);
            response.OptionResults.Items.Add(SaveItemResultFactory.FromCreate(item, write));
        }
        foreach (var item in request.CategoryChanges.CreateItems)
        {
            var entity = item.Data.Adapt<ProductCategoryAttributeGroup>();
            if (groupMap.TryGetValue(entity.AttributeGroupId, out var actualId)) entity.AttributeGroupId = actualId;
            var write = await repository.AddRelationAsync(entity);
            RequireWrite(write);
            response.CategoryResults.Items.Add(SaveItemResultFactory.FromCreate(item, write));
        }
        cancellationToken.ThrowIfCancellationRequested();
        response.GroupResults.TotalCount = request.GroupChanges.TotalCount;
        response.OptionResults.TotalCount = request.OptionChanges.TotalCount;
        response.CategoryResults.TotalCount = request.CategoryChanges.TotalCount;
        return response;
    }

    /// <summary>校验有效目标、重复提交和逐项版本；组版本覆盖子项及关联的聚合变化。</summary>
    private static void ValidateTargets<T>(BatchSaveRequestBase<T> changes, IReadOnlyDictionary<Guid, int> versions)
    {
        var ids = changes.UpdateItems.Select(row => row.Uid).Concat(changes.DeleteItems.Select(row => row.Uid)).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new DefinitionFailure(ServiceErrorCodes.Validation, "同一标签记录不能重复提交或同时修改和删除。");
        foreach (var item in changes.UpdateItems.Select(row => (row.Uid, row.Version)).Concat(changes.DeleteItems.Select(row => (row.Uid, row.Version))))
        {
            if (item.Uid == Guid.Empty || item.Version <= 0 || !versions.TryGetValue(item.Uid, out var version))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "修改或删除的标签记录不存在或已删除。");
            if (version != item.Version) throw new DefinitionFailure(ServiceErrorCodes.VersionConflict, "标签定义已被其他用户修改，请刷新后重试。");
        }
    }

    /// <summary>规范化包装并检查文本、枚举及不可变关联；不参与产品选择清空协议。</summary>
    private static void Normalize(SaveProductAttributeDefinitionsRequest request)
    {
        request.GroupChanges ??= new(); request.OptionChanges ??= new(); request.CategoryChanges ??= new();
        NormalizeItems(request.GroupChanges); NormalizeItems(request.OptionChanges); NormalizeItems(request.CategoryChanges);
        if (request.CategoryChanges.UpdateItems.Count > 0) throw new DefinitionFailure(ServiceErrorCodes.Validation, "分类关联不能修改身份，请使用新增或删除。");
        foreach (var data in request.GroupChanges.CreateItems.Select(row => row.Data).Concat(request.GroupChanges.UpdateItems.Select(row => row.Data)))
        {
            data.GroupCode = Text(data.GroupCode, 100, "组编码"); data.GroupName = Text(data.GroupName, 200, "组名称");
            if (data.SelectionMode is not (ProductAttributeSelectionMode.Single or ProductAttributeSelectionMode.Multiple))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "标签组选择方式无效。");
        }
        foreach (var data in request.OptionChanges.CreateItems.Select(row => row.Data).Concat(request.OptionChanges.UpdateItems.Select(row => row.Data)))
        { data.OptionCode = Text(data.OptionCode, 100, "选项编码"); data.OptionName = Text(data.OptionName, 200, "选项名称"); }
    }

    /// <summary>拒绝空变更项，避免无效请求体被当作没有变更。</summary>
    private static void NormalizeItems<T>(BatchSaveRequestBase<T> changes) where T : class
    {
        changes.CreateItems ??= []; changes.UpdateItems ??= []; changes.DeleteItems ??= [];
        if (changes.CreateItems.Any(row => row?.Data is null) || changes.UpdateItems.Any(row => row?.Data is null) || changes.DeleteItems.Any(row => row is null))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "标签变更不能包含空项。");
    }

    /// <summary>规范文本首尾空白并保证实体长度边界。</summary>
    private static string Text(string? value, int limit, string name)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > limit) throw new DefinitionFailure(ServiceErrorCodes.Validation, $"{name}不能为空且不能超过 {limit} 个字符。");
        return text;
    }

    /// <summary>基础仓储失败转换为业务协议，底层异常不泄漏给调用方。</summary>
    private static void RequireWrite(RepositoryWriteResult result)
    {
        if (!result.Success) throw new DefinitionFailure(result.ErrorCode == ServiceErrorCodes.ConcurrencyConflict ? ServiceErrorCodes.VersionConflict
            : result.ErrorCode ?? ServiceErrorCodes.BusinessBlocked, result.Message ?? "标签写入失败。");
    }

    /// <summary>回滚失败只记录，不覆盖业务失败或用户取消。</summary>
    private void Rollback()
    { try { context.Session.Transaction?.Rollback(); } catch (Exception exception) { _logger.LogError(exception, "标签定义回滚失败"); } }

    /// <summary>验证完成的最终实体图与隐式级联删除池。</summary>
    private sealed record PreparedDefinitions(Dictionary<Guid, ProductAttributeGroup> Groups,
        Dictionary<Guid, ProductAttributeOption> Options, IReadOnlyList<ProductAttributeOption> DeletedOptions,
        IReadOnlyList<ProductCategoryAttributeGroup> DeletedRelations);
    /// <summary>定义用例内部的可控失败，不承载存储异常。</summary>
    private sealed class DefinitionFailure(string code, string message) : Exception(message)
    { internal string Code { get; } = code; }
}
