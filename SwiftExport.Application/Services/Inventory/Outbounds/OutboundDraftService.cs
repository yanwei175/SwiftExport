using SwiftExport.Application.Interfaces.Systems.Numbering;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>维护人工出库草稿，由公共执行器提交草稿和业务审计。</summary>
public sealed class OutboundDraftService(IOutboundQueryRepository queries, IOutboundDocumentRepository repository, IWarehouseManagementRepository warehouses,
    IBusinessOperationLogRepository logs, ICurrentUser user, IDocumentNoService numbers,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    OutboundServiceSupport support) : IOutboundDraftService
{
    private const string Module = "OutboundManager";
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<CreateOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Create", support.AuthorizeAsync<OutboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            return await ExecuteDraftAsync(command!.CommandId, "Create", null, command.Draft, async () =>
            {
                var header = new OutboundDocument
                {
                    Uid = GuidSequentialGenerator.NewGuid(), DocumentType = command.Draft.DocumentType,
                    LifecycleStatus = InventoryDocumentLifecycleStatusEnum.Active,
                    PostingStatus = OutboundPostingStatusEnum.Unposted, CommandId = command.CommandId
                };
                // 幂等检查通过后才取号；编号器共享当前事务，保存失败时一同回滚。
                var number = await numbers.GetNextDocumentNoAsync(new DocumentNoRequest
                {
                    DocType = DocumentTypeStaticsEnum.OutboundDocument,
                    BusinessDate = command.Draft.ShipmentDate,
                    Operator = user.UserName
                });
                if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo))
                    throw new OutboundDraftException(number.Message ?? "出库单号生成失败，草稿未保存。");
                header.DocumentNo = number.Data.DocumentNo;
                await SaveDraftAsync(header, command.Draft, [], 0, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<UpdateOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Update", support.AuthorizeAsync<OutboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("出库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await ExecuteDraftAsync(command!.CommandId, "Update", uid,
                new { command.ExpectedVersion, command.Draft }, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                if (header.DocumentType != command.Draft.DocumentType) throw new OutboundDraftException("已保存单据不能更换出库类型。");
                var existing = await repository.LockLinesAsync(uid, token);
                await SaveDraftAsync(header, command.Draft, existing, command.ExpectedVersion, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<DeleteOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Delete", support.AuthorizeAsync<OutboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("出库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await ExecuteDraftAsync(command!.CommandId, "Delete", uid,
                new { command.ExpectedVersion, command.Reason }, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                await repository.LockLinesAsync(uid, token);
                await repository.DeleteAsync(header, token);
                return Result(header, true);
            }, token, command.Reason);
        }, ct, false), token);

    /// <summary>同一单头更新锁覆盖所有草稿命令；异常正式流水同样禁止普通编辑。</summary>
    private async Task<OutboundDocument> RequireEditableAsync(Guid uid, int version, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在或已删除。");
        if (header.Version != version) throw new OutboundDraftException("出库单已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (header.DocumentType != OutboundDocumentTypeEnum.StockIssue || header.PostingStatus != OutboundPostingStatusEnum.Unposted
            || header.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || header.TriggerPackingListId.HasValue)
            throw new OutboundDraftException("只允许修改活动、未过账的人工草稿。");
        if (await repository.HasLedgerAsync(uid, token)) throw new OutboundDraftException("该单存在正式库存流水，禁止修改或删除草稿。");
        return header;
    }

    /// <summary>保留已有行身份和快照；新行从主数据取值，余额不作为草稿准入条件。</summary>
    private async Task SaveDraftAsync(OutboundDocument header, OutboundDraftData draft,
        IReadOnlyList<OutboundDocumentLine> existing, int version, CancellationToken token)
    {
        if (existing.Count > 0 && header.WarehouseSiteId != draft.WarehouseSiteId)
            throw new OutboundDraftException("已有明细不能直接更换仓库，请先清空明细并保存。");
        var byId = existing.ToDictionary(x => x.Uid);
        var products = (await queries.GetProductsAsync(draft.Lines.Select(x => x.ProductId).Distinct().ToArray(), token)).ToDictionary(x => x.ProductId);
        var rows = new List<OutboundDocumentLine>();
        foreach (var input in draft.Lines)
        {
            if (!products.TryGetValue(input.ProductId, out var product) || string.IsNullOrWhiteSpace(product.Unit))
                throw new OutboundDraftException("产品不存在、已删除或单位不完整。", ServiceErrorCodes.Validation, "Draft.Lines");
            OutboundDocumentLine row;
            if (input.Uid != Guid.Empty)
            {
                if (!byId.TryGetValue(input.Uid, out row!) || row.ProductId != input.ProductId)
                    throw new OutboundDraftException("明细不属于本单或产品身份被更换，请删除原行后重新选择。");
                if (!string.Equals(row.UnitSnapshot?.Trim(), product.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new OutboundDraftException("产品单位与出库快照不一致，请核对。");
            }
            else
            {
                if (!await queries.IsProductActiveAsync(input.ProductId, token))
                    throw new OutboundDraftException("已停用产品不能新增出库草稿行。");
                row = new OutboundDocumentLine
                {
                    Uid = GuidSequentialGenerator.NewGuid(), Version = 0, OutboundDocumentId = header.Uid,
                    ProductId = product.ProductId, SkuSnapshot = product.Sku,
                    DescriptionSnapshot = product.Description, UnitSnapshot = product.Unit,
                    CommandItemId = GuidSequentialGenerator.NewGuid()
                };
            }
            row.LineNumber = rows.Count + 1;
            row.Quantity = input.Quantity;
            rows.Add(row);
        }
        var site = await warehouses.GetForUpdateAsync(draft.WarehouseSiteId);
        if (site is null || !site.IsActive || site.IsDeleted) throw new OutboundDraftException("出库仓库不存在、已删除或已停用。");
        header.WarehouseSiteId = site.Uid; header.WarehouseCodeSnapshot = site.Code; header.WarehouseNameSnapshot = site.Name;
        header.ShipmentDate = draft.ShipmentDate.Date; header.Remark = draft.Remark;
        header.IssueReason = draft.IssueReason;
        await repository.SaveAsync(header, rows, version, token);
    }

    /// <summary>将出库草稿业务与审计交给公共执行器同事务提交；重放不新增审计。</summary>
    private Task<CommandExecutionResult<OutboundDraftCommandResult>> ExecuteDraftAsync(Guid commandId, string operation, Guid? targetUid, object businessInput,
        Func<Task<OutboundDraftCommandResult>> action, CancellationToken token, string? reason = null)
    {
        return executor.ExecuteAsync(commandId, $"Inventory.Outbound.Draft.{operation}", targetUid, businessInput,
            async _ =>
        {
            var result = await action();
            if (await logs.InsertAsync(new BusinessOperationLog
            {
                CommandId = commandId, CorrelationId = correlation.CorrelationId, Module = Module,
                AggregateType = "OutboundDocument", AggregateUid = result.Uid, AggregateNoSnapshot = result.DocumentNo,
                OperationType = operation, OperationName = operation switch { "Create" => "创建出库草稿", "Update" => "更新出库草稿", _ => "删除出库草稿" },
                Summary = "只变更出库草稿，不改变库存。", Reason = reason,
                ChangedFieldsJson = JsonSerializer.Serialize(new { Operation = operation, BusinessInput = businessInput, result.Version, result.IsDeleted }),
                RelatedObjectsJson = JsonSerializer.Serialize(new { result.Uid, result.DocumentNo }), UserUid = user.UserId,
                UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
            }) != 1) throw new OutboundDraftException("出库操作日志写入失败，草稿未保存。");
            return result;
        }, token);
    }

    private static OutboundDraftCommandResult Result(OutboundDocument header, bool deleted) => new() { Uid = header.Uid, Version = header.Version, DocumentNo = header.DocumentNo, IsDeleted = deleted };
}
