using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public partial class ProductCategoryService(IProductCategoryRepository repository, ServiceBaseContext context)
    : BaseService<ProductCategory, ProductCategoryDto>(repository, context), IProductCategoryService
{
    private readonly ILogger<ProductCategoryService> _logger = context.LoggerFactory.CreateLogger<ProductCategoryService>();

    public async Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<ProductCategoryDto>>.Ok(entities.Adapt<IReadOnlyList<ProductCategoryDto>>())
                : ServiceResult<IReadOnlyList<ProductCategoryDto>>.Fail("未找到数据");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "查询全部产品分类失败。");
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            return ServiceResult<IReadOnlyList<ProductCategoryDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    public async Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(
        ProductCategoryBatchSaveRequest batchReq)
    {
        if (batchReq is null)
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail("请求不能为空。");

        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(item => item is null)
            || batchReq.UpdateItems.Any(item => item is null)
            || batchReq.DeleteItems.Any(item => item is null))
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail("批量保存明细不能包含空项。");
        if (!batchReq.HasChanges)
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail("没有需要保存的数据。");
        if (_session.Transaction is not null)
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始产品分类批量保存。");

        var response = new ProductCategoryBatchSaveResponse { TotalCount = batchReq.TotalCount };
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var preparation = await PrepareBatchSaveAsync(batchReq);
            if (preparation.Error is not null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<ProductCategoryBatchSaveResponse>.Fail(preparation.Error);
            }

            var persistedIdByLogicalId = preparation.ExistingById.Keys.ToDictionary(uid => uid, uid => uid);
            foreach (var create in preparation.CreateItems)
            {
                var entity = BuildCreateEntity(create.Request.Data, persistedIdByLogicalId);
                var writeResult = await repository.AddAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromCreate(create.Request, writeResult));
                EnsureWriteSucceeded(writeResult, "新增产品分类失败。");
                persistedIdByLogicalId[create.LogicalUid] = writeResult.Uid;
            }

            foreach (var item in batchReq.UpdateItems)
            {
                var entity = preparation.ExistingById[item.Uid];
                ApplyDto(entity, item.Data, persistedIdByLogicalId);
                entity.Version = item.Version;
                var writeResult = await repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                EnsureWriteSucceeded(writeResult, "更新产品分类失败。");
            }

            foreach (var item in preparation.DeleteItems)
            {
                var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                EnsureWriteSucceeded(writeResult, "删除产品分类失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<ProductCategoryBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (ProductCategoryBatchWriteException exception)
        {
            TryRollbackTransaction();
            _logger.LogWarning(exception, "产品分类批量保存因写入冲突失败，已回滚全部操作。");
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail(
                $"产品分类批量保存失败，所有操作已回滚。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "产品分类批量保存发生未预期异常，已回滚全部操作。");
            return ServiceResult<ProductCategoryBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected,
                "产品分类批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(
        ProductCategorySearchRequest request)
    {
        try
        {
            var paged = await repository.GetPagedAsync(request ?? new ProductCategorySearchRequest());
            return ServiceResult<PagedResponseBase<ProductCategoryDto>>.Ok(new PagedResponseBase<ProductCategoryDto>
            {
                Items = paged.Items.Adapt<List<ProductCategoryDto>>(),
                TotalCount = paged.TotalCount,
                PageIndex = paged.PageIndex,
                PageSize = paged.PageSize
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "产品分类分页查询失败。");
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            return ServiceResult<PagedResponseBase<ProductCategoryDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    private async Task<ProductCategoryBatchPreparation> PrepareBatchSaveAsync(
        ProductCategoryBatchSaveRequest request)
    {
        var duplicateUpdateUid = FindDuplicateUid(request.UpdateItems.Select(item => item.Uid));
        if (duplicateUpdateUid.HasValue)
            return ProductCategoryBatchPreparation.Fail($"同一产品分类不能重复修改：{duplicateUpdateUid}。");
        var duplicateDeleteUid = FindDuplicateUid(request.DeleteItems.Select(item => item.Uid));
        if (duplicateDeleteUid.HasValue)
            return ProductCategoryBatchPreparation.Fail($"同一产品分类不能重复删除：{duplicateDeleteUid}。");
        var conflictingUid = request.UpdateItems.Select(item => item.Uid)
            .Intersect(request.DeleteItems.Select(item => item.Uid))
            .FirstOrDefault(uid => uid != Guid.Empty);
        if (conflictingUid != Guid.Empty)
            return ProductCategoryBatchPreparation.Fail($"同一产品分类不能同时修改和删除：{conflictingUid}。");

        var preparedCreates = new List<PreparedCreate>();
        var createIds = new HashSet<Guid>();
        foreach (var item in request.CreateItems)
        {
            var validationError = NormalizeAndValidateDto(item.Data, item.RowIndex, "新增");
            if (validationError is not null) return ProductCategoryBatchPreparation.Fail(validationError);

            var logicalUid = item.Data.Uid == Guid.Empty ? Guid.NewGuid() : item.Data.Uid;
            item.Data.Uid = logicalUid;
            if (!createIds.Add(logicalUid))
                return ProductCategoryBatchPreparation.Fail($"本次提交包含重复的新分类标识：{logicalUid}。");
            preparedCreates.Add(new PreparedCreate(item, logicalUid));
        }

        foreach (var item in request.UpdateItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return ProductCategoryBatchPreparation.Fail($"第 {FormatRowIndex(item.RowIndex)} 行：修改标识或版本无效。");
            var validationError = NormalizeAndValidateDto(item.Data, item.RowIndex, "修改");
            if (validationError is not null) return ProductCategoryBatchPreparation.Fail(validationError);
            item.Data.Uid = item.Uid;
            item.Data.Version = item.Version;
        }
        foreach (var item in request.DeleteItems)
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return ProductCategoryBatchPreparation.Fail($"第 {FormatRowIndex(item.RowIndex)} 行：删除标识或版本无效。");

        var existing = await repository.GetAllForUpdateAsync();
        var existingById = existing.ToDictionary(item => item.Uid);
        if (createIds.Any(existingById.ContainsKey))
            return ProductCategoryBatchPreparation.Fail("新增分类的临时标识与现有分类冲突，请重新加载后重试。");

        foreach (var item in request.UpdateItems)
        {
            if (!existingById.TryGetValue(item.Uid, out var entity))
                return ProductCategoryBatchPreparation.Fail($"修改失败：产品分类 {item.Uid} 不存在或已删除。");
            if (entity.Version != item.Version)
                return ProductCategoryBatchPreparation.Fail($"产品分类 {item.Uid} 已被其他人修改，请刷新后重试。");
        }
        foreach (var item in request.DeleteItems)
        {
            if (!existingById.TryGetValue(item.Uid, out var entity))
                return ProductCategoryBatchPreparation.Fail($"删除失败：产品分类 {item.Uid} 不存在或已删除。");
            if (entity.Version != item.Version)
                return ProductCategoryBatchPreparation.Fail($"产品分类 {item.Uid} 已被其他人修改，请刷新后重试。");
        }

        var deletedIds = request.DeleteItems.Select(item => item.Uid).ToHashSet();
        var referencedIds = await repository.GetReferencedCategoryIdsAsync(deletedIds);
        if (referencedIds.Count > 0)
            return ProductCategoryBatchPreparation.Fail(
                "删除失败：以下产品分类仍被产品引用：" + string.Join(", ", referencedIds));

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
        if (graphError is not null) return ProductCategoryBatchPreparation.Fail(graphError);

        var orderedCreates = OrderCreatesByParent(preparedCreates);
        var orderedDeletes = request.DeleteItems
            .OrderByDescending(item => GetDepth(existingById, item.Uid))
            .ToArray();
        return ProductCategoryBatchPreparation.Ok(existingById, orderedCreates, orderedDeletes);
    }

    private static string? NormalizeAndValidateDto(ProductCategoryDto? dto, int? rowIndex, string operation)
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
            if (node.ParentId == node.Uid) return $"产品分类“{node.CategoryName}”不能以自身作为上级分类。";
            if (!nodes.ContainsKey(node.ParentId.Value))
                return $"产品分类“{node.CategoryName}”的上级分类不存在或已被删除。";
        }

        foreach (var node in nodes.Values)
        {
            var visited = new HashSet<Guid>();
            var current = node;
            while (current.ParentId.HasValue)
            {
                if (!visited.Add(current.Uid))
                    return $"产品分类层级存在循环引用，涉及分类“{node.CategoryName}”。";
                current = nodes[current.ParentId.Value];
            }
        }

        return null;
    }

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
                throw new ProductCategoryBatchWriteException("新增分类层级无法按父子顺序保存。");
            foreach (var item in ready)
            {
                result.Add(item);
                pending.Remove(item.LogicalUid);
            }
        }
        return result;
    }

    private static ProductCategory BuildCreateEntity(
        ProductCategoryDto dto,
        IReadOnlyDictionary<Guid, Guid> persistedIdByLogicalId)
    {
        var entity = dto.Adapt<ProductCategory>();
        entity.ParentId = ResolveParentId(dto.ParentId, persistedIdByLogicalId);
        entity.CategoryCode = NullIfWhiteSpace(dto.CategoryCode);
        entity.FakeName = NullIfWhiteSpace(dto.FakeName);
        return entity;
    }

    private static void ApplyDto(
        ProductCategory entity,
        ProductCategoryDto dto,
        IReadOnlyDictionary<Guid, Guid> persistedIdByLogicalId)
    {
        entity.CategoryName = dto.CategoryName;
        entity.ParentId = ResolveParentId(dto.ParentId, persistedIdByLogicalId);
        entity.FakeName = NullIfWhiteSpace(dto.FakeName);
        entity.CategoryCode = NullIfWhiteSpace(dto.CategoryCode);
        entity.SortNo = dto.SortNo;
    }

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

    private static int GetDepth(IReadOnlyDictionary<Guid, ProductCategory> existingById, Guid uid)
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

    private static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success)
            throw new ProductCategoryBatchWriteException(result.Message ?? fallbackMessage);
    }

    private void TryRollbackTransaction()
    {
        try
        {
            _session.Transaction?.Rollback();
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "产品分类批量保存事务回滚失败。");
        }
    }

    internal sealed record CategoryNode(
        Guid Uid,
        Guid? ParentId,
        string CategoryName,
        string? CategoryCode)
    {
        public static CategoryNode FromEntity(ProductCategory entity) =>
            new(entity.Uid, NormalizeParentId(entity.ParentId), entity.CategoryName, NullIfWhiteSpace(entity.CategoryCode));

        public static CategoryNode FromDto(Guid uid, ProductCategoryDto dto) =>
            new(uid, NormalizeParentId(dto.ParentId), dto.CategoryName, NullIfWhiteSpace(dto.CategoryCode));
    }

    private sealed record PreparedCreate(CreateItemRequest<ProductCategoryDto> Request, Guid LogicalUid);

    private sealed class ProductCategoryBatchPreparation
    {
        public string? Error { get; private init; }
        public IReadOnlyDictionary<Guid, ProductCategory> ExistingById { get; private init; } =
            new Dictionary<Guid, ProductCategory>();
        public IReadOnlyList<PreparedCreate> CreateItems { get; private init; } = [];
        public IReadOnlyList<DeleteItemRequest> DeleteItems { get; private init; } = [];

        public static ProductCategoryBatchPreparation Fail(string error) => new() { Error = error };

        public static ProductCategoryBatchPreparation Ok(
            IReadOnlyDictionary<Guid, ProductCategory> existingById,
            IReadOnlyList<PreparedCreate> createItems,
            IReadOnlyList<DeleteItemRequest> deleteItems) => new()
            {
                ExistingById = existingById,
                CreateItems = createItems,
                DeleteItems = deleteItems
            };
    }

    private sealed class ProductCategoryBatchWriteException(string message) : Exception(message);
}
