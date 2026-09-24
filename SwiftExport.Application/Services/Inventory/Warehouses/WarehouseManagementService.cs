using SwiftExport.Application.Services.Inventory.Posting;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
namespace SwiftExport.Application.Services.Inventory.Warehouses;

/// <summary>处理供应商虚拟仓创建、改名、启停和删除；公共命令组件持有事务和回执，业务审计只记录变更。</summary>
public sealed class WarehouseManagementService(
    ServiceBaseContext context, IWarehouseManagementRepository warehouseSites, IWarehouseQueryRepository warehouseQueries,
    IBusinessOperationLogRepository operationLogs, ICorrelationContext correlation,
    ICurrentUser currentUser, IUserPermissionService permissions,
    CommandPipeline pipeline, BusinessCommandExecutor executor) : IWarehouseManagementService
{
    private const string Module = "Inventory";
    private const string CreateCommandType = "Inventory.Warehouse.Create";
    private const string UpdateCommandType = "Inventory.Warehouse.Update";
    private const string DeactivateCommandType = "Inventory.Warehouse.Deactivate";
    private const string ActivateCommandType = "Inventory.Warehouse.Activate";
    private const string DeleteCommandType = "Inventory.Warehouse.Delete";
    private const string ManagePermissionDeniedMessage = "当前账号无供应商虚拟仓管理权限。";
    private readonly ILogger<WarehouseManagementService> _logger = context.LoggerFactory.CreateLogger<WarehouseManagementService>();

    /// <inheritdoc/>
    public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default) =>
        RunCommandAsync(() => pipeline.ExecuteAsync<CreateSupplierVirtualWarehouseCommand, WarehouseSiteDto>(
            command, command?.CommandId, CreateCommandType, AuthorizeAsync,
            token => ExecuteCreateCommandAsync(command!, token), cancellationToken));

    /// <summary>以规范化建仓输入取得公共回执；新仓库身份仅在首次执行的事务中生成。</summary>
    private async Task<ServiceResult<CommandExecutionResult<WarehouseSiteDto>>> ExecuteCreateCommandAsync(
        CreateSupplierVirtualWarehouseCommand command, CancellationToken token) =>
        ServiceResult<CommandExecutionResult<WarehouseSiteDto>>.Ok(await executor.ExecuteAsync(command.CommandId, CreateCommandType, null,
            new { command.SupplierId, Code = command.Code.Trim(), Name = command.Name.Trim() },
            ct => CreateInTransactionAsync(command, ct), token));

    /// <summary>锁定供应商和唯一性范围，创建虚拟仓并在同一事务内记录业务前后值。</summary>
    private async Task<WarehouseSiteDto> CreateInTransactionAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code.Trim();
        var name = command.Name.Trim();
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
        var result = await warehouseQueries.GetDtoAsync(site.Uid, cancellationToken)
            ?? throw new InventoryPostingException("虚拟仓创建后无法读取。");
        await WriteWarehouseLogAsync(command.CommandId, "WarehouseSite.Created", result, null, result, null, $"创建虚拟仓 {site.Code}。");
        return result;
    }

    /// <inheritdoc/>
    public Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default) =>
        RunCommandAsync(() => pipeline.ExecuteAsync<UpdateWarehouseSiteCommand, WarehouseSiteDto>(
            command, command?.CommandId, UpdateCommandType, AuthorizeAsync,
            token => ExecuteUpdateCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>校验路由身份，并用目标、版本和规范化名称识别改名命令。</summary>
    private async Task<ServiceResult<CommandExecutionResult<WarehouseSiteDto>>> ExecuteUpdateCommandAsync(
        Guid uid, UpdateWarehouseSiteCommand command, CancellationToken token)
    {
        RequireWarehouseUid(uid);
        var result = await executor.ExecuteAsync(command.CommandId, UpdateCommandType, uid,
            new { command.ExpectedVersion, Name = command.Name.Trim() },
            ct => UpdateInTransactionAsync(uid, command, ct), token);
        return ServiceResult<CommandExecutionResult<WarehouseSiteDto>>.Ok(result);
    }

    /// <summary>锁定仓库并重验版本，写入新名称和业务审计。</summary>
    private async Task<WarehouseSiteDto> UpdateInTransactionAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
        if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
        var before = ToDto(site);
        site.Name = name;
        if (await warehouseSites.UpdateNameAsync(site, command.ExpectedVersion) != 1)
            throw new InventoryPostingException("虚拟仓保存发生并发冲突。");
        var result = await warehouseQueries.GetDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("虚拟仓不存在。");
        await WriteWarehouseLogAsync(command.CommandId, "WarehouseSite.Updated", result, before, result, null, $"虚拟仓改名为 {name}。");
        return result;
    }

    /// <inheritdoc/>
    public Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) =>
        RunCommandAsync(() => pipeline.ExecuteAsync<DeactivateWarehouseSiteCommand, WarehouseSiteDto>(
            command, command?.CommandId, DeactivateCommandType, AuthorizeAsync,
            token => ExecuteDeactivateCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>以仓库、版本和规范化停用原因取得回执；路由身份在事务前校验。</summary>
    private async Task<ServiceResult<CommandExecutionResult<WarehouseSiteDto>>> ExecuteDeactivateCommandAsync(
        Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken token)
    {
        RequireWarehouseUid(uid, "停用");
        var result = await executor.ExecuteAsync(command.CommandId, DeactivateCommandType, uid,
            new { command.ExpectedVersion, Reason = command.Reason.Trim() },
            ct => DeactivateInTransactionAsync(uid, command, ct), token);
        return ServiceResult<CommandExecutionResult<WarehouseSiteDto>>.Ok(result);
    }

    /// <summary>锁定仓库并检查版本、状态与库存历史，停用后写入审计。</summary>
    private async Task<WarehouseSiteDto> DeactivateInTransactionAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
        var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
        if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
        if (!site.IsActive) throw new InventoryPostingException("虚拟仓已经停用。");
        if (await warehouseSites.HasBalanceOrLedgerAsync(uid)) throw new InventoryPostingException("虚拟仓存在库存余额或历史流水，不能停用。");
        var before = ToDto(site);
        if (await warehouseSites.DeactivateAsync(site, command.ExpectedVersion) != 1)
            throw new InventoryPostingException("虚拟仓停用发生并发冲突。");
        var result = await warehouseQueries.GetDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("虚拟仓不存在。");
        await WriteWarehouseLogAsync(command.CommandId, "WarehouseSite.Deactivated", result, before, result, reason, "虚拟仓已停用。");
        return result;
    }

    /// <inheritdoc/>
    public Task<ServiceResult<WarehouseSiteDto>> ActivateWarehouseSiteAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) =>
        RunCommandAsync(() => pipeline.ExecuteAsync<ActivateWarehouseSiteCommand, WarehouseSiteDto>(
            command, command?.CommandId, ActivateCommandType, AuthorizeAsync,
            token => ExecuteActivateCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>以仓库、版本和规范化启用原因取得公共回执。</summary>
    private async Task<ServiceResult<CommandExecutionResult<WarehouseSiteDto>>> ExecuteActivateCommandAsync(
        Guid uid, ActivateWarehouseSiteCommand command, CancellationToken token)
    {
        RequireWarehouseUid(uid, "启用");
        var result = await executor.ExecuteAsync(command.CommandId, ActivateCommandType, uid,
            new { command.ExpectedVersion, Reason = command.Reason.Trim() },
            ct => ActivateInTransactionAsync(uid, command, ct), token);
        return ServiceResult<CommandExecutionResult<WarehouseSiteDto>>.Ok(result);
    }

    /// <summary>锁定仓库并重验版本和供应商活动仓唯一性，启用后写入审计。</summary>
    private async Task<WarehouseSiteDto> ActivateInTransactionAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
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
        var result = await warehouseQueries.GetDtoAsync(uid, cancellationToken)
            ?? throw new InventoryPostingException("虚拟仓启用后无法读取。");
        await WriteWarehouseLogAsync(command.CommandId, "WarehouseSite.Activated", result, before, result, reason, "虚拟仓已启用。");
        return result;
    }

    /// <inheritdoc/>
    public Task<ServiceResult<WarehouseSiteDto>> DeleteWarehouseSiteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default) =>
        RunCommandAsync(() => pipeline.ExecuteAsync<DeleteWarehouseSiteCommand, WarehouseSiteDto>(
            command, command?.CommandId, DeleteCommandType, AuthorizeAsync,
            token => ExecuteDeleteCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>以仓库、版本和规范化删除原因取得公共回执。</summary>
    private async Task<ServiceResult<CommandExecutionResult<WarehouseSiteDto>>> ExecuteDeleteCommandAsync(
        Guid uid, DeleteWarehouseSiteCommand command, CancellationToken token)
    {
        RequireWarehouseUid(uid, "删除");
        var result = await executor.ExecuteAsync(command.CommandId, DeleteCommandType, uid,
            new { command.ExpectedVersion, Reason = command.Reason.Trim() },
            ct => DeleteInTransactionAsync(uid, command, ct), token);
        return ServiceResult<CommandExecutionResult<WarehouseSiteDto>>.Ok(result);
    }

    /// <summary>锁定仓库并重验版本和全部业务引用，软删后记录审计与首次结果。</summary>
    private async Task<WarehouseSiteDto> DeleteInTransactionAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
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
        await WriteWarehouseLogAsync(command.CommandId, "WarehouseSite.Deleted", result, before, result, reason, "虚拟仓已删除。");
        return result;
    }

    /// <summary>先核对独立仓库管理权限，避免未授权请求读取验证细节或开启事务。</summary>
    private async Task<ServiceResult<WarehouseSiteDto>?> AuthorizeAsync(CancellationToken token)
    {
        return currentUser.IsAuthenticated && currentUser.UserId is { } userId
            && await permissions.HasPermissionAsync(userId, PermissionKeys.InventoryWarehouseManage, token)
            ? null : ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, ManagePermissionDeniedMessage);
    }

    /// <summary>将公共管道的验证与执行异常转换为原有仓库服务结果；不接管事务。</summary>
    private async Task<ServiceResult<WarehouseSiteDto>> RunCommandAsync(Func<Task<ServiceResult<WarehouseSiteDto>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex) { return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<WarehouseSiteDto>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (InventoryPostingException ex) { return ServiceResult<WarehouseSiteDto>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "虚拟仓命令执行失败。");
            return ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Unexpected, "虚拟仓命令执行失败。");
        }
    }

    /// <summary>拒绝空路由身份，保留仓库命令的输入语义。</summary>
    private static void RequireWarehouseUid(Guid uid, string? action = null)
    {
        if (uid == Guid.Empty)
            throw new BusinessCommandException(ServiceErrorCodes.Validation,
                action is null ? "虚拟仓、命令、版本和名称不能为空。" : $"虚拟仓、命令、版本和{action}原因不能为空。", "Uid");
    }

    /// <summary>在执行器事务内记录业务前后值、原因、操作者和链路标识，不保存回放结果。</summary>
    private async Task WriteWarehouseLogAsync(Guid commandId, string operationType, WarehouseSiteDto result,
        WarehouseSiteDto? before, WarehouseSiteDto? after, string? reason, string summary)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlation.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId, Module = Module, AggregateType = "WarehouseSite",
            AggregateUid = result.Uid, AggregateNoSnapshot = result.Code,
            OperationType = operationType, OperationName = GetWarehouseOperationName(operationType),
            Reason = reason, Summary = summary, ChangedFieldsJson = JsonSerializer.Serialize(new { Before = before, After = after }),
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1) throw new InventoryPostingException("业务操作日志写入失败。");
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

    private static string? Normalize(string? text, int max) { var value = text?.Trim(); return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)]; }

}
