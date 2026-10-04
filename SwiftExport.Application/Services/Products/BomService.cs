using SwiftExport.Core.Interfaces.Products.Catalog;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Data;
namespace SwiftExport.Application.Services.Products;

/// <summary>维护当前 BOM；所有保存入口共用完整图校验和批量事务，查询继续保留现有兼容契约。</summary>
public partial class BomService(
    IBomRepository bomRepo,
    IProductRepository pdRepo,
    ServiceBaseContext context)
    : BaseService<Bom, BomDto>(bomRepo, context), IBomService
{

    private readonly ILogger<BomService> _logger = context.LoggerFactory.CreateLogger<BomService>();
    private readonly BomStructureValidator _structureValidator = new();

    #region BOM管理CRUD操作

    public async Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var entities = await bomRepo.GetBomDtoByBomNoAsync(bomNo);
        if (!entities.Any()) return ServiceResult<List<BomDto>>.Fail("未找到数据");

        return ServiceResult<List<BomDto>>.Ok([.. entities]);
    }

    public async Task<ServiceResult<bool>> ValidateBomAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
        {
            return ServiceResult<bool>.Fail("产品SKU不能为空");
        }

        // 0. 获取产品的 BOM 号
        Product? pd = await pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
            return ServiceResult<bool>.Fail("产品型号不存在或产品没设置BOM");

        string bomNo = pd.BomNo;

        // 1. 查询该产品的 BOM 项
        var bomItems = await bomRepo.GetBomByBomNoAsync(bomNo);
        if (bomItems == null || !bomItems.Any())
            return ServiceResult<bool>.Fail("该Bom 没有 BOM 数据，视为不合法");

        // 2. 数量合法性
        if (bomItems.Any(i => i.Numerator <= 0))
            return ServiceResult<bool>.Fail("子项的用量不能小于等于0");

        // 3. 子件存在性
        foreach (var item in bomItems)
        {
            var exists = item.ComponentProductId.HasValue
                ? await pdRepo.ExistsIdAsync(item.ComponentProductId.Value)
                : await pdRepo.GetBySkuAsync(item.ComponentSkuSnapshot) != null;
            if (!exists) return ServiceResult<bool>.Fail($"子项{item.ComponentSkuSnapshot}不存在");
        }

        // 4. 重复项检查
        if (bomItems.GroupBy(i => i.ComponentSkuSnapshot).Any(g => g.Count() > 1))
            return ServiceResult<bool>.Fail($"子项中存在重复项");

        // 5. 循环依赖检查（DFS，异步）
        var visited = new HashSet<string>();

        async Task<bool> HasCycleAsync(string currentBomNo)
        {
            if (!visited.Add(currentBomNo)) return true; // 已访问过，说明有环

            var children = await bomRepo.GetBomByBomNoAsync(currentBomNo);
            foreach (var child in children)
            {
                // 递归检查子件的 BOM
                var subPd = await pdRepo.GetBySkuAsync(child.ComponentSkuSnapshot);
                if (subPd != null && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    if (await HasCycleAsync(subPd.BomNo)) return true;
                }
            }

            visited.Remove(currentBomNo);
            return false;
        }

        if (await HasCycleAsync(bomNo)) return ServiceResult<bool>.Fail("递归依赖检查不通过,Bom链上存在不合理.");

        return ServiceResult<bool>.Ok(true); // 所有检查通过
    }

    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
            return ServiceResult<IEnumerable<BomTreeDto>>.Fail("产品SKU不能为空");

        var pd = await pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
        {
            return ServiceResult<IEnumerable<BomTreeDto>>.Ok([]);
        }

        var visited = new HashSet<string>();
        var result = await BuildBomTreeAsync(pd.BomNo, visited);
        return ServiceResult<IEnumerable<BomTreeDto>>.Ok(result);
    }

    private async Task<List<BomTreeDto>> BuildBomTreeAsync(string bomNo, HashSet<string> visited)
    {
        var resultList = new List<BomTreeDto>();
        if (!visited.Add(bomNo))
        {
            // 发生循环依赖，直接返回当前已构建部分避免死循环
            return resultList;
        }

        var bomItems = await bomRepo.GetBomByBomNoAsync(bomNo);
        foreach (var item in bomItems)
        {
            var dto = item.Adapt<BomTreeDto>();
            var subPd = await pdRepo.GetBySkuAsync(item.ComponentSkuSnapshot);
            if (subPd != null)
            {
                // 补齐显示用的字段
                dto.Description = subPd.DescriptionEn;
                dto.MainImage = subPd.MainImage;
                // 如果你有 Finish (表面处理/颜色)，这里可以补齐：
                // 这取决于你的底层模型。例如如果需要查询 Color，可在这里处理

                // 判断是否继续递归拆解
                // 条件：实体有BomNo 且 本身的类型不是"止拆"
                if (subPd.BomVersion > 0 && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    dto.Children = await BuildBomTreeAsync(subPd.BomNo, visited);
                }
            }
            else
            {
                // 如果子件在产品表里找不到，仍然添加这个节点，但可以考虑标记一下（例如设置Description为"未知产品"）
                dto.Description = "未知Sku";
            }
            resultList.Add(dto);
        }

        visited.Remove(bomNo);
        return resultList;
    }
    #endregion






    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<BomDto>>> GetAllAsync()
    {
        try
        {
            var entities = await bomRepo.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<BomDto>>.Ok(entities.Adapt<IReadOnlyList<BomDto>>())
                : ServiceResult<IReadOnlyList<BomDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<IReadOnlyList<BomDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }


    /// <summary>校验批量请求后进入统一保存内核，所有行变更在同一事务完成完整图校验和写入。</summary>
    public async Task<ServiceResult<BomBatchSaveResponse>> BatchSaveAsync(BomBatchSaveRequest batchReq)
    {
        if (batchReq is null)
            return ServiceResult<BomBatchSaveResponse>.Fail("请求不能为空。");

        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(x => x is null)
            || batchReq.UpdateItems.Any(x => x is null)
            || batchReq.DeleteItems.Any(x => x is null))
            return ServiceResult<BomBatchSaveResponse>.Fail("批量保存明细不能包含空项。");
        if (!batchReq.HasChanges)
            return ServiceResult<BomBatchSaveResponse>.Fail("没有需要保存的数据。");
        return await SaveBatchAsync(batchReq);
    }

    /// <summary>拥有既有批量事务；按号删除先在此事务解析当前行，再复用同一准备与最终图校验，不删除历史版本。</summary>
    private async Task<ServiceResult<BomBatchSaveResponse>> SaveBatchAsync(BomBatchSaveRequest batchReq, string? deleteBomNo = null)
    {
        if (_session.Transaction is not null)
            return ServiceResult<BomBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始 BOM 批量保存。");

        IDbTransaction? transaction = null;
        try
        {
            transaction = _session.Connection.BeginTransaction();
            _session.Transaction = transaction;
            if (deleteBomNo is not null)
            {
                var currentRows = (await bomRepo.GetBomByBomNoAsync(deleteBomNo)).ToArray();
                if (currentRows.Length == 0)
                {
                    TryRollbackTransaction(transaction);
                    return ServiceResult<BomBatchSaveResponse>.Fail("未找到当前有效 BOM 行，不能按号删除。");
                }
                batchReq.DeleteItems = currentRows.Select(row => new DeleteItemRequest
                {
                    Uid = row.Uid, Version = row.Version, IsSoftDelete = false
                }).ToList();
            }
            var response = new BomBatchSaveResponse { TotalCount = batchReq.TotalCount };
            var preparation = await PrepareBatchSaveAsync(batchReq);
            if (preparation.Error is not null)
            {
                TryRollbackTransaction(transaction);
                return ServiceResult<BomBatchSaveResponse>.Fail(preparation.Error);
            }

            foreach (var (request, entity) in preparation.CreateItems)
            {
                var writeResult = await bomRepo.AddAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromCreate(request, writeResult));
                EnsureWriteSucceeded(writeResult, "新增 BOM 行失败。");
            }

            foreach (var (request, entity) in preparation.UpdateItems)
            {
                var writeResult = await bomRepo.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(request, writeResult));
                EnsureWriteSucceeded(writeResult, "更新 BOM 行失败。");
            }

            foreach (var request in batchReq.DeleteItems)
            {
                var writeResult = await bomRepo.DeleteAsync(request.Uid, request.Version, request.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(request, writeResult));
                EnsureWriteSucceeded(writeResult, "删除 BOM 行失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<BomBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (BomBatchWriteException ex)
        {
            TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Warning, ex, "BOM 批量保存因写入冲突失败，已回滚全部操作。");
            return ServiceResult<BomBatchSaveResponse>.Fail(
                $"BOM 批量保存失败，所有操作已回滚。原因：{ex.Message}");
        }
        catch (Exception ex)
        {
            TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Error, ex, "BOM 批量保存发生未预期异常，已回滚全部操作。");
            return ServiceResult<BomBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected,
                "BOM 批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            // 只清理本层成功创建的事务；释放故障不能覆盖已确定的业务结果。
            if (transaction is not null)
            {
                try { transaction.Dispose(); }
                catch (Exception disposeException)
                {
                    TryLogSaveFailure(LogLevel.Error, disposeException, "BOM 批量保存事务释放失败，关闭连接。");
                    try { _session.Connection.Dispose(); }
                    catch (Exception connectionException)
                    {
                        TryLogSaveFailure(LogLevel.Error, connectionException, "BOM 批量保存连接释放失败。");
                    }
                }
                finally { _session.Transaction = null; }
            }
        }
    }

    public async Task<ServiceResult<PagedResponseBase<BomDto>>> GetPagedListAsync(BomSearchRequest request)
    {
        try
        {
            var result = await bomRepo.GetPagedAsync(request ?? new BomSearchRequest());
            return ServiceResult<PagedResponseBase<BomDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BOM 分页查询失败。");
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            return ServiceResult<PagedResponseBase<BomDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    private async Task<BomBatchPreparation> PrepareBatchSaveAsync(BomBatchSaveRequest request)
    {
        var requestError = ValidateBatchRequest(request);
        if (requestError is not null) return BomBatchPreparation.Fail(requestError);

        var operationIds = request.UpdateItems.Select(x => x.Uid)
            .Concat(request.DeleteItems.Select(x => x.Uid))
            .Distinct()
            .ToArray();
        var existingRows = await bomRepo.GetByIdListAsync(operationIds, withUpdLock: true);
        var existingById = existingRows.ToDictionary(x => x.Uid);
        var missingIds = operationIds.Except(existingById.Keys).ToArray();
        if (missingIds.Length > 0)
            return BomBatchPreparation.Fail("包含不存在或已删除的 BOM 行：" + string.Join(", ", missingIds));

        foreach (var item in request.UpdateItems)
            if (existingById[item.Uid].Version != item.Version)
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 已被其他人修改，请刷新后重试。");
        foreach (var item in request.DeleteItems)
            if (existingById[item.Uid].Version != item.Version)
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 已被其他人修改，请刷新后重试。");

        var parentByBomNo = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var bomNo in request.CreateItems.Select(x => x.Data.BomNo)
                     .Concat(request.UpdateItems.Select(x => x.Data.BomNo))
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(x => x.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var foundParent = await pdRepo.GetByBomNoAsync(bomNo);
            if (foundParent is null)
                return BomBatchPreparation.Fail($"BOM {bomNo} 没有对应的父产品。");
            var parent = await pdRepo.GetByIdAsync(foundParent.Uid, withUpdLock: true);
            if (parent is null || !string.Equals(parent.BomNo, bomNo, StringComparison.OrdinalIgnoreCase))
                return BomBatchPreparation.Fail($"BOM {bomNo} 对应的父产品已发生变化，请刷新后重试。");
            if (parent.BomVersion <= 0)
                return BomBatchPreparation.Fail($"父产品 {parent.Sku} 的 BOM 版本必须大于零。");
            var parentError = CustomerSuppliedMaterialPolicy.ValidateBomParent(parent);
            if (parentError is not null) return BomBatchPreparation.Fail(parentError);
            parentByBomNo[bomNo] = parent;
        }

        var existingParentIds = existingRows
            .Where(x => x.ParentProductId.HasValue)
            .Select(x => x.ParentProductId!.Value)
            .Distinct()
            .ToArray();
        var existingParents = await pdRepo.GetByIdListAsync(existingParentIds, withUpdLock: true);
        var parentById = existingParents.ToDictionary(x => x.Uid);
        foreach (var row in existingRows)
        {
            if (!row.ParentProductId.HasValue || !parentById.TryGetValue(row.ParentProductId.Value, out var parent))
                return BomBatchPreparation.Fail($"BOM 行 {row.Uid} 的父产品不存在或已删除。");
            if (string.IsNullOrWhiteSpace(parent.BomNo)
                || !string.Equals(row.BomNo, parent.BomNo, StringComparison.OrdinalIgnoreCase))
                return BomBatchPreparation.Fail($"BOM 行 {row.Uid} 与父产品的 BOM 号不一致，不能修改或删除。");
            if (row.BomVersion != parent.BomVersion)
                return BomBatchPreparation.Fail($"BOM 行 {row.Uid} 属于历史版本，不能修改或删除。");
            parentByBomNo[parent.BomNo] = parent;
        }

        var componentIds = request.CreateItems.Select(x => x.Data.SubSkuId)
            .Concat(request.UpdateItems.Select(x => x.Data.SubSkuId))
            .Distinct()
            .ToArray();
        var components = await pdRepo.GetByIdListAsync(componentIds, withUpdLock: true);
        var componentById = components.ToDictionary(x => x.Uid);
        var missingComponents = componentIds.Except(componentById.Keys).ToArray();
        if (missingComponents.Length > 0)
            return BomBatchPreparation.Fail("以下 BOM 子产品不存在或已删除：" + string.Join(", ", missingComponents));

        var currentGraph = (await bomRepo.GetCurrentVersionBomGraphAsync()).ToList();
        var nextLineByParent = currentGraph
            .Where(x => x.ParentProductId.HasValue)
            .GroupBy(x => (ParentId: x.ParentProductId!.Value, x.BomVersion))
            .ToDictionary(x => x.Key, x => x.Max(line => line.LineNumber));

        var createItems = new List<(CreateItemRequest<BomDto> Request, Bom Entity)>();
        foreach (var item in request.CreateItems)
        {
            var parent = parentByBomNo[item.Data.BomNo.Trim()];
            var key = (parent.Uid, parent.BomVersion);
            var lineNumber = nextLineByParent.TryGetValue(key, out var currentMax) ? currentMax + 1 : 1;
            nextLineByParent[key] = lineNumber;
            createItems.Add((item, BuildBomEntity(item.Data, parent, componentById[item.Data.SubSkuId], lineNumber)));
        }

        var updateItems = new List<(UpdateItemRequest<BomDto> Request, Bom Entity)>();
        foreach (var item in request.UpdateItems)
        {
            var existing = existingById[item.Uid];
            var parent = parentById[existing.ParentProductId!.Value];
            if (!string.Equals(existing.BomNo, item.Data.BomNo.Trim(), StringComparison.OrdinalIgnoreCase))
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 不能修改所属 BOM 号。");

            existing.ComponentProductId = item.Data.SubSkuId;
            existing.ComponentSkuSnapshot = componentById[item.Data.SubSkuId].Sku;
            existing.Numerator = item.Data.Numerator;
            existing.Denominator = item.Data.Denominator;
            existing.Note = NormalizeNote(item.Data.Note);
            existing.ParentProductId = parent.Uid;
            existing.ParentSkuSnapshot = parent.Sku;
            existing.BomVersion = parent.BomVersion;
            existing.Version = item.Version;
            updateItems.Add((item, existing));
        }

        var deletedIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
        var updatedById = updateItems.ToDictionary(x => x.Entity.Uid, x => x.Entity);
        var finalGraph = currentGraph
            .Where(x => !deletedIds.Contains(x.Uid))
            .Select(x => updatedById.TryGetValue(x.Uid, out var updated) ? updated : x)
            .Concat(createItems.Select(x => x.Entity))
            .ToArray();

        var affectedParentIds = createItems.Select(x => x.Entity.ParentProductId!.Value)
            .Concat(updateItems.Select(x => x.Entity.ParentProductId!.Value))
            .Concat(request.DeleteItems.Select(x => existingById[x.Uid].ParentProductId!.Value))
            .Distinct()
            .ToArray();
        var affectedParentIdSet = affectedParentIds.ToHashSet();
        var validationProductIds = affectedParentIds
            .Concat(finalGraph
                .Where(x => x.ParentProductId.HasValue
                            && affectedParentIdSet.Contains(x.ParentProductId.Value)
                            && x.ComponentProductId.HasValue)
                .Select(x => x.ComponentProductId!.Value))
            .Distinct()
            .ToArray();
        var validationProducts = await pdRepo.GetByIdListAsync(validationProductIds, withUpdLock: true);
        var affectedProducts = validationProducts
            .Concat(parentById.Values)
            .Concat(parentByBomNo.Values)
            .GroupBy(x => x.Uid)
            .ToDictionary(x => x.Key, x => x.First());
        var structureError = _structureValidator.Validate(finalGraph, affectedParentIds, affectedProducts);
        return structureError is null
            ? BomBatchPreparation.Ok(createItems, updateItems)
            : BomBatchPreparation.Fail(structureError);
    }

    private static string? ValidateBatchRequest(BomBatchSaveRequest request)
    {
        foreach (var item in request.CreateItems)
        {
            var error = ValidateDto(item.Data, item.RowIndex, "新增");
            if (error is not null) return error;
        }
        foreach (var item in request.UpdateItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return $"第 {FormatRowIndex(item.RowIndex)} 行：修改 BOM 的标识或版本无效。";
            var error = ValidateDto(item.Data, item.RowIndex, "修改");
            if (error is not null) return error;
        }
        foreach (var item in request.DeleteItems)
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return $"第 {FormatRowIndex(item.RowIndex)} 行：删除 BOM 的标识或版本无效。";

        var duplicateUpdateId = FindDuplicateUid(request.UpdateItems.Select(x => x.Uid));
        if (duplicateUpdateId.HasValue) return $"BOM 行 {duplicateUpdateId} 在修改列表中重复。";
        var duplicateDeleteId = FindDuplicateUid(request.DeleteItems.Select(x => x.Uid));
        if (duplicateDeleteId.HasValue) return $"BOM 行 {duplicateDeleteId} 在删除列表中重复。";
        var conflictingId = request.UpdateItems.Select(x => x.Uid)
            .Intersect(request.DeleteItems.Select(x => x.Uid))
            .FirstOrDefault();
        return conflictingId == Guid.Empty ? null : $"同一 BOM 行不能同时修改和删除：{conflictingId}。";
    }

    private static string? ValidateDto(BomDto? dto, int? rowIndex, string operation)
    {
        if (dto is null) return $"第 {FormatRowIndex(rowIndex)} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(dto.BomNo)) return $"第 {FormatRowIndex(rowIndex)} 行：BOM 号不能为空。";
        if (dto.SubSkuId == Guid.Empty) return $"第 {FormatRowIndex(rowIndex)} 行：BOM 子产品不能为空。";
        if (dto.Numerator <= 0 || dto.Denominator <= 0)
            return $"第 {FormatRowIndex(rowIndex)} 行：用量分子和分母必须大于零。";
        dto.BomNo = dto.BomNo.Trim();
        return null;
    }

    private static Bom BuildBomEntity(BomDto dto, Product parent, Product child, int lineNumber) => new()
    {
        BomNo = parent.BomNo!.Trim(),
        BomVersion = parent.BomVersion,
        ParentProductId = parent.Uid,
        ParentSkuSnapshot = parent.Sku,
        LineNumber = lineNumber,
        ComponentProductId = child.Uid,
        ComponentSkuSnapshot = child.Sku,
        Numerator = dto.Numerator,
        Denominator = dto.Denominator,
        Note = NormalizeNote(dto.Note)
    };

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";
    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(x => x != Guid.Empty)
        .GroupBy(x => x)
        .FirstOrDefault(x => x.Count() > 1)
        ?.Key;

    /// <summary>任一行写入失败使整批回滚，沿用批量保存现有的业务阻断错误协议。</summary>
    private static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success) throw new BomBatchWriteException(result.Message ?? fallbackMessage);
    }

    /// <summary>尽力回滚本层创建的事务；回滚或诊断日志故障均不得替换原始保存结果。</summary>
    private void TryRollbackTransaction(IDbTransaction? transaction)
    {
        try
        {
            transaction?.Rollback();
        }
        catch (Exception rollbackException)
        {
            TryLogSaveFailure(LogLevel.Error, rollbackException, "BOM 批量保存事务回滚失败。");
        }
    }

    /// <summary>保存失败及资源清理只尽力记录诊断，日志提供者异常不得中断回滚、释放或覆盖业务结果。</summary>
    private void TryLogSaveFailure(LogLevel level, Exception exception, string message)
    {
        try { _logger.Log(level, exception, "{Message}", message); }
        catch { /* 不通过同一失效日志管道再次记录，保留原始结果并继续清理。 */ }
    }

    /// <summary>将行写入失败提升为批量回滚，不改变既有批量错误分类。</summary>
    private sealed class BomBatchWriteException(string message) : Exception(message);

    private sealed class BomBatchPreparation
    {
        public string? Error { get; private init; }
        public IReadOnlyList<(CreateItemRequest<BomDto> Request, Bom Entity)> CreateItems { get; private init; } = [];
        public IReadOnlyList<(UpdateItemRequest<BomDto> Request, Bom Entity)> UpdateItems { get; private init; } = [];

        public static BomBatchPreparation Fail(string error) => new() { Error = error };
        public static BomBatchPreparation Ok(
            IReadOnlyList<(CreateItemRequest<BomDto> Request, Bom Entity)> createItems,
            IReadOnlyList<(UpdateItemRequest<BomDto> Request, Bom Entity)> updateItems) => new()
            {
                CreateItems = createItems,
                UpdateItems = updateItems
            };
    }
    #endregion

}
