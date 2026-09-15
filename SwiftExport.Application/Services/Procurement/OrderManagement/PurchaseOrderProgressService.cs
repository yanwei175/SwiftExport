using System.Text.Json;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>采购供应商跟进用例；保留追加历史、纠错、版本、幂等和权限门禁，不依赖需求 Link。</summary>
public sealed class PurchaseOrderProgressService(
    ProcurementOrderTransaction transaction,
    IPurchaseOrderProgressQueryRepository queries,
    IProcurementOrderRepository orders,
    ISupplierProgressReportRepository reports,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser) : IPurchaseOrderProgressService
{
    private const string Module = "Procurement.Progress";
    private const string Operation = "SupplierProgress.Reported";

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default)
        => transaction.ExecuteAsync("Read", async () =>
        {
            ValidateSearchRequest(request);
            return await queries.SearchAsync(request, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => transaction.ExecuteAsync("Read", async () =>
        {
            if (purchaseOrderId == Guid.Empty)
                throw ProcurementOperationException.Validation("采购单标识不能为空。", "PurchaseOrderId");
            return await queries.GetDetailAsync(purchaseOrderId, cancellationToken)
                ?? throw ProcurementOperationException.Blocked("采购单不存在。");
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
        Guid purchaseOrderId,
        ReportPurchaseOrderProgressCommand command,
        CancellationToken cancellationToken = default)
        => transaction.ExecuteAsync("Edit", async () =>
        {
            ValidateReportCommand(purchaseOrderId, command);
            var head = await orders.GetHeadAsync(purchaseOrderId, cancellationToken)
                ?? throw ProcurementOperationException.Blocked("采购单不存在。");
            var lines = await orders.GetLinesAsync(purchaseOrderId, cancellationToken);
            var line = lines.SingleOrDefault(item => item.Uid == command.PurchaseOrderLineId)
                ?? throw ProcurementOperationException.Blocked("采购行不存在。");

            var prior = await reports.GetByCommandAsync(command.CommandId);
            if (prior is not null)
            {
                if (prior.PurchaseOrderLineId != line.Uid)
                    throw new ProcurementOperationException(ServiceErrorCodes.CommandConflict,
                        "命令标识已用于其他采购行。", "CommandId");
                return CreateResult(head, prior.Uid, ProcurementCommandOutcomeEnum.AlreadyApplied,
                    "供应商反馈已经登记。");
            }

            if (head.Version != command.ExpectedPurchaseOrderVersion)
                throw new ProcurementOperationException(ServiceErrorCodes.VersionConflict,
                    "采购单已被其他用户修改，请刷新后重试。", "ExpectedPurchaseOrderVersion");
            if (line.Version != command.ExpectedLineVersion)
                throw new ProcurementOperationException(ServiceErrorCodes.VersionConflict,
                    "采购行已被其他用户修改，请刷新后重试。", "ExpectedLineVersion");
            if (head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active
                || head.ConfirmationStatus != ConfirmationStatusEnum.已确认)
                throw ProcurementOperationException.Blocked("仅活动且已确认的采购单允许登记供应商反馈。");
            if (command.ReadyQty > line.Quantity)
                throw ProcurementOperationException.Validation(
                    "累计已备好数量不能超过采购行当前有效数量。", "ReadyQty");

            var latest = await reports.GetLatestForUpdateAsync(line.Uid);
            var report = CreateReport(line.Uid, command, latest);
            if (await reports.InsertAsync(report) != 1)
                throw new InvalidOperationException("供应商反馈保存失败。");
            if (await operationLogs.InsertAsync(CreateOperationLog(head, line, report, command)) != 1)
                throw new InvalidOperationException("供应商反馈业务日志写入失败。");

            return CreateResult(head, report.Uid, ProcurementCommandOutcomeEnum.Succeeded,
                "供应商反馈已登记。");
        }, cancellationToken);

    /// <summary>根据锁定的最新报告形成追加快照，保留未填写字段并验证累计数量纠错。</summary>
    internal static SupplierProgressReport CreateReport(
        Guid lineId, ReportPurchaseOrderProgressCommand command, SupplierProgressReport? latest)
    {
            var isCorrection = latest is not null && command.ReadyQty < latest.ReadyQty;
            if (isCorrection && string.IsNullOrWhiteSpace(command.CorrectionReason))
                throw ProcurementOperationException.Validation(
                    "累计已备好数量下降时必须填写纠错原因。", "CorrectionReason");

            return new SupplierProgressReport
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                PurchaseOrderLineId = lineId,
                Sequence = (latest?.Sequence ?? 0) + 1,
                // 未填字段在锁定最新报告后继承，首次未反馈数量保持未知。
                ArrangementStatus = latest?.ArrangementStatus ?? SupplierArrangementStatusEnum.Unknown,
                ReadyQty = command.ReadyQty ?? latest?.ReadyQty,
                ExpectedReadyDate = command.ExpectedReadyDate ?? latest?.ExpectedReadyDate,
                Note = Normalize(command.Note, 2000),
                IsCorrection = isCorrection,
                CorrectionReason = isCorrection ? Normalize(command.CorrectionReason, 2000) : null,
                CommandId = command.CommandId
            };
    }

    /// <summary>验证分页、文本、状态集合及日期范围，避免无界或无效筛选进入 SQL。</summary>
    private static void ValidateSearchRequest(PurchaseOrderProgressSearchRequest request)
    {
        if (request is null
            || request.PageIndex < 1
            || request.PageSize is < 1 or > 200
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
            || request.ConfirmationStatuses is null
            || request.LifecycleStatuses is null
            || request.ArrangementStatuses is null
            || request.ConfirmationStatuses.Count > 20
            || request.LifecycleStatuses.Count > 20
            || request.ArrangementStatuses.Count > 20
            || request.ConfirmationStatuses.Any(status => !Enum.IsDefined(status))
            || request.LifecycleStatuses.Any(status => !Enum.IsDefined(status))
            || request.ArrangementStatuses.Any(status => !Enum.IsDefined(status))
            || HasOversizedText(request.Keyword)
            || HasOversizedText(request.OrderNo)
            || HasOversizedText(request.SupplierKeyword)
            || HasOversizedText(request.Sku)
            || request.PlannedDeliveryDateFrom > request.PlannedDeliveryDateTo
            || request.ExpectedReadyDateFrom > request.ExpectedReadyDateTo)
            throw ProcurementOperationException.Validation("采购跟进筛选参数无效。");
    }

    /// <summary>验证追加反馈的身份、版本、有效输入和文本边界；业务状态在锁定事实后复验。</summary>
    internal static void ValidateReportCommand(Guid purchaseOrderId, ReportPurchaseOrderProgressCommand command)
    {
        if (purchaseOrderId == Guid.Empty)
            throw ProcurementOperationException.Validation("采购单标识不能为空。", "PurchaseOrderId");
        if (command is null)
            throw ProcurementOperationException.Validation("供应商反馈命令不能为空。");
        if (command.CommandId == Guid.Empty)
            throw ProcurementOperationException.Validation("命令标识不能为空。", "CommandId");
        if (command.PurchaseOrderLineId == Guid.Empty)
            throw ProcurementOperationException.Validation("采购行标识不能为空。", "PurchaseOrderLineId");
        if (command.ExpectedPurchaseOrderVersion <= 0)
            throw ProcurementOperationException.Validation("采购单版本无效。", "ExpectedPurchaseOrderVersion");
        if (command.ExpectedLineVersion <= 0)
            throw ProcurementOperationException.Validation("采购行版本无效。", "ExpectedLineVersion");
        if (!command.ReadyQty.HasValue && !command.ExpectedReadyDate.HasValue && string.IsNullOrWhiteSpace(command.Note))
            throw ProcurementOperationException.Validation("请至少填写累计已备好数量、预计交期或跟进备注中的一项。");
        if (command.ReadyQty < 0)
            throw ProcurementOperationException.Validation("累计已备好数量不能小于零。", "ReadyQty");
        if (command.Note?.Length > 2000)
            throw ProcurementOperationException.Validation("跟进备注不能超过 2000 个字符。", "Note");
        if (command.CorrectionReason?.Length > 2000)
            throw ProcurementOperationException.Validation("纠错原因不能超过 2000 个字符。", "CorrectionReason");
    }

    /// <summary>形成跟进专属业务日志；不写采购状态或库存事实。</summary>
    private BusinessOperationLog CreateOperationLog(
        PurchaseOrderHead head,
        PurchaseOrderLine line,
        SupplierProgressReport report,
        ReportPurchaseOrderProgressCommand command)
        => new()
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlationContext.CorrelationId, 64) ?? report.CommandId.ToString("N"),
            CommandId = report.CommandId,
            Module = Module,
            AggregateType = "PurchaseOrder",
            AggregateUid = head.Uid,
            AggregateNoSnapshot = head.OrderNo,
            OperationType = Operation,
            OperationName = "供应商进度已登记",
            Reason = report.CorrectionReason,
            Summary = $"采购行 {line.LineNumber} 已追加跟进反馈，累计已备好：{report.ReadyQty?.ToString() ?? "尚未反馈"}。",
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                SubmittedReadyQty = command.ReadyQty,
                SubmittedExpectedReadyDate = command.ExpectedReadyDate,
                SubmittedNote = Normalize(command.Note, 2000),
                SnapshotReadyQty = report.ReadyQty,
                SnapshotExpectedReadyDate = report.ExpectedReadyDate,
                report.IsCorrection
            }),
            RelatedObjectsJson = JsonSerializer.Serialize(new
            {
                PurchaseOrderLineId = line.Uid,
                line.LineNumber,
                line.Sku
            }),
            UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        };

    private static PurchaseOrderProgressReportResult CreateResult(
        PurchaseOrderHead head,
        Guid reportId,
        ProcurementCommandOutcomeEnum outcome,
        string message)
        => new()
        {
            Outcome = outcome,
            PurchaseOrderId = head.Uid,
            Version = head.Version,
            SupplierProgressReportId = reportId,
            Message = message
        };

    private static bool HasOversizedText(string? value) => value?.Length > 100;

    private static string? Normalize(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
