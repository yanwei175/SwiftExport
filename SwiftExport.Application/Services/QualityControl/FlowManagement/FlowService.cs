using System.Data;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.QualityControl.FlowManagement;

namespace SwiftExport.Application.Services.QualityControl.FlowManagement;

/// <summary>管理完整流量测试组；以头版本和头锁串行化同组变更，统一校验历史快照、功能唯一性及行归属。</summary>
public sealed class FlowService(IFlowRepository repository, ServiceBaseContext context,
    IPermissionEvaluationService permissions, ICurrentUser user) : IFlowService
{
    private readonly IDbSession _session = context.Session;
    private readonly ILogger<FlowService> _logger = context.LoggerFactory.CreateLogger<FlowService>();

    /// <summary>授权后分页查询历史测试组，验证分页与日期范围，读取不创建事务。</summary>
    public async Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync(FlowSearchRequest request, CancellationToken cancellationToken = default)
    {
        var auth = await AuthorizationErrorAsync(PermissionKeys.QualityControlFlowView, cancellationToken);
        if (auth is not null) return ServiceResult<PagedResponseBase<FlowListItemDto>>.Fail(auth, "当前账号不能查询流量测试组。");
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 500 ||
            request.MaterialId == Guid.Empty || request.TestDateFrom?.Date > request.TestDateTo?.Date)
            return ServiceResult<PagedResponseBase<FlowListItemDto>>.Fail(ServiceErrorCodes.Validation, "分页、物料或测试日期范围无效。");
        return ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(await repository.GetPagedAsync(request, cancellationToken));
    }

    /// <summary>直接读取保存的快照，不因当前物料或字典改名、停用而过滤历史。</summary>
    public async Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = await AuthorizationErrorAsync(PermissionKeys.QualityControlFlowView, cancellationToken);
        if (auth is not null) return ServiceResult<FlowDetailDto>.Fail(auth, "当前账号不能查询流量测试组。");
        if (id == Guid.Empty) return ServiceResult<FlowDetailDto>.Fail(ServiceErrorCodes.Validation, "测试组标识不能为空。");
        var detail = await repository.GetDetailAsync(id, cancellationToken);
        return detail is null ? ServiceResult<FlowDetailDto>.Fail(ServiceErrorCodes.NotFound, "测试组不存在或已删除。") :
            ServiceResult<FlowDetailDto>.Ok(detail);
    }

    /// <summary>新增完整测试组，只接受有效物料和启用字典项，所有快照从服务器读取。</summary>
    public async Task<ServiceResult<FlowSaveResponse>> CreateAsync(CreateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default)
    {
        var auth = await AuthorizationErrorAsync(PermissionKeys.QualityControlFlowCreate, cancellationToken);
        if (auth is not null) return ServiceResult<FlowSaveResponse>.Fail(auth, "当前账号不能新增流量测试组。");
        var validation = ValidateEdit(request?.Data, true);
        if (validation is not null) return ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.Validation, validation);
        return await WriteAsync(async () =>
        {
            var dto = request!.Data;
            var material = await repository.ReadMaterialAsync(dto.MaterialId, cancellationToken);
            if (material is null || material.IsDeleted || !material.IsActive || string.IsNullOrWhiteSpace(material.Sku))
                throw new FlowWriteException(ServiceErrorCodes.Validation, "请选择未删除且启用的有效物料。", "Data.MaterialId");
            var dictionary = await LoadSelectedDictionaryAsync(dto.Lines, [], cancellationToken);
            var header = dto.Adapt<FlowHeader>();
            header.TestDate = dto.TestDate.Date;
            header.SkuSnapshot = material.Sku;
            var lines = dto.Lines.Select(input => BuildLine(input, null, dictionary)).ToList();
            var result = await repository.AddHeaderAsync(header, cancellationToken);
            EnsureWrite(result);
            foreach (var line in lines)
            {
                line.HeaderId = header.Uid;
                EnsureWrite(await repository.AddLineAsync(line, cancellationToken));
            }
            return new FlowSaveResponse { HeaderResult = SaveItemResultFactory.FromCreate(request, result), Detail = Detail(header, lines) };
        }, cancellationToken);
    }

    /// <summary>锁头并校验所有版本和归属后提交最终行集合；遗漏旧行软删除，换功能的行按替换处理以避免唯一键交换冲突。</summary>
    public async Task<ServiceResult<FlowSaveResponse>> UpdateAsync(UpdateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default)
    {
        var auth = await AuthorizationErrorAsync(PermissionKeys.QualityControlFlowEdit, cancellationToken);
        if (auth is not null) return ServiceResult<FlowSaveResponse>.Fail(auth, "当前账号不能修改流量测试组。");
        if (request is null || request.Uid == Guid.Empty || request.Version < 1)
            return ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.Validation, "测试组身份或版本无效。");
        var validation = ValidateEdit(request.Data, false);
        if (validation is not null) return ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.Validation, validation);
        return await WriteAsync(async () =>
        {
            var header = await ReadExpectedHeaderAsync(request.Uid, request.Version, cancellationToken);
            if (header.MaterialId != request.Data.MaterialId)
                throw new FlowWriteException(ServiceErrorCodes.Validation, "测试组创建后不能更换物料，请另建测试组。", "Data.MaterialId");
            var oldLines = await repository.ReadLinesAsync(header.Uid, cancellationToken);
            var oldById = oldLines.ToDictionary(x => x.Uid);
            foreach (var input in request.Data.Lines.Where(x => x.Uid != Guid.Empty))
            {
                if (!oldById.TryGetValue(input.Uid, out var old))
                    throw new FlowWriteException(ServiceErrorCodes.Validation, "提交的行不属于本测试组或已删除。", "Data.Lines");
                if (old.Version != input.Version)
                    throw new FlowWriteException(ServiceErrorCodes.ConcurrencyConflict, "功能行已被修改，请刷新后重试。", "Data.Lines");
            }
            var dictionary = await LoadSelectedDictionaryAsync(request.Data.Lines, oldLines, cancellationToken);
            var finalLines = request.Data.Lines.Select(input =>
                BuildLine(input, oldById.GetValueOrDefault(input.Uid), dictionary)).ToList();
            var retainedIds = finalLines.Where(x => x.Uid != Guid.Empty).Select(x => x.Uid).ToHashSet();
            // 先删除缺失及换功能的旧行，再更新和新增；交换功能不会触发中间态唯一键冲突。
            foreach (var old in oldLines.Where(x => !retainedIds.Contains(x.Uid)).OrderBy(x => x.Uid))
                EnsureWrite(await repository.DeleteLineAsync(old.Uid, old.Version, cancellationToken));
            request.Data.Adapt(header);
            header.TestDate = request.Data.TestDate.Date;
            header.Version = request.Version;
            var result = await repository.UpdateHeaderAsync(header, cancellationToken);
            EnsureWrite(result);
            foreach (var line in finalLines.Where(x => x.Uid != Guid.Empty).OrderBy(x => x.Uid))
                EnsureWrite(await repository.UpdateLineAsync(line, cancellationToken));
            foreach (var line in finalLines.Where(x => x.Uid == Guid.Empty))
            {
                line.HeaderId = header.Uid;
                EnsureWrite(await repository.AddLineAsync(line, cancellationToken));
            }
            return new FlowSaveResponse { HeaderResult = SaveItemResultFactory.FromUpdate(request, result), Detail = Detail(header, finalLines) };
        }, cancellationToken);
    }

    /// <summary>按头版本原子软删除整组，忽略外部物理删除请求，保留历史事实。</summary>
    public async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest request, CancellationToken cancellationToken = default)
    {
        var auth = await AuthorizationErrorAsync(PermissionKeys.QualityControlFlowDelete, cancellationToken);
        if (auth is not null) return ServiceResult<SaveItemResult>.Fail(auth, "当前账号不能删除流量测试组。");
        if (request is null || request.Uid == Guid.Empty || request.Version < 1)
            return ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.Validation, "测试组身份或版本无效。");
        return await WriteAsync(async () =>
        {
            var header = await ReadExpectedHeaderAsync(request.Uid, request.Version, cancellationToken);
            var lines = await repository.ReadLinesAsync(header.Uid, cancellationToken);
            foreach (var line in lines.OrderBy(x => x.Uid))
                EnsureWrite(await repository.DeleteLineAsync(line.Uid, line.Version, cancellationToken));
            var result = await repository.DeleteHeaderAsync(header.Uid, request.Version, cancellationToken);
            EnsureWrite(result);
            return SaveItemResultFactory.FromDelete(request, result);
        }, cancellationToken);
    }

    /// <summary>检查最终组的结构和测量值；新行与旧行的身份语义不同，空值与零值严格区分。</summary>
    private static string? ValidateEdit(FlowEditDto? dto, bool create)
    {
        if (dto is null || dto.MaterialId == Guid.Empty || dto.TestDate == default) return "物料和测试日期不能为空。";
        if (dto.Lines is null || dto.Lines.Count == 0) return "每个测试组至少需要一个功能行。";
        var functions = new HashSet<Guid>();
        var ids = new HashSet<Guid>();
        foreach (var line in dto.Lines)
        {
            if (line is null) return "功能行不能包含空项。";
            if (line.FunctionId == Guid.Empty || line.LimiterSpecificationId == Guid.Empty) return "功能和限流器规格必须从字典选择。";
            if (!functions.Add(line.FunctionId)) return "同一测试组内不能重复记录同一个功能。";
            if (line.SortNo < 0) return "行排序号不能为负数。";
            if ((create && line.Uid != Guid.Empty) || (line.Uid == Guid.Empty && line.Version != 0) ||
                (line.Uid != Guid.Empty && (line.Version < 1 || !ids.Add(line.Uid))))
                return "功能行身份或版本无效；新行标识应为空且版本为零。";
            decimal?[] measurements = [line.Flow1BarLMin, line.Flow2BarLMin, line.Flow3BarLMin, line.Flow4BarLMin, line.Flow5BarLMin, line.Flow6BarLMin, line.Flow7BarLMin, line.Flow8BarLMin, line.Flow20PsiGpm, line.Flow40PsiGpm, line.Flow60PsiGpm, line.Flow80PsiGpm, line.Flow100PsiGpm];
            if (!measurements.Any(x => x.HasValue)) return "每个功能行至少需要一个实测流量。";
            if (measurements.Any(x => x < 0)) return "实测流量不能为负数。";
            // 沿用项目 numeric(18,4) 精度；拒绝数据库会舍入或溢出的值，避免回执与实存结果不同。
            if (measurements.Any(x => x.HasValue && (decimal.Round(x.Value, 4) != x.Value || x.Value >= 100000000000000m)))
                return "实测流量最多保留四位小数，且必须小于 100000000000000。";
        }
        return null;
    }

    /// <summary>只对新增或更换的字典项校验当前准入；既有未变项即使停用，也保留历史名称及可编辑结果。</summary>
    private async Task<IReadOnlyDictionary<Guid, SysDictData>> LoadSelectedDictionaryAsync(
        IReadOnlyList<FlowLineEditDto> inputs, IReadOnlyList<FlowLine> oldLines, CancellationToken token)
    {
        var oldById = oldLines.ToDictionary(x => x.Uid);
        var requirements = new List<(Guid Id, string Code)>();
        foreach (var input in inputs)
        {
            var old = oldById.GetValueOrDefault(input.Uid);
            if (old?.FunctionId != input.FunctionId) requirements.Add((input.FunctionId, DictCodes.FlowTestFunction));
            if (old?.LimiterSpecificationId != input.LimiterSpecificationId) requirements.Add((input.LimiterSpecificationId, DictCodes.FlowLimiterSpecification));
        }
        var dictionary = (await repository.ReadDictionaryItemsAsync(requirements.Select(x => x.Id), token)).ToDictionary(x => x.Uid);
        foreach (var (id, code) in requirements)
            if (!dictionary.TryGetValue(id, out var item) || item.IsDeleted || !item.IsEnabled ||
                !string.Equals(item.DictCode?.Trim(), code, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(DictionaryName(item)))
                throw new FlowWriteException(ServiceErrorCodes.Validation, $"所选字典项不存在、已停用或不属于 {code}。", "Data.Lines");
        return dictionary;
    }

    /// <summary>映射可编辑测量字段；名称仅在采用不同字典项时刷新，快照不从客户端接收。</summary>
    private static FlowLine BuildLine(FlowLineEditDto input, FlowLine? old, IReadOnlyDictionary<Guid, SysDictData> dictionary)
    {
        var line = old is null ? input.Adapt<FlowLine>() : old.Adapt<FlowLine>();
        input.Adapt(line);
        line.FunctionNameSnapshot = old?.FunctionId == input.FunctionId
            ? old.FunctionNameSnapshot : DictionaryName(dictionary[input.FunctionId]);
        line.LimiterSpecificationNameSnapshot = old?.LimiterSpecificationId == input.LimiterSpecificationId
            ? old.LimiterSpecificationNameSnapshot : DictionaryName(dictionary[input.LimiterSpecificationId]);
        if (old is null || old.FunctionId != input.FunctionId)
        {
            line.Uid = Guid.Empty;
            line.Version = 1;
            line.CreatedDate = null;
            line.CreatedBy = null;
            line.UpdatedDate = null;
            line.UpdatedBy = null;
        }
        return line;
    }

    /// <summary>字典中文显示名优先，未提供中文时采用英文，避免保存空快照。</summary>
    private static string DictionaryName(SysDictData item) =>
        (string.IsNullOrWhiteSpace(item.DataNameZh) ? item.DataNameEn : item.DataNameZh)?.Trim() ?? string.Empty;

    /// <summary>返回头及行的持久化版本与审计，排序稳定，结果不读取当前字典名称。</summary>
    private static FlowDetailDto Detail(FlowHeader header, IReadOnlyList<FlowLine> lines)
    {
        var detail = header.Adapt<FlowDetailDto>();
        detail.Lines = lines.OrderBy(x => x.SortNo).ThenBy(x => x.Uid).Adapt<List<FlowLineDto>>();
        return detail;
    }

    /// <summary>锁定未删除头后检查预期版本；外层用例在所有行变更前调用。</summary>
    private async Task<FlowHeader> ReadExpectedHeaderAsync(Guid id, int version, CancellationToken token)
    {
        var header = await repository.ReadHeaderAsync(id, true, token);
        if (header is null) throw new FlowWriteException(ServiceErrorCodes.NotFound, "测试组不存在或已删除。");
        if (header.Version != version) throw new FlowWriteException(ServiceErrorCodes.ConcurrencyConflict, "测试组已被修改，请刷新后重试。");
        return header;
    }

    /// <summary>区分身份缺失与业务权限不足，拒绝路径不访问仓储或开启事务。</summary>
    private async Task<string?> AuthorizationErrorAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || !user.UserId.HasValue) return ServiceErrorCodes.Unauthenticated;
        return await permissions.HasPermissionAsync(user.UserId.Value, key, token) ? null : ServiceErrorCodes.Forbidden;
    }

    /// <summary>本服务私有的整组事务边界；ReadCommitted 配合头锁与字典共享锁，失败全量回滚且不接管外层事务。</summary>
    private async Task<ServiceResult<T>> WriteAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        if (_session.Transaction is not null) return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "流量管理不能接管外部事务。");
        IDbTransaction? owned = null;
        var committing = false;
        try
        {
            token.ThrowIfCancellationRequested();
            owned = _session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            _session.Transaction = owned;
            var result = await action();
            token.ThrowIfCancellationRequested();
            committing = true;
            owned.Commit();
            return ServiceResult<T>.Ok(result);
        }
        catch (FlowWriteException exception)
        {
            Rollback(owned);
            return ServiceResult<T>.Fail(exception.Code, exception.Message, exception.Field);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !committing)
        {
            Rollback(owned);
            throw;
        }
        catch (Exception exception)
        {
            Rollback(owned);
            _logger.LogError(exception, "流量测试组保存失败。");
            var kind = _session.Errors.Classify(exception);
            var code = committing ? ServiceErrorCodes.OutcomeUnknown :
                kind is DatabaseFailureKind.SerializationFailure or DatabaseFailureKind.Deadlock ? ServiceErrorCodes.ServiceUnavailable :
                kind == DatabaseFailureKind.UniqueConflict ? ServiceErrorCodes.BusinessBlocked : ServiceErrorCodes.Unexpected;
            var message = committing ? "提交结果尚不确定，请查询测试组核实后再操作。" :
                code == ServiceErrorCodes.ServiceUnavailable ? "数据发生并发变化，请刷新后重试。" :
                code == ServiceErrorCodes.BusinessBlocked ? "本组功能已存在，请刷新后核对。" : "保存失败，未提交的头行操作已回滚。";
            return ServiceResult<T>.Fail(code, message);
        }
        finally
        {
            if (owned is not null)
            {
                if (ReferenceEquals(_session.Transaction, owned)) _session.Transaction = null;
                try { owned.Dispose(); }
                catch (Exception exception) { _logger.LogError(exception, "流量事务释放失败。"); }
            }
        }
    }

    /// <summary>回滚清理失败只记录日志，不能覆盖原始业务异常或取消。</summary>
    private void Rollback(IDbTransaction? transaction)
    {
        try { transaction?.Rollback(); }
        catch (Exception exception) { _logger.LogError(exception, "流量事务回滚失败。"); }
    }

    /// <summary>任一头行写入失败即阻断整组提交，不返回部分成功结果。</summary>
    private static void EnsureWrite(RepositoryWriteResult result)
    {
        if (!result.Success) throw new FlowWriteException(result.ErrorCode ?? ServiceErrorCodes.WriteFailed, result.Message ?? "流量记录写入失败。");
    }

    /// <summary>携带稳定错误码和字段路径的用例失败，用于整体回滚。</summary>
    private sealed class FlowWriteException(string code, string message, string? field = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Field { get; } = field;
    }
}

