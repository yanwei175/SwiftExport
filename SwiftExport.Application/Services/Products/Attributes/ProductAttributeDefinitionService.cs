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
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Categories;

namespace SwiftExport.Application.Services.Products.Attributes;

/// <summary>只维护分类内标签组及选项；分类父锁保护定义与主型号选择的一致性。</summary>
public sealed class ProductAttributeDefinitionService(IProductAttributeRepository repository,
    IProductCategoryRepository categories, ICurrentUser currentUser, IPermissionEvaluationService permissions,
    ServiceBaseContext context) : IProductAttributeDefinitionService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<ProductAttributeDefinitionService>();

    /// <inheritdoc />
    public async Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken = default)
    {
        if (categoryId.HasValue && (categoryId == Guid.Empty || await categories.GetByIdAsync(categoryId.Value) is not { IsDeleted: false }))
            return ServiceResult<ProductAttributeDefinitionsDto>.Fail(ServiceErrorCodes.Validation, "产品分类不存在或已删除。");
        return ServiceResult<ProductAttributeDefinitionsDto>.Ok(await ReadDefinitionsAsync(categoryId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var projection = currentUser.IsAuthenticated && userId != Guid.Empty
            ? await permissions.GetProjectionAsync(userId, cancellationToken) : null;
        if (projection is null) return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效。");
        if (!projection.IsAdmin && !projection.Permissions.Contains(PermissionKeys.ProductsCategoryEdit, StringComparer.Ordinal))
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Forbidden, "缺少产品分类编辑权限。");
        if (categoryId == Guid.Empty || request is null)
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Validation, "请选择分类并提交标签定义变更。");
        if (context.Session.Transaction is not null)
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已有未完成事务。");

        try
        {
            Normalize(request);
            if (!request.GroupChanges.HasChanges && !request.OptionChanges.HasChanges)
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "没有需要保存的标签定义变更。");
            cancellationToken.ThrowIfCancellationRequested();
            context.Session.Transaction = context.Session.Connection.BeginTransaction();
            var category = await categories.GetByIdAsync(categoryId, true);
            if (category is null || category.IsDeleted) throw new DefinitionFailure(ServiceErrorCodes.Validation, "产品分类不存在或已删除。");
            var groups = (await repository.GetGroupsAsync(categoryId, true, cancellationToken)).ToDictionary(row => row.Uid);
            var options = (await repository.GetOptionsAsync(groups.Keys, true, cancellationToken)).ToDictionary(row => row.Uid);
            var prepared = await PrepareAsync(categoryId, request, groups, options, cancellationToken);
            var response = new SaveProductAttributeDefinitionsResponse();
            var groupMap = new Dictionary<Guid, Guid>();

            // 先移除本批删除值，允许在同一最终图中复用编码；组删除的子项只生成一次关联效果。
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
                response.GroupResults.Items.Add(SaveItemResultFactory.FromUpdate(item, write));
            }
            foreach (var item in request.GroupChanges.CreateItems)
            {
                var entity = prepared.Groups[item.Data.ClientUid];
                var write = await repository.AddGroupAsync(entity);
                RequireWrite(write);
                groupMap.Add(item.Data.ClientUid, write.Uid);
                response.GroupResults.Items.Add(SaveItemResultFactory.FromCreate(item, write));
            }
            foreach (var item in request.OptionChanges.UpdateItems.OrderBy(row => row.Uid))
            {
                var write = await repository.UpdateOptionAsync(prepared.Options[item.Uid]);
                RequireWrite(write);
                response.OptionResults.Items.Add(SaveItemResultFactory.FromUpdate(item, write));
            }
            foreach (var item in request.OptionChanges.CreateItems)
            {
                var entity = item.Data.Adapt<ProductAttributeOption>();
                if (groupMap.TryGetValue(entity.AttributeGroupId, out var actualId)) entity.AttributeGroupId = actualId;
                var write = await repository.AddOptionAsync(entity);
                RequireWrite(write);
                response.OptionResults.Items.Add(SaveItemResultFactory.FromCreate(item, write));
            }
            response.GroupResults.TotalCount = request.GroupChanges.TotalCount;
            response.OptionResults.TotalCount = request.OptionChanges.TotalCount;
            response.Definitions = await ReadDefinitionsAsync(categoryId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            context.Session.Transaction.Commit();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(response, "标签定义已保存。");
        }
        catch (OperationCanceledException) { Rollback(); throw; }
        catch (DefinitionFailure failure) { Rollback(); return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(failure.Code, failure.Message); }
        catch (Exception exception)
        {
            Rollback();
            _logger.LogError(exception, "标签定义保存失败，整个分类变更已回滚");
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail(ServiceErrorCodes.Unexpected, "标签定义保存失败，所有变更已回滚。");
        }
        finally { context.Session.Transaction?.Dispose(); context.Session.Transaction = null; }
    }

    /// <summary>构建最终定义图后验证归属、版本、编码、引用和单选基数；写入前完成所有业务检查。</summary>
    private async Task<PreparedDefinitions> PrepareAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request,
        Dictionary<Guid, ProductAttributeGroup> existingGroups, Dictionary<Guid, ProductAttributeOption> existingOptions, CancellationToken cancellationToken)
    {
        ValidateTargets(request.GroupChanges, existingGroups.ToDictionary(row => row.Key, row => row.Value.Version));
        ValidateTargets(request.OptionChanges, existingOptions.ToDictionary(row => row.Key, row => row.Value.Version));
        var groups = existingGroups.ToDictionary(row => row.Key, row => row.Value.Adapt<ProductAttributeGroup>());
        var options = existingOptions.ToDictionary(row => row.Key, row => row.Value.Adapt<ProductAttributeOption>());
        var removedGroupIds = request.GroupChanges.DeleteItems.Select(row => row.Uid).ToHashSet();
        var removedOptionIds = request.OptionChanges.DeleteItems.Select(row => row.Uid)
            .Concat(existingOptions.Values.Where(row => removedGroupIds.Contains(row.AttributeGroupId)).Select(row => row.Uid)).ToHashSet();
        var referenced = await repository.CountReferencedMastersAsync(removedOptionIds, cancellationToken);
        if (referenced > 0) throw new DefinitionFailure(ServiceErrorCodes.BusinessBlocked, $"删除失败：标签仍被 {referenced} 个主型号使用，请先在详情中解除选择。");
        foreach (var id in removedGroupIds) groups.Remove(id);
        foreach (var id in removedOptionIds) options.Remove(id);
        var allGroupIds = request.GroupChanges.CreateItems.Count == 0 ? new HashSet<Guid>()
            : (await repository.GetGroupsAsync(null, false, cancellationToken)).Select(row => row.Uid).ToHashSet();
        foreach (var item in request.GroupChanges.CreateItems)
        {
            if (item.Data.ClientUid == Guid.Empty || allGroupIds.Contains(item.Data.ClientUid) || groups.ContainsKey(item.Data.ClientUid))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "新增组临时标识为空、重复或与已有组冲突。");
            var group = item.Data.Adapt<ProductAttributeGroup>();
            group.Uid = item.Data.ClientUid;
            group.CategoryId = categoryId;
            groups.Add(group.Uid, group);
        }
        foreach (var item in request.GroupChanges.UpdateItems)
        {
            var group = groups[item.Uid];
            item.Data.Adapt(group);
            if (group.SelectionMode == ProductAttributeSelectionMode.Single
                && await repository.CountMultipleSelectionsAsync(group.Uid, cancellationToken) > 0)
                throw new DefinitionFailure(ServiceErrorCodes.BusinessBlocked, "该组仍有主型号选择了多个值，请先调整产品选择再改为单选。");
        }
        foreach (var item in request.OptionChanges.UpdateItems)
        {
            var existing = existingOptions[item.Uid];
            if (item.Data.AttributeGroupId != existing.AttributeGroupId || !groups.ContainsKey(existing.AttributeGroupId))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "选项不能迁移到另一组，也不能修改被删除组的选项。");
            item.Data.Adapt(options[item.Uid]);
        }
        var createdOptions = new List<ProductAttributeOption>();
        foreach (var item in request.OptionChanges.CreateItems)
        {
            if (!groups.ContainsKey(item.Data.AttributeGroupId)) throw new DefinitionFailure(ServiceErrorCodes.Validation, "新增选项的组不属于当前分类或已删除。");
            createdOptions.Add(item.Data.Adapt<ProductAttributeOption>());
        }
        if (groups.Values.GroupBy(row => row.GroupCode, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)
            || options.Values.Concat(createdOptions).GroupBy(row => row.AttributeGroupId)
                .Any(group => group.GroupBy(row => row.OptionCode, StringComparer.OrdinalIgnoreCase).Any(code => code.Count() > 1)))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "同一分类的组编码或同一组的选项编码重复。");
        return new(groups, options, existingOptions.Values.Where(row => removedOptionIds.Contains(row.Uid)).ToArray());
    }

    /// <summary>有效目标必须在当前分类图中，重复、修改删除重叠及旧版本均拒绝。</summary>
    private static void ValidateTargets<T>(BatchSaveRequestBase<T> changes, IReadOnlyDictionary<Guid, int> versions)
    {
        var ids = changes.UpdateItems.Select(row => row.Uid).Concat(changes.DeleteItems.Select(row => row.Uid)).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new DefinitionFailure(ServiceErrorCodes.Validation, "同一标签记录不能重复提交或同时修改和删除。");
        foreach (var item in changes.UpdateItems.Select(row => (row.Uid, row.Version)).Concat(changes.DeleteItems.Select(row => (row.Uid, row.Version))))
        {
            if (item.Uid == Guid.Empty || item.Version <= 0 || !versions.TryGetValue(item.Uid, out var version))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "修改或删除的标签不属于当前分类或已删除。");
            if (version != item.Version) throw new DefinitionFailure(ServiceErrorCodes.VersionConflict, "标签定义已被其他用户修改，请刷新后重试。");
        }
    }

    /// <summary>规范化输入并检查文本及枚举，ClientUid 与选择 null 协议分别处理。</summary>
    private static void Normalize(SaveProductAttributeDefinitionsRequest request)
    {
        request.GroupChanges ??= new(); request.OptionChanges ??= new();
        request.GroupChanges.CreateItems ??= []; request.GroupChanges.UpdateItems ??= []; request.GroupChanges.DeleteItems ??= [];
        request.OptionChanges.CreateItems ??= []; request.OptionChanges.UpdateItems ??= []; request.OptionChanges.DeleteItems ??= [];
        if (request.GroupChanges.CreateItems.Any(row => row?.Data is null) || request.GroupChanges.UpdateItems.Any(row => row?.Data is null)
            || request.OptionChanges.CreateItems.Any(row => row?.Data is null) || request.OptionChanges.UpdateItems.Any(row => row?.Data is null)
            || request.GroupChanges.DeleteItems.Any(row => row is null) || request.OptionChanges.DeleteItems.Any(row => row is null))
            throw new DefinitionFailure(ServiceErrorCodes.Validation, "标签变更不能包含空项。");
        foreach (var data in request.GroupChanges.CreateItems.Select(row => row.Data).Concat(request.GroupChanges.UpdateItems.Select(row => row.Data)))
        {
            data.GroupCode = Text(data.GroupCode, 100, "组编码"); data.GroupName = Text(data.GroupName, 200, "组名称");
            if (data.SelectionMode is not (ProductAttributeSelectionMode.Single or ProductAttributeSelectionMode.Multiple))
                throw new DefinitionFailure(ServiceErrorCodes.Validation, "标签组选择方式无效。");
        }
        foreach (var data in request.OptionChanges.CreateItems.Select(row => row.Data).Concat(request.OptionChanges.UpdateItems.Select(row => row.Data)))
        { data.OptionCode = Text(data.OptionCode, 100, "选项编码"); data.OptionName = Text(data.OptionName, 200, "选项名称"); }
    }

    /// <summary>标签显示和编码均规范首尾空白，不接受空值或超出实体长度的文本。</summary>
    private static string Text(string? value, int limit, string name)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > limit) throw new DefinitionFailure(ServiceErrorCodes.Validation, $"{name}不能为空且不能超过 {limit} 个字符。");
        return text;
    }

    /// <summary>读取完整快照，查询仓储以单条联表语句保持组与选项一致。</summary>
    private async Task<ProductAttributeDefinitionsDto> ReadDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken) => new()
    { CategoryId = categoryId ?? Guid.Empty, Groups = (await repository.GetDefinitionsAsync(categoryId, cancellationToken)).ToList() };

    /// <summary>基础仓储并发结果转换为用例协议，不泄漏底层 SQL 或数据库错误。</summary>
    private static void RequireWrite(RepositoryWriteResult result)
    {
        if (!result.Success) throw new DefinitionFailure(result.ErrorCode == ServiceErrorCodes.ConcurrencyConflict ? ServiceErrorCodes.VersionConflict
            : result.ErrorCode ?? ServiceErrorCodes.BusinessBlocked, result.Message ?? "标签写入失败。");
    }

    /// <summary>回滚失败只记录，不覆盖业务失败或用户取消。</summary>
    private void Rollback()
    { try { context.Session.Transaction?.Rollback(); } catch (Exception exception) { _logger.LogError(exception, "标签定义回滚失败"); } }

    private sealed record PreparedDefinitions(Dictionary<Guid, ProductAttributeGroup> Groups,
        Dictionary<Guid, ProductAttributeOption> Options, IReadOnlyList<ProductAttributeOption> DeletedOptions);
    private sealed class DefinitionFailure(string code, string message) : Exception(message)
    { internal string Code { get; } = code; }
}
