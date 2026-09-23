using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using SwiftExport.Contracts.DTOs.Errors;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Commands;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>人工出库草稿与正向过账编排；草稿不预占容量，正式过账通过库存内核同事务提交。</summary>
public sealed partial class OutboundManagerService(
    ServiceBaseContext context, IOutboundManagerRepository repository, IWarehouseSiteRepository warehouses,
    IBusinessOperationLogRepository logs, ICurrentUser user, IUserPermissionService permissions,
    IDocumentNoService numbers, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline) : IOutboundManagerService
{
    private const string Module = "OutboundManager";
    private readonly ILogger<OutboundManagerService> _logger = context.LoggerFactory.CreateLogger<OutboundManagerService>();

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidatePage(request);
            if (request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new DraftException("出库类型无效。");
            if (request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                throw new DraftException("过账状态无效。", ServiceErrorCodes.Validation, nameof(request.PostingStatus));
            return await repository.SearchAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("出库单标识不能为空。");
            return await repository.GetDetailAsync(uid, token) ?? throw new DraftException("出库单不存在或已删除。");
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidatePage(request);
            if (request.WarehouseSiteId == Guid.Empty || request.ExcludedProductIds is null || request.ExcludedProductIds.Count > 500)
                throw new DraftException("请选择仓库，排除产品数量不得超过 500。", ServiceErrorCodes.Validation);
            return await repository.SearchItemsAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<CreateOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Create", AuthorizeDraftAsync<OutboundDraftCommandResult>, ct => RunAsync(async () =>
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
                    throw new DraftException(number.Message ?? "出库单号生成失败，草稿未保存。");
                header.DocumentNo = number.Data.DocumentNo;
                await SaveDraftAsync(header, command.Draft, [], 0, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<UpdateOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Update", AuthorizeDraftAsync<OutboundDraftCommandResult>, ct => RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("出库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await ExecuteDraftAsync(command!.CommandId, "Update", uid,
                new { command.ExpectedVersion, command.Draft }, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                if (header.DocumentType != command.Draft.DocumentType) throw new DraftException("已保存单据不能更换出库类型。");
                var existing = await repository.LockLinesAsync(uid, token);
                await SaveDraftAsync(header, command.Draft, existing, command.ExpectedVersion, token);
                return Result(header, false);
            }, token);
        }, ct, false), token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<DeleteOutboundDraftCommand, OutboundDraftCommandResult>(command, command?.CommandId,
            "Inventory.Outbound.Draft.Delete", AuthorizeDraftAsync<OutboundDraftCommandResult>, ct => RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("出库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
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
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("出库单不存在或已删除。");
        if (header.Version != version) throw new DraftException("出库单已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (header.DocumentType != OutboundDocumentTypeEnum.StockIssue || header.PostingStatus != OutboundPostingStatusEnum.Unposted
            || header.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || header.TriggerPackingListId.HasValue)
            throw new DraftException("只允许修改活动、未过账的人工草稿。");
        if (await repository.HasLedgerAsync(uid, token)) throw new DraftException("该单存在正式库存流水，禁止修改或删除草稿。");
        return header;
    }

    /// <summary>保留已有行身份和快照；新行从主数据取值，余额不作为草稿准入条件。</summary>
    private async Task SaveDraftAsync(OutboundDocument header, OutboundDraftData draft,
        IReadOnlyList<OutboundDocumentLine> existing, int version, CancellationToken token)
    {
        if (existing.Count > 0 && header.WarehouseSiteId != draft.WarehouseSiteId)
            throw new DraftException("已有明细不能直接更换仓库，请先清空明细并保存。");
        var byId = existing.ToDictionary(x => x.Uid);
        var products = (await repository.GetProductsAsync(draft.Lines.Select(x => x.ProductId).Distinct().ToArray(), token)).ToDictionary(x => x.ProductId);
        var rows = new List<OutboundDocumentLine>();
        foreach (var input in draft.Lines)
        {
            if (!products.TryGetValue(input.ProductId, out var product) || string.IsNullOrWhiteSpace(product.Unit))
                throw new DraftException("产品不存在、已删除或单位不完整。", ServiceErrorCodes.Validation, "Draft.Lines");
            OutboundDocumentLine row;
            if (input.Uid != Guid.Empty)
            {
                if (!byId.TryGetValue(input.Uid, out row!) || row.ProductId != input.ProductId)
                    throw new DraftException("明细不属于本单或产品身份被更换，请删除原行后重新选择。");
                if (!string.Equals(row.UnitSnapshot?.Trim(), product.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new DraftException("产品单位与出库快照不一致，请核对。");
            }
            else
            {
                if (!await repository.IsProductActiveAsync(input.ProductId, token))
                    throw new DraftException("已停用产品不能新增出库草稿行。");
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
        if (site is null || !site.IsActive || site.IsDeleted) throw new DraftException("出库仓库不存在、已删除或已停用。");
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
            }) != 1) throw new DraftException("出库操作日志写入失败，草稿未保存。");
            return result;
        }, token);
    }

    /// <summary>统一读取与写入访问权限及错误包装；数据库技术信息仅写服务端日志。</summary>
    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken token, bool checkPermission = true)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (checkPermission && await AuthorizeDraftAsync<T>(token) is { } refusal) return refusal;
            return ServiceResult<T>.Ok(await action());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (BusinessCommandException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DraftException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex)
        {
            _logger.LogWarning(ex, "出库草稿版本冲突，事务已回滚。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.VersionConflict, "出库单或明细已变化，请刷新后重试。", "ExpectedVersion");
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            _logger.LogError(ex, "出库数据库结构尚未对齐。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "出库数据库结构与当前实体不一致，请先完成数据库结构核对；本次操作未完成。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "出库操作结果未能确认。");
            return ServiceResult<T>.Fail(OutboundErrorCodes.OutcomeUnknown, "出库操作结果未能确认；写操作请使用原 CommandId 重试，不要重复创建单据。");
        }
    }

    /// <summary>在验证和回执读取之前核对身份与库存模块权限。</summary>
    private async Task<ServiceResult<T>?> AuthorizeDraftAsync<T>(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        if (!await permissions.HasPermissionAsync(id, PermissionKeys.ModuleInventoryAccess, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号无库存模块访问权限。");
        return null;
    }

    /// <summary>验证分页上界，避免无界查询和偏移量溢出。</summary>
    private static void ValidatePage(PagedRequestBase? request)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            throw new DraftException("分页参数无效，页大小为 1–200，关键词最多 200 字。");
    }

    private static void ValidateIdentity(Guid uid, Guid command, int version)
    {
        if (uid == Guid.Empty || command == Guid.Empty || version <= 0) throw new DraftException("单据标识、CommandId 和 ExpectedVersion 无效。", ServiceErrorCodes.Validation);
    }
    private static string Fingerprint(params object[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static OutboundDraftCommandResult Result(OutboundDocument header, bool deleted) => new() { Uid = header.Uid, Version = header.Version, DocumentNo = header.DocumentNo, IsDeleted = deleted };
    /// <summary>可安全显示给用户的出库草稿业务错误。</summary>
    private sealed class DraftException(string message, string code = ServiceErrorCodes.BusinessBlocked, string? field = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Field { get; } = field;
    }
}
