using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
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
using SwiftExport.Application.Services.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>以公共命令入口原子处理人工入库草稿、审计和成功回执。</summary>
public sealed class InboundDraftService(
    IInboundDocumentRepository repository, IInboundExecutionReadRepository executionReads, IInboundSourceRepository sourceRepository, IWarehouseManagementRepository warehouses,
    IBusinessOperationLogRepository logs, ICurrentUser user, IDocumentNoService numbers,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    InboundServiceSupport support) : IInboundDraftService
{
    private const string Module = "InboundManager";

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<CreateInboundDraftCommand, InboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Inbound.Draft.Create", support.AuthorizeAsync<InboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            return await ExecuteDraftAsync(command!.CommandId, "Create", null, command.Draft, async () =>
            {
                var header = new InboundDocument
                {
                    Uid = GuidSequentialGenerator.NewGuid(), DocumentType = command.Draft.DocumentType,
                    CreationMode = InboundCreationModeEnum.Manual, LifecycleStatus = InventoryDocumentLifecycleStatusEnum.Active,
                    PostingStatus = InboundPostingStatusEnum.Unposted, CommandId = command.CommandId
                };
                // 幂等检查通过后才取号；编号器共享当前事务，保存失败时一同回滚。
                var number = await numbers.GetNextDocumentNoAsync(new DocumentNoRequest
                {
                    DocType = DocumentTypeStaticsEnum.InboundDocument,
                    BusinessDate = command.Draft.ReceiptDate,
                    Operator = user.UserName
                });
                if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo))
                    throw new DraftException(number.Message ?? "入库单号生成失败，草稿未保存。");
                header.DocumentNo = number.Data.DocumentNo;
                await SaveDraftAsync(header, command.Draft, [], 0, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<UpdateInboundDraftCommand, InboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Inbound.Draft.Update", support.AuthorizeAsync<InboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("入库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await ExecuteDraftAsync(command!.CommandId, "Update", uid,
                new { command.ExpectedVersion, command.Draft }, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                if (header.DocumentType != command.Draft.DocumentType) throw new DraftException("已保存单据不能更换入库类型。");
                var existing = await repository.LockLinesAsync(uid, token);
                await SaveDraftAsync(header, command.Draft, existing, command.ExpectedVersion, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<DeleteInboundDraftCommand, InboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Inbound.Draft.Delete", support.AuthorizeAsync<InboundDraftCommandResult>, ct => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("入库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
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
    private async Task<InboundDocument> RequireEditableAsync(Guid uid, int version, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
        if (header.Version != version) throw new DraftException("入库单已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (header.CreationMode != InboundCreationModeEnum.Manual || header.PostingStatus != InboundPostingStatusEnum.Unposted
            || header.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || header.TriggerPackingListId.HasValue)
            throw new DraftException("只允许修改活动、未过账的人工草稿。");
        if (await executionReads.HasLedgerAsync(uid, token)) throw new DraftException("该单存在正式库存流水，禁止修改或删除草稿。");
        return header;
    }

    /// <summary>保留已有行历史快照；新增行从真实采购行取值，仓库由单头独立确定。</summary>
    private async Task SaveDraftAsync(InboundDocument header, InboundDraftData draft,
        IReadOnlyList<InboundDocumentLine> existing, int version, CancellationToken token)
    {
        var byId = existing.ToDictionary(x => x.Uid);
        foreach (var input in draft.Lines.Where(x => x.Uid != Guid.Empty))
            if (!byId.TryGetValue(input.Uid, out var old) || old.SourceLineId != input.SourceLineId)
                throw new DraftException("入库行不属于当前单据，或来源被更换；请删除原行后重新选源。");
        var sourceIds = draft.Lines.Where(x => x.Uid == Guid.Empty).Select(x => x.SourceLineId).Distinct().ToArray();
        var sources = sourceIds.Length == 0 ? new Dictionary<Guid, InboundSourceCandidateDto>()
            : (await sourceRepository.GetSourcesAsync(sourceIds, token)).ToDictionary(x => x.SourceLineId);
        var rows = new List<InboundDocumentLine>();
        foreach (var input in draft.Lines)
        {
            InboundDocumentLine row;
            if (input.Uid != Guid.Empty) row = byId[input.Uid];
            else
            {
                if (!sources.TryGetValue(input.SourceLineId, out var source) || source.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(source.Unit))
                    throw new DraftException("采购来源不存在或产品、单位不完整，请重新选择来源。");
                row = new InboundDocumentLine
                {
                    Uid = GuidSequentialGenerator.NewGuid(), Version = 0, InboundDocumentId = header.Uid,
                    SourceLineId = input.SourceLineId, ProductId = source.ProductId, SkuSnapshot = source.Sku,
                    DescriptionSnapshot = source.Description, UnitSnapshot = source.Unit
                };
            }
            row.LineNumber = rows.Count + 1;
            row.Quantity = input.Quantity;
            rows.Add(row);
        }
        var site = await warehouses.GetForUpdateAsync(draft.WarehouseSiteId);
        if (site is null || !site.IsActive || site.IsDeleted) throw new DraftException("收货仓库不存在、已删除或已停用。");
        header.WarehouseSiteId = site.Uid; header.WarehouseCodeSnapshot = site.Code; header.WarehouseNameSnapshot = site.Name;
        header.ReceiptDate = draft.ReceiptDate.Date; header.Remark = draft.Remark;
        await repository.SaveAsync(header, rows, version, token);
    }

    /// <summary>将入库草稿业务与审计交给公共执行器同事务提交；重放不新增审计。</summary>
    private Task<CommandExecutionResult<InboundDraftCommandResult>> ExecuteDraftAsync(Guid commandId, string operation, Guid? targetUid, object businessInput,
        Func<Task<InboundDraftCommandResult>> action, CancellationToken token, string? reason = null)
    {
        return executor.ExecuteAsync(commandId, $"Inventory.Inbound.Draft.{operation}", targetUid, businessInput,
            async _ =>
        {
            var result = await action();
            if (await logs.InsertAsync(new BusinessOperationLog
            {
                CommandId = commandId, CorrelationId = correlation.CorrelationId, Module = Module,
                AggregateType = "InboundDocument", AggregateUid = result.Uid, AggregateNoSnapshot = result.DocumentNo,
                OperationType = operation, OperationName = operation switch { "Create" => "创建入库草稿", "Update" => "更新入库草稿", _ => "删除入库草稿" },
                Summary = "只变更入库草稿，不改变库存。", Reason = reason,
                ChangedFieldsJson = JsonSerializer.Serialize(new { Operation = operation, BusinessInput = businessInput, result.Version, result.IsDeleted }),
                RelatedObjectsJson = JsonSerializer.Serialize(new { result.Uid, result.DocumentNo }), UserUid = user.UserId,
                UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
            }) != 1) throw new DraftException("入库操作日志写入失败，草稿未保存。");
            return result;
        }, token);
    }

    private static InboundDraftCommandResult Result(InboundDocument header, bool deleted) => new() { Uid = header.Uid, Version = header.Version, DocumentNo = header.DocumentNo, IsDeleted = deleted };
}
