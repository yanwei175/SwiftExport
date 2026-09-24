using SwiftExport.Contracts.DTOs.Errors;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>提供库存查询与命令编排；供应商虚拟仓写操作在同一事务内完成校验、变更和审计。</summary>
public sealed class InventoryService(
    ServiceBaseContext context, IWarehouseSiteRepository warehouseSites, IInventoryRepository inventory,
    IBusinessOperationLogRepository operationLogs, ICorrelationContext correlation,
    ICurrentUser currentUser, IUserPermissionService permissions) : IInventoryService
{
    private const string Module = "Inventory";
    private const string ReadPermissionDeniedMessage = "当前账号无库存模块访问权限。";
    private const string ManagePermissionDeniedMessage = "当前账号无供应商虚拟仓管理权限。";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<InventoryService> _logger = context.LoggerFactory.CreateLogger<InventoryService>();
    private readonly IUserPermissionService _permissions = permissions;

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            return ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Fail("仓库选择查询条件无效。");
        return await ReadWarehouseAsync(
            () => warehouseSites.GetPickerPagedAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, cancellationToken), cancellationToken, "查询仓库选择候选失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchWarehouseSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 100 || request.Keyword?.Length > 100)
            return ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>.Fail("供应商查询条件无效。");
        return await ReadWarehouseAsync(
            () => warehouseSites.SearchSupplierLookupAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, cancellationToken), cancellationToken, "查询供应商候选失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 100)
            return ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Fail("虚拟仓查询条件无效。");
        return await ReadWarehouseAsync(() => warehouseSites.SearchAsync(request, cancellationToken), cancellationToken, "查询供应商虚拟仓失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default) =>
        uid == Guid.Empty
            ? ServiceResult<WarehouseSiteDto>.Fail("虚拟仓 Uid 不能为空。")
            : await ReadWarehouseAsync(async () => await warehouseSites.GetDtoAsync(uid, cancellationToken)
                ?? throw new InventoryPostingException("虚拟仓不存在。"), cancellationToken, "加载虚拟仓失败。");
    public async Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            return ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Fail("库存余额查询条件无效。");
        return await ReadWarehouseAsync(() => inventory.SearchBalancesAsync(request, cancellationToken), cancellationToken, "查询库存余额失败。");
    }

    public async Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200
            || request.SourceDocumentNo?.Length > 100 || request.OccurredDateFrom > request.OccurredDateTo)
            return ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Fail("库存流水查询条件无效。");
        return await ReadWarehouseAsync(() => inventory.SearchLedgerAsync(request, cancellationToken), cancellationToken, "查询库存流水失败。");
    }

    public async Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || pageIndex < 1 || pageSize is < 1 or > 200)
            return ServiceResult<InventoryBalanceTraceDto>.Fail("库存余额 Uid 或追溯分页参数无效。");
        return await ReadWarehouseAsync(async () =>
        {
            var balance = await inventory.GetBalanceDtoAsync(uid, cancellationToken)
                ?? throw new InventoryPostingException("库存余额不存在。");
            var ledgerPage = await inventory.GetLedgerByBalanceAsync(uid, pageIndex, pageSize, cancellationToken);
            return new InventoryBalanceTraceDto
            {
                Balance = balance,
                Ledgers = ledgerPage.Items,
                TotalCount = ledgerPage.TotalCount,
                PageIndex = ledgerPage.PageIndex,
                PageSize = ledgerPage.PageSize,
                Reconciliation = await inventory.GetReconciliationAsync(uid, cancellationToken)
                    ?? throw new InventoryPostingException("库存对账信息不存在。")
            };
        }, cancellationToken, "加载库存追溯失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateWarehouse(command);
        if (validation is not null) return ServiceResult<WarehouseSiteDto>.Fail(validation);
        if (!await HasWarehouseManagePermissionAsync(cancellationToken)) return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
        var code = command.Code.Trim();
        var name = command.Name.Trim();
        var fingerprint = Fingerprint("create", command.SupplierId, code, name);
        return await ExecuteWarehouseCommandAsync(command.CommandId, "WarehouseSite.Created", fingerprint, async () =>
        {
            var supplier = await warehouseSites.GetActiveSupplierAsync(command.SupplierId, cancellationToken)
                ?? throw new InventoryPostingException("供应商不存在、已停用或不允许新业务引用。");
            if (await warehouseSites.FindByCodeForUpdateAsync(code) is not null)
                throw new InventoryPostingException("虚拟仓代码已存在。");
            if (await warehouseSites.FindActiveSupplierWarehouseForUpdateAsync(command.SupplierId) is not null)
                throw new InventoryPostingException("该供应商已存在活动虚拟仓。");
            var site = new WarehouseSite
            {
                Uid = GuidSequentialGenerator.NewGuid(), Code = code, Name = name,
                SiteType = WarehouseSiteTypeEnum.SupplierVirtual, SupplierId = command.SupplierId,
                SupplierCodeSnapshot = supplier.SupplierCode, SupplierNameSnapshot = supplier.SupplierName, IsActive = true
            };
            if (await warehouseSites.InsertAsync(site) != 1)
                throw new InventoryPostingException("虚拟仓代码已存在，或该供应商已存在活动虚拟仓。");
            var result = await warehouseSites.GetDtoAsync(site.Uid, cancellationToken)
                ?? throw new InventoryPostingException("虚拟仓创建后无法读取。");
            return WarehouseCommandExecution.Create(result, null, result, null, $"创建虚拟仓 {site.Code}。");
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateUpdateWarehouse(uid, command);
        if (validation is not null) return ServiceResult<WarehouseSiteDto>.Fail(validation);
        if (!await HasWarehouseManagePermissionAsync(cancellationToken)) return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
        var name = command.Name.Trim();
        var fingerprint = Fingerprint("update", uid, command.ExpectedVersion, name);
        return await ExecuteWarehouseCommandAsync(command.CommandId, "WarehouseSite.Updated", fingerprint, async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
            if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            var before = ToDto(site);
            site.Name = name;
            if (await warehouseSites.UpdateNameAsync(site, command.ExpectedVersion) != 1)
                throw new InventoryPostingException("虚拟仓保存发生并发冲突。");
            var result = await warehouseSites.GetDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("虚拟仓不存在。");
            return WarehouseCommandExecution.Create(result, before, result, null, $"虚拟仓改名为 {name}。");
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateReasonCommand(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0, command?.Reason, "停用");
        if (validation is not null) return ServiceResult<WarehouseSiteDto>.Fail(validation);
        if (!await HasWarehouseManagePermissionAsync(cancellationToken)) return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
        var reason = command!.Reason.Trim();
        var fingerprint = Fingerprint("deactivate", uid, command.ExpectedVersion, reason);
        return await ExecuteWarehouseCommandAsync(command.CommandId, "WarehouseSite.Deactivated", fingerprint, async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
            if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            if (!site.IsActive) throw new InventoryPostingException("虚拟仓已经停用。");
            if (await warehouseSites.HasBalanceOrLedgerAsync(uid)) throw new InventoryPostingException("虚拟仓存在库存余额或历史流水，不能停用。");
            var before = ToDto(site);
            if (await warehouseSites.DeactivateAsync(site, command.ExpectedVersion) != 1)
                throw new InventoryPostingException("虚拟仓停用发生并发冲突。");
            var result = await warehouseSites.GetDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("虚拟仓不存在。");
            return WarehouseCommandExecution.Create(result, before, result, reason, "虚拟仓已停用。");
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> ActivateWarehouseSiteAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateReasonCommand(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0, command?.Reason, "启用");
        if (validation is not null) return ServiceResult<WarehouseSiteDto>.Fail(validation);
        if (!await HasWarehouseManagePermissionAsync(cancellationToken)) return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
        var reason = command!.Reason.Trim();
        var fingerprint = Fingerprint("activate", uid, command.ExpectedVersion, reason);
        return await ExecuteWarehouseCommandAsync(command.CommandId, "WarehouseSite.Activated", fingerprint, async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid)
                ?? throw new InventoryPostingException("虚拟仓不存在或已删除，不能启用。");
            if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            if (site.IsActive) throw new InventoryPostingException("虚拟仓已经启用。");
            if (site.SupplierId is not { } supplierId)
                throw new InventoryPostingException("虚拟仓没有供应商归属，不能启用。");
            var activeSite = await warehouseSites.FindActiveSupplierWarehouseForUpdateAsync(supplierId);
            if (activeSite is not null && activeSite.Uid != site.Uid)
                throw new InventoryPostingException("该供应商已存在活动虚拟仓，不能重复启用。");
            var before = ToDto(site);
            if (await warehouseSites.ActivateAsync(site, command.ExpectedVersion) != 1)
                throw new InventoryPostingException("虚拟仓启用发生并发冲突，或该供应商已存在活动虚拟仓。");
            var result = await warehouseSites.GetDtoAsync(uid, cancellationToken)
                ?? throw new InventoryPostingException("虚拟仓启用后无法读取。");
            return WarehouseCommandExecution.Create(result, before, result, reason, "虚拟仓已启用。");
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> DeleteWarehouseSiteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateReasonCommand(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0, command?.Reason, "删除");
        if (validation is not null) return ServiceResult<WarehouseSiteDto>.Fail(validation);
        if (!await HasWarehouseManagePermissionAsync(cancellationToken)) return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
        var reason = command!.Reason.Trim();
        var fingerprint = Fingerprint("delete", uid, command.ExpectedVersion, reason);
        return await ExecuteWarehouseCommandAsync(command.CommandId, "WarehouseSite.Deleted", fingerprint, async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
            if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            var blockers = await warehouseSites.GetDeleteBlockersAsync(uid, cancellationToken);
            if (blockers.Count > 0) throw new InventoryPostingException($"虚拟仓存在{string.Join("、", blockers)}，不能删除。");
            var before = ToDto(site);
            if (await warehouseSites.DeleteAsync(uid, command.ExpectedVersion) != 1)
                throw new InventoryPostingException("虚拟仓删除发生并发冲突。");
            var result = new WarehouseSiteDto
            {
                Uid = uid, Version = site.Version + 1, Code = site.Code, Name = site.Name, SupplierId = site.SupplierId,
                SupplierCode = site.SupplierCodeSnapshot ?? string.Empty, SupplierName = site.SupplierNameSnapshot ?? string.Empty,
                IsActive = false, UpdatedDate = DateTime.Now
            };
            return WarehouseCommandExecution.Create(result, before, result, reason, "虚拟仓已删除。");
        }, cancellationToken);
    }

    /// <summary>在读取仓库管理资料前执行服务端访问授权；前端菜单权限不作为安全边界。</summary>
    private async Task<ServiceResult<T>> ReadWarehouseAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, string failure)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId
            || !await _permissions.HasPermissionAsync(userId, PermissionKeys.ModuleInventoryAccess, cancellationToken))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, ReadPermissionDeniedMessage);
        return await ReadAsync(action, failure);
    }

    /// <summary>校验当前真实用户是否拥有虚拟仓独立管理能力。</summary>
    private async Task<bool> HasWarehouseManagePermissionAsync(CancellationToken cancellationToken)
    {
        return currentUser.IsAuthenticated && currentUser.UserId is { } userId
            && await _permissions.HasPermissionAsync(userId, PermissionKeys.InventoryWarehouseManage, cancellationToken);
    }

    /// <summary>串行化同一命令，在同一事务中完成重放判断、仓库写入和业务日志，保证成功结果可恢复。</summary>
    private async Task<ServiceResult<WarehouseSiteDto>> ExecuteWarehouseCommandAsync(
        Guid commandId,
        string operationType,
        string fingerprint,
        Func<Task<WarehouseCommandExecution>> action,
        CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null) return ServiceResult<WarehouseSiteDto>.Fail("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            if (await warehouseSites.AcquireCommandLockAsync(commandId, cancellationToken) < 0)
                throw new InventoryPostingException("仓库命令正在执行，请稍后以同一命令标识重试。");
            var existing = await operationLogs.GetByModuleCommandAsync(Module, commandId);
            if (existing is not null)
            {
                if (!string.Equals(existing.OperationType, operationType, StringComparison.Ordinal)
                    || !TryReadFingerprint(existing.ChangedFieldsJson, out var savedFingerprint)
                    || !string.Equals(savedFingerprint, fingerprint, StringComparison.Ordinal)
                    || !TryReadWarehouseResult(existing.RelatedObjectsJson, out var savedResult))
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.CommandConflict, "COMMAND_CONFLICT：同一 CommandId 已用于不同仓库操作或不同内容。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                _session.Transaction.Commit();
                return ServiceResult<WarehouseSiteDto>.Ok(savedResult);
            }

            var execution = await action();
            await WriteWarehouseLogAsync(commandId, operationType, fingerprint, execution);
            cancellationToken.ThrowIfCancellationRequested();
            _session.Transaction.Commit();
            return ServiceResult<WarehouseSiteDto>.Ok(execution.Result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (InventoryPostingException exception)
        {
            _session.Transaction.Rollback();
            return ServiceResult<WarehouseSiteDto>.Fail(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            _session.Transaction.Rollback();
            _logger.LogError(exception, "虚拟仓命令 {OperationType}/{CommandId} 执行失败。", operationType, commandId);
            return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Unexpected, "虚拟仓命令执行失败。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>持久化仓库命令指纹、前后值和成功结果；该日志同时承担可靠重放记录。</summary>
    private async Task WriteWarehouseLogAsync(Guid commandId, string operationType, string fingerprint, WarehouseCommandExecution execution)
    {
        var changed = JsonSerializer.Serialize(new WarehouseCommandAuditEnvelope
        {
            Fingerprint = fingerprint, Before = execution.Before, After = execution.After
        });
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlation.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId, Module = Module, AggregateType = "WarehouseSite",
            AggregateUid = execution.Result.Uid, AggregateNoSnapshot = execution.Result.Code,
            OperationType = operationType, OperationName = GetWarehouseOperationName(operationType),
            Reason = execution.Reason, Summary = execution.Summary, ChangedFieldsJson = changed,
            RelatedObjectsJson = JsonSerializer.Serialize(execution.Result), UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1) throw new InventoryPostingException("业务操作日志写入失败。");
    }

    /// <summary>从仓库命令审计内容读取稳定指纹；旧日志缺少指纹时按冲突处理，避免猜测重放。</summary>
    private static bool TryReadFingerprint(string? json, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            fingerprint = JsonSerializer.Deserialize<WarehouseCommandAuditEnvelope>(json)?.Fingerprint ?? string.Empty;
            return fingerprint.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读取首次成功保存的完整仓库结果，支持删除响应丢失后的同意图重试。</summary>
    private static bool TryReadWarehouseResult(string? json, out WarehouseSiteDto result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            result = JsonSerializer.Deserialize<WarehouseSiteDto>(json)!;
            return result is not null && result.Uid != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>校验建仓命令的必填项和数据库列长度，不通过截断改变用户输入。</summary>
    private static string? ValidateCreateWarehouse(CreateSupplierVirtualWarehouseCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty || command.SupplierId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.Name))
            return "命令、供应商、仓库代码和名称不能为空。";
        if (command.Code.Trim().Length > 100) return "虚拟仓代码不能超过 100 个字符。";
        return command.Name.Trim().Length > 300 ? "虚拟仓名称不能超过 300 个字符。" : null;
    }

    /// <summary>校验改名命令的稳定目标、并发版本和名称长度。</summary>
    private static string? ValidateUpdateWarehouse(Guid uid, UpdateWarehouseSiteCommand command)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty
            || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.Name))
            return "虚拟仓、命令、版本和名称不能为空。";
        return command.Name.Trim().Length > 300 ? "虚拟仓名称不能超过 300 个字符。" : null;
    }

    /// <summary>校验启用、停用或删除命令的稳定目标、并发版本和真实原因。</summary>
    private static string? ValidateReasonCommand(Guid uid, Guid commandId, int expectedVersion, string? reason, string action)
    {
        if (uid == Guid.Empty || commandId == Guid.Empty || expectedVersion <= 0 || string.IsNullOrWhiteSpace(reason))
            return $"虚拟仓、命令、版本和{action}原因不能为空。";
        return reason.Trim().Length > WarehouseSiteLimits.ReasonMaxLength
            ? $"{action}原因不能超过 {WarehouseSiteLimits.ReasonMaxLength} 个字符。"
            : null;
    }

    /// <summary>生成规范化请求指纹；操作、目标、版本及全部有效输入均参与比较。</summary>
    private static string Fingerprint(params object?[] values)
    {
        // JSON 数组保留字段边界并转义控制字符，避免分隔符出现在用户输入时形成拼接碰撞。
        var canonical = JsonSerializer.Serialize(values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>把锁定的仓库实体转换为审计前值；供应商显示采用建仓时保存的权威快照。</summary>
    private static WarehouseSiteDto ToDto(WarehouseSite site) => new()
    {
        Uid = site.Uid, Version = site.Version, Code = site.Code, Name = site.Name, SupplierId = site.SupplierId,
        SupplierCode = site.SupplierCodeSnapshot ?? string.Empty, SupplierName = site.SupplierNameSnapshot ?? string.Empty,
        IsActive = site.IsActive, UpdatedDate = site.UpdatedDate
    };

    /// <summary>返回仓库业务日志的人类可读操作名称。</summary>
    private static string GetWarehouseOperationName(string operationType) => operationType switch
    {
        "WarehouseSite.Created" => "供应商虚拟仓已创建",
        "WarehouseSite.Updated" => "供应商虚拟仓已改名",
        "WarehouseSite.Deactivated" => "供应商虚拟仓已停用",
        "WarehouseSite.Activated" => "供应商虚拟仓已启用",
        "WarehouseSite.Deleted" => "供应商虚拟仓已删除",
        _ => "供应商虚拟仓已变更"
    };

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string failure)
    {
        try { return ServiceResult<T>.Ok(await action()); } catch (OperationCanceledException) { throw; } catch (InventoryPostingException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message); } catch (Exception ex) { _logger.LogError(ex, "{Failure}", failure); return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, failure); }
    }
    private static string? Normalize(string? text, int max) { var value = text?.Trim(); return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)]; }

    /// <summary>仓库日志中的命令指纹与前后值载体，仅用于服务端可靠重放。</summary>
    private sealed class WarehouseCommandAuditEnvelope
    {
        public string Fingerprint { get; set; } = string.Empty;
        public WarehouseSiteDto? Before { get; set; }
        public WarehouseSiteDto? After { get; set; }
    }

    /// <summary>聚合一次仓库写入的成功结果及审计信息。</summary>
    private sealed class WarehouseCommandExecution
    {
        public required WarehouseSiteDto Result { get; init; }
        public WarehouseSiteDto? Before { get; init; }
        public WarehouseSiteDto? After { get; init; }
        public string? Reason { get; init; }
        public required string Summary { get; init; }

        /// <summary>创建一次待原子写入日志的仓库命令结果。</summary>
        public static WarehouseCommandExecution Create(WarehouseSiteDto result, WarehouseSiteDto? before, WarehouseSiteDto? after, string? reason, string summary) => new()
        {
            Result = result, Before = before, After = after, Reason = reason, Summary = summary
        };
    }
}
