using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Inventory;

public sealed class InventoryService(
    ServiceBaseContext context, IWarehouseSiteRepository warehouseSites, IInventoryRepository inventory,
    IInventoryPostingService posting, IBusinessOperationLogRepository operationLogs, ICorrelationContext correlation, ICurrentUser currentUser) : IInventoryService
{
    private const string Module = "Inventory";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<InventoryService> _logger = context.LoggerFactory.CreateLogger<InventoryService>();
    public const string OpeningBalancePostPermission = "Inventory.OpeningBalance.Post";
    public const string NegativeStockOverridePermission = "Inventory.NegativeStock.Override";
    public const string DocumentReversePermission = "Inventory.Document.Reverse";

    public async Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) => await ReadAsync(() => warehouseSites.SearchAsync(request ?? new WarehouseSiteSearchRequest(), cancellationToken), "查询供应商虚拟仓失败。");
    public async Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default) => uid == Guid.Empty ? ServiceResult<WarehouseSiteDto>.Fail("虚拟仓 Uid 不能为空。") : await ReadAsync(async () => await warehouseSites.GetDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("虚拟仓不存在。"), "加载虚拟仓失败。");
    public async Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default) => await ReadAsync(() => inventory.SearchBalancesAsync(request ?? new InventoryBalanceSearchRequest(), cancellationToken), "查询库存余额失败。");
    public async Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default) => await ReadAsync(() => inventory.SearchLedgerAsync(request ?? new InventoryLedgerSearchRequest(), cancellationToken), "查询库存流水失败。");

    public async Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<InventoryBalanceTraceDto>.Fail("库存余额 Uid 不能为空。");
        return await ReadAsync(async () => new InventoryBalanceTraceDto { Balance = await inventory.GetBalanceDtoAsync(uid, cancellationToken) ?? throw new InventoryPostingException("库存余额不存在。"), Ledgers = (await inventory.GetLedgerByBalanceAsync(uid, cancellationToken)).ToList() }, "加载库存追溯失败。");
    }

    public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CommandId == Guid.Empty || command.SupplierId == Guid.Empty || string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.Name)) return Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("命令、供应商、仓库代码和名称不能为空。"));
        return ExecuteAsync(command.CommandId, "WarehouseSite.Created", async () =>
        {
            if (!await warehouseSites.IsActiveSupplierAsync(command.SupplierId)) throw new InventoryPostingException("供应商不存在或已停用。");
            var site = new WarehouseSite { Uid = GuidSequentialGenerator.NewGuid(), Code = Normalize(command.Code, 50)!, Name = Normalize(command.Name, 200)!, SiteType = 0, SupplierId = command.SupplierId, IsActive = true };
            if (await warehouseSites.InsertAsync(site) != 1) throw new InventoryPostingException("虚拟仓创建失败。");
            await WriteLogAsync(command.CommandId, "WarehouseSite", site.Uid, site.Code, "WarehouseSite.Created", "供应商虚拟仓已创建", null, $"创建虚拟仓 {site.Code}。", null);
            return await warehouseSites.GetDtoAsync(site.Uid) ?? throw new InventoryPostingException("虚拟仓创建后无法读取。");
        }, cancellationToken);
    }

    public Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.Name)) return Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("虚拟仓、命令、版本和名称不能为空。"));
        return ExecuteAsync(command.CommandId, "WarehouseSite.Updated", async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。"); if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            site.Name = Normalize(command.Name, 200)!; if (await warehouseSites.UpdateNameAsync(site, command.ExpectedVersion) != 1) throw new InventoryPostingException("虚拟仓保存发生并发冲突。");
            await WriteLogAsync(command.CommandId, "WarehouseSite", uid, site.Code, "WarehouseSite.Updated", "供应商虚拟仓已改名", null, $"虚拟仓改名为 {site.Name}。", null);
            return await warehouseSites.GetDtoAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
        }, cancellationToken);
    }

    public Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.Reason)) return Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("虚拟仓、命令、版本和停用原因不能为空。"));
        return ExecuteAsync(command.CommandId, "WarehouseSite.Deactivated", async () =>
        {
            var site = await warehouseSites.GetForUpdateAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。"); if (site.Version != command.ExpectedVersion) throw new InventoryPostingException("虚拟仓已被修改，请刷新后重试。");
            if (await warehouseSites.HasBalanceOrLedgerAsync(uid)) throw new InventoryPostingException("虚拟仓存在库存余额或历史流水，不能停用。");
            if (await warehouseSites.DeactivateAsync(site, command.ExpectedVersion) != 1) throw new InventoryPostingException("虚拟仓停用发生并发冲突。");
            await WriteLogAsync(command.CommandId, "WarehouseSite", uid, site.Code, "WarehouseSite.Deactivated", "供应商虚拟仓已停用", Normalize(command.Reason, 1000), "虚拟仓已停用。", null);
            return await warehouseSites.GetDtoAsync(uid) ?? throw new InventoryPostingException("虚拟仓不存在。");
        }, cancellationToken);
    }

    public Task<ServiceResult<List<InventoryPostingResultDto>>> PreviewOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateOpening(command); if (validation is not null) return Task.FromResult(ServiceResult<List<InventoryPostingResultDto>>.Fail(validation));
        return Task.FromResult(ServiceResult<List<InventoryPostingResultDto>>.Ok(command.Lines.Select(x => new InventoryPostingResultDto { CommandItemId = x.CommandItemId, Delta = x.QuantityDelta, AfterQty = x.QuantityDelta }).ToList()));
    }

    public Task<ServiceResult<List<InventoryPostingResultDto>>> PostOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateOpening(command); if (validation is not null) return Task.FromResult(ServiceResult<List<InventoryPostingResultDto>>.Fail(validation));
        if (!currentUser.IsAuthenticated) return Task.FromResult(ServiceResult<List<InventoryPostingResultDto>>.Fail($"需要权限 {OpeningBalancePostPermission}。"));
        return ExecuteAsync(command.CommandId, "Inventory.OpeningBalance.Posted", async () =>
        {
            var items = command.Lines.Select(x => new InventoryPostingItem { CommandItemId = x.CommandItemId, WarehouseSiteId = x.WarehouseSiteId, ProductId = x.ProductId, QuantityDelta = x.QuantityDelta, TransactionType = InventoryTransactionTypeEnum.OpeningBalance, SourceDocumentType = "OpeningBalance", SourceDocumentId = command.CommandId, Reason = Normalize(x.Reason, 500), OccurredDate = DateTime.Now }).ToList();
            var result = (await posting.PostAsync(command.CommandId, items, cancellationToken)).ToList();
            await WriteLogAsync(command.CommandId, "InventoryOpeningBalance", command.CommandId, null, "Inventory.OpeningBalance.Posted", "库存期初余额已过账", null, $"已过账 {result.Count} 条库存期初余额。", null);
            return result;
        }, cancellationToken);
    }

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string failure)
    {
        try { return ServiceResult<T>.Ok(await action()); } catch (OperationCanceledException) { throw; } catch (InventoryPostingException ex) { return ServiceResult<T>.Fail(ex.Message); } catch (Exception ex) { _logger.LogError(ex, "{Failure}", failure); return ServiceResult<T>.Fail(failure); }
    }
    private async Task<ServiceResult<T>> ExecuteAsync<T>(Guid commandId, string operationType, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null) return ServiceResult<T>.Fail("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try { var result = await action(); cancellationToken.ThrowIfCancellationRequested(); _session.Transaction.Commit(); return ServiceResult<T>.Ok(result); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _session.Transaction.Rollback(); throw; }
        catch (InventoryPostingException ex) { _session.Transaction.Rollback(); return ServiceResult<T>.Fail(ex.Message); }
        catch (Exception ex) { _session.Transaction.Rollback(); _logger.LogError(ex, "库存命令 {OperationType}/{CommandId} 执行失败。", operationType, commandId); return ServiceResult<T>.Fail("库存命令执行失败。"); }
        finally { _session.Transaction.Dispose(); _session.Transaction = null; }
    }
    private async Task WriteLogAsync(Guid commandId, string aggregateType, Guid aggregateId, string? aggregateNo, string operationType, string operationName, string? reason, string summary, string? changed)
    {
        if (await operationLogs.GetByCommandAsync(Module, operationType, commandId) is not null) return;
        var log = new BusinessOperationLog { Uid = GuidSequentialGenerator.NewGuid(), CorrelationId = Normalize(correlation.CorrelationId, 64) ?? commandId.ToString("N"), CommandId = commandId, Module = Module, AggregateType = aggregateType, AggregateUid = aggregateId, AggregateNoSnapshot = aggregateNo, OperationType = operationType, OperationName = operationName, Reason = reason, Summary = summary, ChangedFieldsJson = changed, UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now };
        if (await operationLogs.InsertAsync(log) != 1) throw new InventoryPostingException("业务操作日志写入失败。");
    }
    private static string? ValidateOpening(OpeningBalanceCommand command) => command is null || command.CommandId == Guid.Empty || command.Lines is null || command.Lines.Count == 0 ? "期初余额命令和明细不能为空。" : command.Lines.Any(x => x.CommandItemId == Guid.Empty || x.WarehouseSiteId == Guid.Empty || x.ProductId == Guid.Empty || x.QuantityDelta == 0 || string.IsNullOrWhiteSpace(x.Reason)) ? "期初余额明细必须包含仓库、产品、非零数量和原因。" : command.Lines.Select(x => x.CommandItemId).Distinct().Count() != command.Lines.Count ? "期初余额 CommandItemId 不能重复。" : null;
    private static string? Normalize(string? text, int max) { var value = text?.Trim(); return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)]; }
}
