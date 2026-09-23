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
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>人工入库草稿与正向过账编排；草稿不预占容量，正式过账通过库存内核同事务提交。</summary>
public sealed partial class InboundManagerService(
    ServiceBaseContext context, IInboundManagerRepository repository, IWarehouseSiteRepository warehouses,
    IBusinessOperationLogRepository logs, ICurrentUser user, IUserPermissionService permissions,
    IDocumentNoService numbers, IInventoryPostingService inventoryPosting) : IInboundManagerService
{
    private const string Module = "InboundManager";
    private readonly ILogger<InboundManagerService> _logger = context.LoggerFactory.CreateLogger<InboundManagerService>();

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidatePage(request);
            if (request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new DraftException("入库类型无效。");
            if (request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                throw new DraftException("过账状态无效。", ServiceErrorCodes.Validation, nameof(request.PostingStatus));
            return await repository.SearchAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("入库单标识不能为空。");
            return await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidatePage(request);
            if (request.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
                throw new DraftException("该入库类型的选源尚未实现。");
            if (!request.PurchaseCategory.HasValue || !Enum.IsDefined(request.PurchaseCategory.Value))
                throw new DraftException("请选择正常采购或客供件采购。");
            if (request.SupplierId == Guid.Empty) throw new DraftException("供应商标识无效。");
            return await repository.SearchSourcesAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            if (command is null || command.CommandId == Guid.Empty) throw new DraftException("CommandId 不能为空。", ServiceErrorCodes.Validation, "CommandId");
            ValidateDraft(command.Draft);
            if (command.Draft.Lines.Any(x => x.Uid != Guid.Empty)) throw new DraftException("新增草稿行不能携带已有入库行标识。", ServiceErrorCodes.Validation, "Draft.Lines");
            return await ExecuteAsync(command.CommandId, "Create", Fingerprint(command), async () =>
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
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            ValidateDraft(command!.Draft);
            return await ExecuteAsync(command.CommandId, "Update", Fingerprint(uid, command), async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                if (header.DocumentType != command.Draft.DocumentType) throw new DraftException("已保存单据不能更换入库类型。");
                var existing = await repository.LockLinesAsync(uid, token);
                await SaveDraftAsync(header, command.Draft, existing, command.ExpectedVersion, token);
                return Result(header, false);
            }, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            if (string.IsNullOrWhiteSpace(command!.Reason) || command.Reason.Length > 500)
                throw new DraftException("请填写不超过 500 字的删除原因。", ServiceErrorCodes.Validation, "Reason");
            return await ExecuteAsync(command.CommandId, "Delete", Fingerprint(uid, command), async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                await repository.LockLinesAsync(uid, token);
                await repository.DeleteAsync(header, token);
                return Result(header, true);
            }, token, command.Reason);
        }, token);

    /// <summary>同一单头更新锁覆盖所有草稿命令；异常正式流水同样禁止普通编辑。</summary>
    private async Task<InboundDocument> RequireEditableAsync(Guid uid, int version, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
        if (header.Version != version) throw new DraftException("入库单已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (header.CreationMode != InboundCreationModeEnum.Manual || header.PostingStatus != InboundPostingStatusEnum.Unposted
            || header.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || header.TriggerPackingListId.HasValue)
            throw new DraftException("只允许修改活动、未过账的人工草稿。");
        if (await repository.HasLedgerAsync(uid, token)) throw new DraftException("该单存在正式库存流水，禁止修改或删除草稿。");
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
            : (await repository.GetSourcesAsync(sourceIds, token)).ToDictionary(x => x.SourceLineId);
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

    /// <summary>命令、头行和日志在同一事务提交；同键不同内容拒绝，同键同内容返回首次结果。</summary>
    private async Task<InboundDraftCommandResult> ExecuteAsync(Guid commandId, string operation, string fingerprint,
        Func<Task<InboundDraftCommandResult>> action, CancellationToken token, string? reason = null)
    {
        if (context.Session.Transaction is not null) throw new DraftException("当前请求已有事务，不能独立保存入库草稿。");
        using var transaction = context.Session.Connection.BeginTransaction();
        context.Session.Transaction = transaction;
        try
        {
            // 日志字段的 ISJSON 约束要求对象或数组；写入和重放比对共用同一对象格式。
            var fingerprintJson = JsonSerializer.Serialize(new { Fingerprint = fingerprint });
            if (await repository.LockCommandAsync(commandId, token) < 0) throw new DraftException("入库命令正在处理中，请以同一 CommandId 重试。", InboundErrorCodes.CommandBusy);
            var prior = await logs.GetByModuleCommandAsync(Module, commandId);
            if (prior is not null)
            {
                if (prior.OperationType != operation || prior.ChangedFieldsJson != fingerprintJson || prior.UserUid != user.UserId)
                    throw new DraftException("IDEMPOTENCY_CONFLICT：CommandId 已用于不同内容或操作。", ServiceErrorCodes.CommandConflict, "CommandId");
                var replay = JsonSerializer.Deserialize<InboundDraftCommandResult>(prior.RelatedObjectsJson ?? "")
                    ?? throw new DraftException("历史命令结果不完整，不能安全重放。", InboundErrorCodes.OutcomeUnknown);
                token.ThrowIfCancellationRequested();
                transaction.Commit();
                return replay;
            }
            var result = await action();
            if (await logs.InsertAsync(new BusinessOperationLog
            {
                CommandId = commandId, CorrelationId = commandId.ToString("N"), Module = Module,
                AggregateType = "InboundDocument", AggregateUid = result.Uid, AggregateNoSnapshot = result.DocumentNo,
                OperationType = operation, OperationName = operation switch { "Create" => "创建入库草稿", "Update" => "更新入库草稿", _ => "删除入库草稿" },
                Summary = "只变更入库草稿，不改变库存。", Reason = reason, ChangedFieldsJson = fingerprintJson,
                RelatedObjectsJson = JsonSerializer.Serialize(result), UserUid = user.UserId,
                UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
            }) != 1) throw new DraftException("入库操作日志写入失败，草稿未保存。");
            token.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }
        catch { transaction.Rollback(); throw; }
        finally { context.Session.Transaction = null; }
    }

    /// <summary>统一读取与写入访问权限及错误包装；数据库技术信息仅写服务端日志。</summary>
    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!user.IsAuthenticated || user.UserId is not { } id)
                return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
            if (!await permissions.HasPermissionAsync(id, PermissionKeys.ModuleInventoryAccess, token))
                return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号无库存模块访问权限。");
            return ServiceResult<T>.Ok(await action());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DraftException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex)
        {
            _logger.LogWarning(ex, "入库草稿版本冲突，事务已回滚。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.VersionConflict, "入库单或明细已变化，请刷新后重试。", "ExpectedVersion");
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            _logger.LogError(ex, "入库数据库结构尚未对齐。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "入库数据库结构与当前实体不一致，请先完成数据库结构核对；本次操作未完成。");
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            _logger.LogWarning(ex, "存在来源或反向关系未核实的正式入库事实。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "存在未完成来源或反向完整性核对的历史入库，无法可靠计算净收货量，请先核实历史事实。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "入库操作结果未能确认。");
            return ServiceResult<T>.Fail(InboundErrorCodes.OutcomeUnknown, "入库操作结果未能确认；写操作请使用原 CommandId 重试，不要重复创建单据。");
        }
    }

    /// <summary>验证分页上界，避免无界查询和偏移量溢出。</summary>
    private static void ValidatePage(PagedRequestBase? request)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            throw new DraftException("分页参数无效，页大小为 1–200，关键词最多 200 字。");
    }

    /// <summary>校验人工草稿结构，不预占来源剩余量。</summary>
    private static void ValidateDraft(InboundDraftData? draft)
    {
        if (draft is null || draft.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
            throw new DraftException("当前只支持采购入库人工草稿。", ServiceErrorCodes.Validation, "Draft.DocumentType");
        if (draft.WarehouseSiteId == Guid.Empty || draft.ReceiptDate == default || draft.Remark?.Length > 500)
            throw new DraftException("请选择收货仓库、收货日期，备注最多 500 字。", ServiceErrorCodes.Validation, "Draft");
        if (draft.Lines is null || draft.Lines.Count is < 1 or > 500)
            throw new DraftException("每张草稿须包含 1–500 行。", ServiceErrorCodes.Validation, "Draft.Lines");
        if (draft.Lines.Any(x => x is null || x.SourceLineId == Guid.Empty || x.Quantity <= 0
            || x.Quantity > 99999999999999.9999m || decimal.Round(x.Quantity, 4) != x.Quantity))
            throw new DraftException("来源不能为空，实收数量须为正数、最多四位小数且符合 decimal(18,4)。", ServiceErrorCodes.Validation, "Draft.Lines");
        if (draft.Lines.Select(x => x.SourceLineId).Distinct().Count() != draft.Lines.Count
            || draft.Lines.Where(x => x.Uid != Guid.Empty).Select(x => x.Uid).Distinct().Count() != draft.Lines.Count(x => x.Uid != Guid.Empty))
            throw new DraftException("同一张入库单不能重复来源或明细标识。", ServiceErrorCodes.Validation, "Draft.Lines");
    }

    private static void ValidateIdentity(Guid uid, Guid command, int version)
    {
        if (uid == Guid.Empty || command == Guid.Empty || version <= 0) throw new DraftException("单据标识、CommandId 和 ExpectedVersion 无效。", ServiceErrorCodes.Validation);
    }
    private static string Fingerprint(params object[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static InboundDraftCommandResult Result(InboundDocument header, bool deleted) => new() { Uid = header.Uid, Version = header.Version, DocumentNo = header.DocumentNo, IsDeleted = deleted };
    /// <summary>可安全显示给用户的入库草稿业务错误。</summary>
    private sealed class DraftException(string message, string code = ServiceErrorCodes.BusinessBlocked, string? field = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Field { get; } = field;
    }
}
