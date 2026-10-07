using SwiftExport.Contracts.DTOs.Errors;
using System.Data;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.QualityControl.Categories;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.QualityControl.Categories;

namespace SwiftExport.Application.Services.QualityControl.Categories;

/// <summary>售后品质分类整树维护；独占本用例事务，以版本、最终图和引用检查保证整批原子性。</summary>
public sealed class AfterSalesQualityCategoryService(
    IAfterSalesQualityCategoryRepository repository, ServiceBaseContext context,
    IPermissionEvaluationService permissions, ICurrentUser user) : IAfterSalesQualityCategoryService
{
    private readonly IDbSession _session = context.Session;
    private readonly ILogger<AfterSalesQualityCategoryService> _logger = context.LoggerFactory.CreateLogger<AfterSalesQualityCategoryService>();

    /// <summary>按查看权限返回全部未删节点，空树返回成功空集合，停用节点仍可显示。</summary>
    public async Task<ServiceResult<List<AfterSalesQualityCategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!await AuthorizedAsync(PermissionKeys.QualityControlAfterSalesView, cancellationToken))
            return ServiceResult<List<AfterSalesQualityCategoryDto>>.Fail(ServiceErrorCodes.Forbidden, "当前账号无售后查看权限。");
        var entities = await repository.ReadAllAsync(false, cancellationToken);
        return ServiceResult<List<AfterSalesQualityCategoryDto>>.Ok(entities.Adapt<List<AfterSalesQualityCategoryDto>>());
    }

    /// <summary>整批校验最终树再按拓扑顺序写入；Serializable 防止并发新增的编码、父节点和引用幻读。</summary>
    public async Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>> BatchSaveAsync(
        AfterSalesQualityCategoryBatchSaveRequest batchReq, CancellationToken cancellationToken = default)
    {
        if (!await AuthorizedAsync(PermissionKeys.AfterSalesMaintain, cancellationToken))
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(ServiceErrorCodes.Forbidden, "当前账号无售后维护权限。");
        if (batchReq is null) return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "请求不能为空。");
        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(x => x is null) || batchReq.UpdateItems.Any(x => x is null) || batchReq.DeleteItems.Any(x => x is null))
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "批量明细不能包含空项。");
        if (!batchReq.HasChanges) return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "没有需要保存的数据。");
        if (_session.Transaction is not null)
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(ServiceErrorCodes.BusinessBlocked, "不能接管外部事务。");

        var response = new AfterSalesQualityCategoryBatchSaveResponse { TotalCount = batchReq.TotalCount };
        IDbTransaction? owned = null;
        var committing = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            owned = _session.Connection.BeginTransaction(IsolationLevel.Serializable);
            _session.Transaction = owned;
            var preparation = await PrepareBatchSaveAsync(batchReq, cancellationToken);
            if (preparation.Error is not null) throw new CategoryWriteException(ServiceErrorCodes.Validation, preparation.Error);
            var ids = preparation.ExistingById.Keys.ToDictionary(uid => uid, uid => uid);
            foreach (var create in preparation.CreateItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await repository.AddAsync(BuildCreateEntity(create.Request.Data, ids));
                EnsureWriteSucceeded(result, "新增分类失败。");
                response.Items.Add(SaveItemResultFactory.FromCreate(create.Request, result));
                ids[create.LogicalUid] = result.Uid;
            }
            foreach (var item in batchReq.UpdateItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = preparation.ExistingById[item.Uid];
                ApplyDto(entity, item.Data, ids);
                entity.Version = item.Version;
                var result = await repository.UpdateAsync(entity);
                EnsureWriteSucceeded(result, "更新分类失败。");
                var receipt = SaveItemResultFactory.FromUpdate(item, result);
                receipt.OldVersion = item.Version;
                response.Items.Add(receipt);
            }
            foreach (var item in preparation.DeleteItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                EnsureWriteSucceeded(result, "删除分类失败。");
                response.Items.Add(SaveItemResultFactory.FromDelete(item, result));
            }
            cancellationToken.ThrowIfCancellationRequested();
            committing = true;
            owned.Commit();
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Ok(response);
        }
        catch (CategoryWriteException exception)
        {
            TryRollbackTransaction();
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(exception.Code, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !committing)
        {
            TryRollbackTransaction();
            throw;
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "售后品质分类整批保存失败。");
            var kind = _session.Errors.Classify(exception);
            var code = kind switch
            {
                DatabaseFailureKind.SerializationFailure or DatabaseFailureKind.Deadlock => ServiceErrorCodes.ServiceUnavailable,
                DatabaseFailureKind.UniqueConflict => ServiceErrorCodes.BusinessBlocked,
                _ when committing => ServiceErrorCodes.OutcomeUnknown,
                _ => ServiceErrorCodes.Unexpected
            };
            var message = code == ServiceErrorCodes.OutcomeUnknown ? "提交结果尚不确定，请刷新核实后再操作。"
                : code == ServiceErrorCodes.ServiceUnavailable ? "分类或引用发生并发变化，请刷新后重试。"
                : code == ServiceErrorCodes.BusinessBlocked ? "分类编码已被使用，请刷新后修改。" : "分类保存失败，所有未提交操作已回滚。";
            return ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail(code, message);
        }
        finally
        {
            // 仅释放本用例创建的事务，释放失败不能覆盖提交结果或原始错误。
            if (owned is not null)
            {
                _session.Transaction = null;
                try { owned.Dispose(); }
                catch (Exception exception) { _logger.LogError(exception, "售后分类事务释放失败。"); }
            }
        }
    }

    /// <summary>拒绝未认证账号，并沿用售后稳定权限键，不单独创建分类权限族。</summary>
    private Task<bool> AuthorizedAsync(string key, CancellationToken token) => user.IsAuthenticated && user.UserId.HasValue
        ? permissions.HasPermissionAsync(user.UserId.Value, key, token) : Task.FromResult(false);
    /// <summary>校验请求身份、版本和引用，构造最终树并计算新增与删除拓扑顺序。</summary>
    private async Task<AfterSalesQualityCategoryBatchPreparation> PrepareBatchSaveAsync(
        AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken)
    {
        var duplicateUpdateUid = FindDuplicateUid(request.UpdateItems.Select(item => item.Uid));
        if (duplicateUpdateUid.HasValue)
            return AfterSalesQualityCategoryBatchPreparation.Fail($"同一售后品质分类不能重复修改：{duplicateUpdateUid}。");
        var duplicateDeleteUid = FindDuplicateUid(request.DeleteItems.Select(item => item.Uid));
        if (duplicateDeleteUid.HasValue)
            return AfterSalesQualityCategoryBatchPreparation.Fail($"同一售后品质分类不能重复删除：{duplicateDeleteUid}。");
        var conflictingUid = request.UpdateItems.Select(item => item.Uid)
            .Intersect(request.DeleteItems.Select(item => item.Uid))
            .FirstOrDefault(uid => uid != Guid.Empty);
        if (conflictingUid != Guid.Empty)
            return AfterSalesQualityCategoryBatchPreparation.Fail($"同一售后品质分类不能同时修改和删除：{conflictingUid}。");

        var preparedCreates = new List<PreparedCreate>();
        var createIds = new HashSet<Guid>();
        foreach (var item in request.CreateItems)
        {
            var validationError = NormalizeAndValidateDto(item.Data, item.RowIndex, "新增");
            if (validationError is not null) return AfterSalesQualityCategoryBatchPreparation.Fail(validationError);

            var logicalUid = item.Data.Uid == Guid.Empty ? Guid.NewGuid() : item.Data.Uid;
            item.Data.Uid = logicalUid;
            if (!createIds.Add(logicalUid))
                return AfterSalesQualityCategoryBatchPreparation.Fail($"本次提交包含重复的新分类标识：{logicalUid}。");
            preparedCreates.Add(new PreparedCreate(item, logicalUid));
        }

        foreach (var item in request.UpdateItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return AfterSalesQualityCategoryBatchPreparation.Fail($"第 {FormatRowIndex(item.RowIndex)} 行：修改标识或版本无效。");
            var validationError = NormalizeAndValidateDto(item.Data, item.RowIndex, "修改");
            if (validationError is not null) return AfterSalesQualityCategoryBatchPreparation.Fail(validationError);
            item.Data.Uid = item.Uid;
            
        }
        foreach (var item in request.DeleteItems)
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return AfterSalesQualityCategoryBatchPreparation.Fail($"第 {FormatRowIndex(item.RowIndex)} 行：删除标识或版本无效。");

        var existing = await repository.ReadAllAsync(true, cancellationToken);
        var existingById = existing.ToDictionary(item => item.Uid);
        if (createIds.Any(existingById.ContainsKey))
            return AfterSalesQualityCategoryBatchPreparation.Fail("新增分类的临时标识与现有分类冲突，请重新加载后重试。");

        foreach (var item in request.UpdateItems)
        {
            if (!existingById.TryGetValue(item.Uid, out var entity))
                return AfterSalesQualityCategoryBatchPreparation.Fail($"修改失败：售后品质分类 {item.Uid} 不存在或已删除。");
            if (entity.Version != item.Version)
                throw new CategoryWriteException(ServiceErrorCodes.ConcurrencyConflict, $"售后品质分类 {item.Uid} 已被其他人修改，请刷新后重试。");
        }
        foreach (var item in request.DeleteItems)
        {
            if (!existingById.TryGetValue(item.Uid, out var entity))
                return AfterSalesQualityCategoryBatchPreparation.Fail($"删除失败：售后品质分类 {item.Uid} 不存在或已删除。");
            if (entity.Version != item.Version)
                throw new CategoryWriteException(ServiceErrorCodes.ConcurrencyConflict, $"售后品质分类 {item.Uid} 已被其他人修改，请刷新后重试。");
        }

        var deletedIds = request.DeleteItems.Select(item => item.Uid).ToHashSet();
        var referencedIds = await repository.GetReferencedCategoryIdsAsync(deletedIds, cancellationToken);
        if (referencedIds.Count > 0)
            return AfterSalesQualityCategoryBatchPreparation.Fail(
                "删除失败：以下售后品质分类仍被售后单引用：" + string.Join(", ", referencedIds));

        var finalNodes = existing
            .Where(entity => !deletedIds.Contains(entity.Uid))
            .ToDictionary(
                entity => entity.Uid,
                entity => CategoryNode.FromEntity(entity));
        foreach (var item in request.UpdateItems)
            finalNodes[item.Uid] = CategoryNode.FromDto(item.Uid, item.Data);
        foreach (var item in preparedCreates)
            finalNodes[item.LogicalUid] = CategoryNode.FromDto(item.LogicalUid, item.Request.Data);

        var graphError = ValidateFinalGraph(finalNodes);
        if (graphError is not null) return AfterSalesQualityCategoryBatchPreparation.Fail(graphError);

        var orderedCreates = OrderCreatesByParent(preparedCreates);
        var orderedDeletes = request.DeleteItems
            .OrderByDescending(item => GetDepth(existingById, item.Uid))
            .ToArray();
        return AfterSalesQualityCategoryBatchPreparation.Ok(existingById, orderedCreates, orderedDeletes);
    }

    /// <summary>规范编辑输入，空父标识视为根；名称必填、排序非负，编码和别名可空。</summary>
    private static string? NormalizeAndValidateDto(AfterSalesQualityCategoryEditDto? dto, int? rowIndex, string operation)
    {
        if (dto is null) return $"第 {FormatRowIndex(rowIndex)} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(dto.CategoryName))
            return $"第 {FormatRowIndex(rowIndex)} 行：分类名称不能为空。";
        if (dto.SortNo < 0)
            return $"第 {FormatRowIndex(rowIndex)} 行：排序号不能小于零。";

        dto.CategoryName = dto.CategoryName.Trim();
        dto.FakeName = dto.FakeName?.Trim() ?? string.Empty;
        dto.CategoryCode = dto.CategoryCode?.Trim() ?? string.Empty;
        dto.ParentId = NormalizeParentId(dto.ParentId);
        return null;
    }

    /// <summary>验证最终未删图的编码、上级和循环，不限制父级引用，也不级联停用。</summary>
    internal static string? ValidateFinalGraph(IReadOnlyDictionary<Guid, CategoryNode> nodes)
    {
        var duplicateCode = nodes.Values
            .Where(node => !string.IsNullOrWhiteSpace(node.CategoryCode))
            .GroupBy(node => node.CategoryCode!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateCode is not null) return $"分类编码不能重复：{duplicateCode}。";

        foreach (var node in nodes.Values)
        {
            if (!node.ParentId.HasValue) continue;
            if (node.ParentId == node.Uid) return $"售后品质分类“{node.CategoryName}”不能以自身作为上级分类。";
            if (!nodes.ContainsKey(node.ParentId.Value))
                return $"售后品质分类“{node.CategoryName}”的上级分类不存在或已被删除。";
        }

        foreach (var node in nodes.Values)
        {
            var visited = new HashSet<Guid>();
            var current = node;
            while (current.ParentId.HasValue)
            {
                if (!visited.Add(current.Uid))
                    return $"售后品质分类层级存在循环引用，涉及分类“{node.CategoryName}”。";
                current = nodes[current.ParentId.Value];
            }
        }

        return null;
    }

    /// <summary>按父先子后的顺序安排新增，使临时身份能够解析为持久化身份。</summary>
    private static IReadOnlyList<PreparedCreate> OrderCreatesByParent(IReadOnlyList<PreparedCreate> creates)
    {
        var pending = creates.ToDictionary(item => item.LogicalUid);
        var result = new List<PreparedCreate>(creates.Count);
        while (pending.Count > 0)
        {
            var ready = pending.Values
                .Where(item => !item.Request.Data.ParentId.HasValue
                               || !pending.ContainsKey(item.Request.Data.ParentId.Value))
                .ToArray();
            if (ready.Length == 0)
                throw new CategoryWriteException(ServiceErrorCodes.Validation, "新增分类层级无法按父子顺序保存。");
            foreach (var item in ready)
            {
                result.Add(item);
                pending.Remove(item.LogicalUid);
            }
        }
        return result;
    }

    /// <summary>将编辑字段转换为新实体并解析本批新增父节点身份。</summary>
    private static AfterSalesQualityCategory BuildCreateEntity(
        AfterSalesQualityCategoryEditDto dto,
        IReadOnlyDictionary<Guid, Guid> persistedIdByLogicalId)
    {
        var entity = dto.Adapt<AfterSalesQualityCategory>();
        entity.ParentId = ResolveParentId(dto.ParentId, persistedIdByLogicalId);
        entity.CategoryCode = NullIfWhiteSpace(dto.CategoryCode);
        entity.FakeName = NullIfWhiteSpace(dto.FakeName);
        return entity;
    }

    /// <summary>只写可编辑字段并保留原实体审计及身份。</summary>
    private static void ApplyDto(
        AfterSalesQualityCategory entity,
        AfterSalesQualityCategoryEditDto dto,
        IReadOnlyDictionary<Guid, Guid> persistedIdByLogicalId)
    {
        entity.CategoryName = dto.CategoryName;
        entity.ParentId = ResolveParentId(dto.ParentId, persistedIdByLogicalId);
        entity.FakeName = NullIfWhiteSpace(dto.FakeName);
        entity.CategoryCode = NullIfWhiteSpace(dto.CategoryCode);
        entity.SortNo = dto.SortNo;
        entity.IsEnabled = dto.IsEnabled;
    }

    /// <summary>把本批临时父标识转换为已保存身份，根节点保持空值。</summary>
    private static Guid? ResolveParentId(Guid? parentId, IReadOnlyDictionary<Guid, Guid> persistedIdByLogicalId)
    {
        parentId = NormalizeParentId(parentId);
        return parentId.HasValue && persistedIdByLogicalId.TryGetValue(parentId.Value, out var persistedId)
            ? persistedId
            : parentId;
    }

    private static Guid? NormalizeParentId(Guid? parentId) => parentId == Guid.Empty ? null : parentId;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";
    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(uid => uid != Guid.Empty)
        .GroupBy(uid => uid)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    /// <summary>计算旧树节点深度以保证先删除子节点，坏数据循环有限终止。</summary>
    private static int GetDepth(IReadOnlyDictionary<Guid, AfterSalesQualityCategory> existingById, Guid uid)
    {
        var depth = 0;
        var visited = new HashSet<Guid>();
        while (existingById.TryGetValue(uid, out var entity)
               && entity.ParentId.HasValue
               && visited.Add(uid))
        {
            depth++;
            uid = entity.ParentId.Value;
        }
        return depth;
    }

    /// <summary>仓储失败必须抛出业务异常，禁止提交部分成功回执。</summary>
    private static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success)
            throw new CategoryWriteException(result.ErrorCode ?? ServiceErrorCodes.BusinessBlocked, result.Message ?? fallbackMessage);
    }

    /// <summary>失败时尽力回滚，清理异常仅记录日志而不覆盖原始失败。</summary>
    private void TryRollbackTransaction()
    {
        try
        {
            _session.Transaction?.Rollback();
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "售后品质分类批量保存事务回滚失败。");
        }
    }

    /// <summary>最终图校验使用的最小节点，隔离客户端临时标识和实体写入。</summary>
    internal sealed record CategoryNode(
        Guid Uid,
        Guid? ParentId,
        string CategoryName,
        string? CategoryCode)
    {
        /// <summary>将当前实体投影为最终树校验节点。</summary>
        public static CategoryNode FromEntity(AfterSalesQualityCategory entity) =>
            new(entity.Uid, NormalizeParentId(entity.ParentId), entity.CategoryName, NullIfWhiteSpace(entity.CategoryCode));

        /// <summary>以权威请求身份和编辑字段构造最终树校验节点。</summary>
        public static CategoryNode FromDto(Guid uid, AfterSalesQualityCategoryEditDto dto) =>
            new(uid, NormalizeParentId(dto.ParentId), dto.CategoryName, NullIfWhiteSpace(dto.CategoryCode));
    }

    /// <summary>保存新增请求与同批引用所使用的临时身份。</summary>
    private sealed record PreparedCreate(CreateItemRequest<AfterSalesQualityCategoryEditDto> Request, Guid LogicalUid);

    /// <summary>保存整批校验结果及有序写入输入。</summary>
    private sealed class AfterSalesQualityCategoryBatchPreparation
    {
        public string? Error { get; private init; }
        public IReadOnlyDictionary<Guid, AfterSalesQualityCategory> ExistingById { get; private init; } =
            new Dictionary<Guid, AfterSalesQualityCategory>();
        public IReadOnlyList<PreparedCreate> CreateItems { get; private init; } = [];
        public IReadOnlyList<DeleteItemRequest> DeleteItems { get; private init; } = [];

        /// <summary>拒绝整批准备，不允许后续开始持久化。</summary>
        public static AfterSalesQualityCategoryBatchPreparation Fail(string error) => new() { Error = error };

        /// <summary>交付已验证的实体快照及拓扑写入顺序。</summary>
        public static AfterSalesQualityCategoryBatchPreparation Ok(
            IReadOnlyDictionary<Guid, AfterSalesQualityCategory> existingById,
            IReadOnlyList<PreparedCreate> createItems,
            IReadOnlyList<DeleteItemRequest> deleteItems) => new()
            {
                ExistingById = existingById,
                CreateItems = createItems,
                DeleteItems = deleteItems
            };
    }

    /// <summary>携带可公开的业务失败码，触发本批整体回滚。</summary>
    private sealed class CategoryWriteException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}

