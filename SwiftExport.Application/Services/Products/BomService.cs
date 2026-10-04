using SwiftExport.Core.Interfaces.Products.Catalog;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Models.Products;
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
    private readonly SharedBomStructureValidator _structureValidator = new();

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

    /// <summary>拥有批量事务并复用最终图校验；明确版本冲突独立返回，提交异常保持结果未知，不自动重试。</summary>
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
                    var rolledBack = TryRollbackTransaction(transaction);
                    return ServiceResult<BomBatchSaveResponse>.Fail(rolledBack
                        ? "未找到当前有效 BOM 行，不能按号删除。"
                        : "未找到当前有效 BOM 行，不能按号删除；事务回滚未能确认，请先核实结果。");
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
                var rolledBack = TryRollbackTransaction(transaction);
                return ServiceResult<BomBatchSaveResponse>.Fail(preparation.ErrorCode, rolledBack
                    ? preparation.Error
                    : preparation.Error + "事务回滚未能确认，请先核实结果。");
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

            // 提交调用抛出时不能据后续回滚判断是否已经提交，也不能自动重复新增。
            try { transaction.Commit(); }
            catch (Exception commitException)
            {
                TryRollbackTransaction(transaction);
                TryLogSaveFailure(LogLevel.Error, commitException, "BOM 批量保存提交结果无法确认，已尝试回滚清理。");
                return ServiceResult<BomBatchSaveResponse>.Fail(ServiceErrorCodes.OutcomeUnknown,
                    "BOM 保存提交结果无法确认，请先刷新并核实结果，再决定后续操作。");
            }
            return ServiceResult<BomBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (BomBatchWriteException ex)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Warning, ex, rolledBack
                ? "BOM 批量保存因行写入失败，已回滚全部操作。"
                : "BOM 批量保存因行写入失败，事务回滚未能确认。");
            return ServiceResult<BomBatchSaveResponse>.Fail(ex.ErrorCode, rolledBack
                ? $"BOM 批量保存失败，所有操作已回滚。原因：{ex.Message}"
                : $"BOM 批量保存失败，事务回滚未能确认，请先核实结果。原因：{ex.Message}");
        }
        catch (Exception ex)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Error, ex, rolledBack
                ? "BOM 批量保存发生未预期异常，已回滚全部操作。"
                : "BOM 批量保存发生未预期异常，事务未开始或回滚未能确认。");
            return ServiceResult<BomBatchSaveResponse>.Fail(ServiceErrorCodes.Unexpected, rolledBack
                ? "BOM 批量保存失败，所有操作已回滚，请刷新后重试。"
                : "BOM 批量保存失败，事务未开始或回滚未能确认，请先核实结果。");
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

    /// <summary>按数据库共享组解析目标、分配行号并校验候选图；来源父只用于兼容追溯，不能决定组成归属。</summary>
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
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        foreach (var item in request.DeleteItems)
            if (existingById[item.Uid].Version != item.Version)
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);

        var requestedNos = request.CreateItems.Select(item => item.Data.BomNo)
            .Concat(request.UpdateItems.Select(item => item.Data.BomNo)).ToArray();
        var facts = await bomRepo.GetSharedSaveFactsAsync(requestedNos);
        var byInput = facts.SelectMany(group => group.InputIndexes.Select(index => (index, group)))
            .ToDictionary(item => item.index, item => item.group);
        var byLine = facts.SelectMany(group => group.Lines.Select(line => (line.Uid, Group: group)))
            .ToDictionary(item => item.Uid, item => item.Group);
        foreach (var row in existingRows)
            if (!byLine.ContainsKey(row.Uid))
                return BomBatchPreparation.Fail($"BOM 行 {row.Uid} 已发生变化，请刷新后重试。");
        for (var index = 0; index < requestedNos.Length; index++)
            if (!byInput.ContainsKey(index))
                return BomBatchPreparation.Fail("无法确认本次 BOM 编号的数据库归属，请刷新后重试。");

        var affectedKeys = existingRows.Select(row => byLine[row.Uid].GroupKey)
            .Concat(byInput.Values.Select(group => group.GroupKey)).ToHashSet();
        var affectedGroups = facts.Where(group => affectedKeys.Contains(group.GroupKey)).ToArray();
        foreach (var group in affectedGroups)
        {
            if (group.Products.Count == 0)
                return BomBatchPreparation.Fail($"BOM {group.BomNo} 没有启用该编号的有效父产品，不能修改组成。");
            // 普通读取暂保留标准标记匹配；不能写出没有标准引用父、保存后仍不可见的组成。
            if (!group.Products.Any(product => product.BomVersion == 1))
                return BomBatchPreparation.Fail($"BOM {group.BomNo} 的引用父有效标记尚未标准化，不能保存普通读取不可见的组成。");
            if (group.Lines.Any(line => !line.IsDeleted && line.BomVersion != 1))
                return BomBatchPreparation.Fail($"BOM {group.BomNo} 存在异常组成有效标记，请先核实旧数据，不能自动启用或改写。");
        }
        var componentIds = request.CreateItems.Select(item => item.Data.SubSkuId)
            .Concat(request.UpdateItems.Select(item => item.Data.SubSkuId))
            .Concat(affectedGroups.SelectMany(group => group.Lines)
                .Where(line => line.ComponentProductId.HasValue).Select(line => line.ComponentProductId!.Value))
            .Distinct().ToArray();
        // 共享归属来自事实快照；定点锁定受影响父及组件，版本不符时不使用等待期间过期的父资格。
        // UID 排序只稳定输入参数顺序；仓储查询没有 ORDER BY，不能据此保证数据库加锁顺序。
        var affectedParents = affectedGroups.SelectMany(group => group.Products)
            .GroupBy(product => product.Uid).Select(group => group.First()).ToArray();
        var lockIds = affectedParents.Select(product => product.Uid).Concat(componentIds)
            .Distinct().OrderBy(uid => uid).ToArray();
        var components = await pdRepo.GetByIdListAsync(lockIds, withUpdLock: true);
        var componentById = components.Where(product => !product.IsDeleted).ToDictionary(product => product.Uid);
        foreach (var parent in affectedParents)
            if (!componentById.TryGetValue(parent.Uid, out var lockedParent) || lockedParent.Version != parent.Version)
                return BomBatchPreparation.Fail($"共享父产品 {parent.Sku} 已变化，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        var requestedComponentIds = request.CreateItems.Select(item => item.Data.SubSkuId)
            .Concat(request.UpdateItems.Select(item => item.Data.SubSkuId));
        var missingComponents = requestedComponentIds.Except(componentById.Keys).ToArray();
        if (missingComponents.Length > 0)
            return BomBatchPreparation.Fail("以下 BOM 子产品不存在或已删除：" + string.Join(", ", missingComponents));
        var nextLineByGroup = affectedGroups.ToDictionary(group => group.GroupKey,
            group => group.Lines.Count == 0 ? 0 : group.Lines.Max(line => line.LineNumber));

        var createItems = new List<(CreateItemRequest<BomDto> Request, Bom Entity)>();
        for (var index = 0; index < request.CreateItems.Count; index++)
        {
            var item = request.CreateItems[index];
            var group = byInput[index];
            var parent = componentById[group.Products.OrderBy(product => product.Uid).First().Uid];
            var lineNumber = ++nextLineByGroup[group.GroupKey];
            createItems.Add((item, BuildBomEntity(item.Data, parent, componentById[item.Data.SubSkuId], lineNumber, group.BomNo)));
        }

        var updateItems = new List<(UpdateItemRequest<BomDto> Request, Bom Entity)>();
        for (var index = 0; index < request.UpdateItems.Count; index++)
        {
            var item = request.UpdateItems[index];
            var source = existingById[item.Uid];
            if (byLine[item.Uid].GroupKey != byInput[request.CreateItems.Count + index].GroupKey)
                return BomBatchPreparation.Fail($"BOM 行 {item.Uid} 不能修改所属 BOM 号。");
            // 创建候选实体，保留编号和来源快照，不修改事实查询返回的物理行。
            var existing = CopyBomForUpdate(source);
            existing.ComponentProductId = item.Data.SubSkuId;
            existing.ComponentSkuSnapshot = componentById[item.Data.SubSkuId].Sku;
            existing.Numerator = item.Data.Numerator;
            existing.Denominator = item.Data.Denominator;
            existing.Note = NormalizeNote(item.Data.Note);
            existing.Version = item.Version;
            updateItems.Add((item, existing));
        }

        var deletedIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
        var updatedById = updateItems.ToDictionary(x => x.Entity.Uid, x => x.Entity);
        var createdByGroup = createItems.Select((item, index) => (item.Entity, byInput[index].GroupKey))
            .GroupBy(item => item.GroupKey).ToDictionary(group => group.Key, group => group.Select(item => item.Entity).ToArray());
        var finalGroups = facts.Select(group => new SharedBomGroup
        {
            GroupKey = group.GroupKey, BomNo = group.BomNo, InputIndexes = group.InputIndexes,
            Products = group.Products.Select(product => componentById.GetValueOrDefault(product.Uid) ?? product).ToArray(),
            Lines = group.Lines.Where(line => !deletedIds.Contains(line.Uid))
                .Select(line => updatedById.TryGetValue(line.Uid, out var updated) ? updated : line)
                .Concat(createdByGroup.GetValueOrDefault(group.GroupKey) ?? []).ToArray()
        }).ToArray();
        var validationProducts = facts.SelectMany(group => group.Products).Concat(components)
            .Where(product => !product.IsDeleted).GroupBy(product => product.Uid)
            .ToDictionary(group => group.Key, group => group.Last());
        var structureError = _structureValidator.Validate(finalGroups, affectedKeys, validationProducts);
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

    /// <summary>新增共享组成固定写入标准有效标记，稳定父身份仅作为兼容来源，不代表唯一归属。</summary>
    private static Bom BuildBomEntity(BomDto dto, Product parent, Product child, int lineNumber, string sharedBomNo) => new()
    {
        BomNo = sharedBomNo,
        BomVersion = 1,
        ParentProductId = parent.Uid,
        ParentSkuSnapshot = parent.Sku,
        LineNumber = lineNumber,
        ComponentProductId = child.Uid,
        ComponentSkuSnapshot = child.Sku,
        Numerator = dto.Numerator,
        Denominator = dto.Denominator,
        Note = NormalizeNote(dto.Note)
    };

    /// <summary>显式复制候选物理行，避免映射配置复用原对象；编号、有效标记和来源审计保持原事实。</summary>
    private static Bom CopyBomForUpdate(Bom source) => new()
    {
        Uid = source.Uid, Version = source.Version, IsDeleted = source.IsDeleted,
        CreatedDate = source.CreatedDate, CreatedBy = source.CreatedBy,
        UpdatedDate = source.UpdatedDate, UpdatedBy = source.UpdatedBy,
        BomNo = source.BomNo, BomVersion = source.BomVersion,
        ParentProductId = source.ParentProductId, ParentSkuSnapshot = source.ParentSkuSnapshot,
        LineNumber = source.LineNumber, ComponentProductId = source.ComponentProductId,
        ComponentSkuSnapshot = source.ComponentSkuSnapshot, Numerator = source.Numerator,
        Denominator = source.Denominator, Note = source.Note
    };

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";
    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(x => x != Guid.Empty)
        .GroupBy(x => x)
        .FirstOrDefault(x => x.Count() > 1)
        ?.Key;

    /// <summary>任一行写入失败中止批次；仅按仓储明确版本错误码分类，其他失败保留业务阻断协议。</summary>
    private static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success)
        {
            var errorCode = result.ErrorCode is "CONCURRENCY_CONFLICT" or ServiceErrorCodes.VersionConflict
                ? ServiceErrorCodes.VersionConflict
                : ServiceErrorCodes.BusinessBlocked;
            throw new BomBatchWriteException(result.Message ?? fallbackMessage, errorCode);
        }
    }

    /// <summary>尽力回滚本层事务并返回是否成功；不用于推断提交结果，清理及诊断故障不覆盖原始结果。</summary>
    private bool TryRollbackTransaction(IDbTransaction? transaction)
    {
        try
        {
            if (transaction is null) return false;
            transaction.Rollback();
            return true;
        }
        catch (Exception rollbackException)
        {
            TryLogSaveFailure(LogLevel.Error, rollbackException, "BOM 批量保存事务回滚失败。");
            return false;
        }
    }

    /// <summary>保存失败及资源清理只尽力记录诊断，日志提供者异常不得中断回滚、释放或覆盖业务结果。</summary>
    private void TryLogSaveFailure(LogLevel level, Exception exception, string message)
    {
        try { _logger.Log(level, exception, "{Message}", message); }
        catch { /* 不通过同一失效日志管道再次记录，保留原始结果并继续清理。 */ }
    }

    /// <summary>将行写入失败提升为批量中止，携带已确认的版本冲突或业务阻断分类。</summary>
    private sealed class BomBatchWriteException(string message, string errorCode) : Exception(message)
    {
        /// <summary>服务边界使用的稳定错误码，不携带提供者异常细节。</summary>
        public string ErrorCode { get; } = errorCode;
    }

    /// <summary>保存准备结果，区分明确版本冲突与其他业务校验失败，不暴露数据库异常。</summary>
    private sealed class BomBatchPreparation
    {
        public string? Error { get; private init; }
        public string ErrorCode { get; private init; } = ServiceErrorCodes.BusinessBlocked;
        public IReadOnlyList<(CreateItemRequest<BomDto> Request, Bom Entity)> CreateItems { get; private init; } = [];
        public IReadOnlyList<(UpdateItemRequest<BomDto> Request, Bom Entity)> UpdateItems { get; private init; } = [];

        /// <summary>创建准备失败结果；未显式分类的规则失败仍为业务阻断。</summary>
        public static BomBatchPreparation Fail(string error, string errorCode = ServiceErrorCodes.BusinessBlocked)
            => new() { Error = error, ErrorCode = errorCode };
        /// <summary>携带已校验的新增与修改实体，后续写入仍由最外层事务拥有者执行。</summary>
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
