using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;
using System.Text.Json;

namespace SwiftExport.Application.Services.Procurement;

public sealed class PurchaseOrderWorkspaceService(
    ServiceBaseContext context,
    IPurchaseOrderWorkspaceRepository purchaseOrders,
    ISupplierProgressReportRepository progressReports,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser) : IPurchaseOrderWorkspaceService
{
    private const string Module = "Procurement";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<PurchaseOrderWorkspaceService> _logger = context.LoggerFactory.CreateLogger<PurchaseOrderWorkspaceService>();

    public async Task<ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>> SearchAsync(PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default)
    {
        try { return ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>.Ok(await purchaseOrders.SearchAsync(request ?? new PurchaseOrderSearchRequest(), cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "查询采购工作台失败。"); return ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>.Fail("查询采购工作台失败。"); }
    }

    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单 Uid 不能为空。");
        try
        {
            var summary = await purchaseOrders.GetHeadSummaryAsync(uid, cancellationToken);
            var head = await purchaseOrders.GetHeadAsync(uid, cancellationToken);
            if (summary is null || head is null) return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在。");
            var lines = (await purchaseOrders.GetLineDetailsAsync(uid, cancellationToken)).ToList();
            var contributions = await purchaseOrders.GetContributionsAsync(uid, cancellationToken);
            var progress = await progressReports.GetByPurchaseOrderAsync(uid, cancellationToken);
            foreach (var line in lines)
            {
                line.Contributions = contributions.Where(x => x.PurchaseOrderLineId == line.Uid).ToList();
                line.ProgressHistory = progress.Where(x => x.PurchaseOrderLineId == line.Uid).OrderByDescending(x => x.Sequence).ToList();
                line.LatestProgress = line.ProgressHistory.FirstOrDefault();
            }
            return ServiceResult<PurchaseOrderDetailDto>.Ok(new PurchaseOrderDetailDto
            {
                Head = summary,
                SalesOrderNo = head.SalesOrderNo,
                CustomerCode = head.CustomerCode,
                CustomerPoNo = head.CustomerPoNo,
                SalesPerson = head.SoPerson,
                PurchasePerson = head.PoPerson,
                InletHoseType = head.InletrHoseType,
                AeratorType = head.AeratorType,
                PalletType = head.PalletType,
                BoxType = head.BoxType,
                LabelType = head.LabelType,
                InstructionManualType = head.IMType,
                CartonType = head.CartonType,
                LogoType = head.LogoType,
                ReviewedBy = head.ReviewedBy,
                PurchaseOrderFolder = head.PoFolder,
                DiscountRemark = head.ZheKouBeiZhu,
                Remark = head.Remark,
                ConfirmedDate = head.ConfirmedDate,
                ConfirmedBy = head.ConfirmedBy,
                CreatedDate = head.CreatedDate,
                CreatedBy = head.CreatedBy,
                UpdatedDate = head.UpdatedDate,
                UpdatedBy = head.UpdatedBy,
                CurrencyText = string.IsNullOrWhiteSpace(head.Currency) ? "CNY" : head.Currency,
                UsdTotalText = "不适用（当前仅人民币）",
                CartonTotalText = "尚未接入",
                CbmTotalText = "尚未接入",
                InboundStatusText = "尚未接入",
                ShippedStatusText = $"{summary.FinalShippedQty:N4} / {summary.TotalQty:N4}",
                Lines = lines,
                Operations = (await operationLogs.GetByAggregateAsync(Module, uid, cancellationToken)).ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "加载采购单 {Uid} 详情失败。", uid); return ServiceResult<PurchaseOrderDetailDto>.Fail("加载采购单详情失败。"); }
    }

    public async Task<ServiceResult<PurchaseOrderTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<PurchaseOrderTraceDto>.Fail("采购单 Uid 不能为空。");
        try
        {
            var head = await purchaseOrders.GetHeadAsync(uid, cancellationToken);
            if (head is null) return ServiceResult<PurchaseOrderTraceDto>.Fail("采购单不存在。");
            return ServiceResult<PurchaseOrderTraceDto>.Ok(new PurchaseOrderTraceDto
            {
                PurchaseOrderId = uid,
                OrderNo = head.OrderNo,
                Contributions = (await purchaseOrders.GetContributionsAsync(uid, cancellationToken)).ToList(),
                Operations = (await operationLogs.GetByAggregateAsync(Module, uid, cancellationToken)).ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "加载采购单 {Uid} 追溯失败。", uid); return ServiceResult<PurchaseOrderTraceDto>.Fail("加载采购追溯失败。"); }
    }

    public Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressAsync(Guid uid, CancellationToken cancellationToken = default)
        => GetProgressCoreAsync(uid, cancellationToken);

    private async Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressCoreAsync(Guid uid, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty) return ServiceResult<IReadOnlyList<SupplierProgressReportDto>>.Fail("采购单 Uid 不能为空。");
        try { return ServiceResult<IReadOnlyList<SupplierProgressReportDto>>.Ok(await progressReports.GetByPurchaseOrderAsync(uid, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "加载采购单 {Uid} 供应商反馈失败。", uid); return ServiceResult<IReadOnlyList<SupplierProgressReportDto>>.Fail("加载供应商反馈失败。"); }
    }

    public async Task<ServiceResult<ProcurementCommandResult>> UpdateDraftAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0)
            return ServiceResult<ProcurementCommandResult>.Fail("采购单、命令标识和版本不能为空。");
        if (command.Lines is null || command.Lines.Count == 0 || command.Lines.Any(x => x.PurchaseOrderLineId == Guid.Empty || x.ExpectedVersion <= 0 || x.UnitPrice < 0 || x.TaxPrice < 0))
            return ServiceResult<ProcurementCommandResult>.Fail("草稿行或价格无效。");
        return await ExecuteMutationAsync(uid, command.CommandId, "PurchaseOrder.DraftUpdated", async () =>
        {
            var head = await RequireOperableHeadAsync(uid, command.ExpectedVersion, ConfirmationStatusEnum.待确认);
            var lines = await purchaseOrders.GetLinesForUpdateAsync(uid);
            var lockedContributions = await purchaseOrders.GetContributionsForUpdateAsync(uid, cancellationToken);
            if (await purchaseOrders.HasDownstreamFactsAsync(uid)) throw new ProcurementException("采购单已有收货或库存事实，不能编辑草稿。");
            if (lines.Count != command.Lines.Count || lines.Select(x => x.Uid).Except(command.Lines.Select(x => x.PurchaseOrderLineId)).Any())
                throw new ProcurementException("草稿行集合已变化，请刷新后重试。");
            if (lockedContributions.Count == 0 || lines.Any(line => lockedContributions.Where(x => x.PurchaseOrderLineId == line.Uid).Sum(x => x.EffectiveQty) != line.QTY))
                throw new ProcurementException("采购行需求 Link 不完整或数量不守恒，遗留/损坏采购单只允许查看。");
            foreach (var line in lines)
            {
                var submitted = command.Lines.Single(x => x.PurchaseOrderLineId == line.Uid);
                if (line.Version != submitted.ExpectedVersion) throw new ProcurementException("采购行已被其他用户修改，请刷新后重试。");
                line.PlannedDeliveryDate = submitted.PlannedDeliveryDate;
                line.SupplierSku = Normalize(submitted.SupplierSku, 200);
                line.DescriptionZh = Normalize(submitted.DescriptionZh, 1000);
                line.UnitPrice = submitted.UnitPrice;
                line.TaxPrice = submitted.TaxPrice;
                line.SubNote = Normalize(submitted.SubNote, 1000);
            }
            if (command.UpdateLegacyHeaderFields)
            {
                head.BoNo = Normalize(command.BoNo, 100);
                head.CustomerPoNo = Normalize(command.CustomerPoNo, 100);
                head.SoPerson = Normalize(command.SalesPerson, 100);
                head.PoPerson = Normalize(command.PurchasePerson, 100);
                head.ReplyDate = command.ReplyDate;
                head.InletrHoseType = Normalize(command.InletHoseType, 200);
                head.AeratorType = Normalize(command.AeratorType, 200);
                head.PalletType = Normalize(command.PalletType, 200);
                head.BoxType = Normalize(command.BoxType, 200);
                head.LabelType = Normalize(command.LabelType, 200);
                head.IMType = Normalize(command.InstructionManualType, 200);
                head.CartonType = Normalize(command.CartonType, 200);
                head.LogoType = Normalize(command.LogoType, 200);
                head.ZheKouBeiZhu = Normalize(command.DiscountRemark, 1000);
            }
            head.Remark = Normalize(command.Remark, 2000);
            var affected = await purchaseOrders.UpdateDraftAsync(head, lines, command.ExpectedVersion);
            if (affected != lines.Count + 1) throw new ProcurementException("草稿保存发生并发冲突，请刷新后重试。");
            await WriteLogAsync(head, command.CommandId, "PurchaseOrder.DraftUpdated", "采购草稿已更新", null,
                "已更新采购草稿商业与包装字段。", JsonSerializer.Serialize(new { Head = new[] { "BoNo", "CustomerPoNo", "SoPerson", "PoPerson", "ReplyDate", "InletrHoseType", "AeratorType", "PalletType", "BoxType", "LabelType", "IMType", "CartonType", "LogoType", "ZheKouBeiZhu", "Remark" }, Lines = new[] { "PlannedDeliveryDate", "SupplierSku", "DescriptionZh", "UnitPrice", "TaxPrice", "SubNote" } }), null);
            var saved = Result(head, ConfirmationStatusEnum.待确认, "采购草稿已保存。"); saved.Version = head.Version + 1; return saved;
        }, cancellationToken);
    }

    public async Task<ServiceResult<ProcurementCommandResult>> ConfirmAsync(Guid uid, ConfirmPurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0)
            return ServiceResult<ProcurementCommandResult>.Fail("采购单、命令标识和版本不能为空。");
        return await ExecuteMutationAsync(uid, command.CommandId, "PurchaseOrder.Confirmed", async () =>
        {
            var head = await RequireOperableHeadAsync(uid, command.ExpectedVersion, ConfirmationStatusEnum.待确认);
            if (!head.SupplierId.HasValue || !await purchaseOrders.IsActiveSupplierAsync(head.SupplierId.Value)) throw new ProcurementException("采购供应商不存在或已失效。");
            var lines = await purchaseOrders.GetLinesForUpdateAsync(uid);
            if (lines.Count == 0) throw new ProcurementException("采购单至少需要一条有效采购行。");
            var contributions = await purchaseOrders.GetContributionsForUpdateAsync(uid, cancellationToken);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Sku) || line.QTY <= 0) throw new ProcurementException($"采购行 {line.LineNumber} 的 SKU 或数量无效。");
                if (!line.TaxPrice.HasValue || line.TaxPrice.Value < 0) throw new ProcurementException($"采购行 {line.LineNumber} 必须填写含税单价；明确的零价允许确认。");
                var linked = contributions.Where(x => x.PurchaseOrderLineId == line.Uid).Sum(x => x.EffectiveQty);
                if (linked != line.QTY) throw new ProcurementException($"采购行 {line.LineNumber} 的需求 Link 数量不守恒。");
                if (line.DeliveryTargetAllocationId.HasValue != line.DeliveryTargetSupplierId.HasValue) throw new ProcurementException($"采购行 {line.LineNumber} 的客供件送达目标不完整。");
            }
            var now = DateTime.Now;
            if (await purchaseOrders.SetConfirmationAsync(uid, command.ExpectedVersion, true, now, currentUser.UserName, currentUser.UserName) != 1)
                throw new ProcurementException("采购确认发生并发冲突，请刷新后重试。");
            await WriteLogAsync(head, command.CommandId, "PurchaseOrder.Confirmed", "采购单已确认", null, "采购单通过完整性校验并确认。", "{\"ConfirmationStatus\":\"已确认\"}", null);
            var result = Result(head, ConfirmationStatusEnum.已确认, "采购单已确认。"); result.Version = head.Version + 1; return result;
        }, cancellationToken);
    }

    public async Task<ServiceResult<ProcurementCommandResult>> CancelConfirmationAsync(Guid uid, CancelPurchaseOrderConfirmationCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.Reason))
            return ServiceResult<ProcurementCommandResult>.Fail("采购单、命令标识、版本和取消原因不能为空。");
        return await ExecuteMutationAsync(uid, command.CommandId, "PurchaseOrder.ConfirmationCancelled", async () =>
        {
            var head = await RequireOperableHeadAsync(uid, command.ExpectedVersion, ConfirmationStatusEnum.已确认);
            _ = await purchaseOrders.GetLinesForUpdateAsync(uid);
            if (await purchaseOrders.HasDownstreamFactsAsync(uid)) throw new ProcurementException("采购单已有收货或库存事实，不能取消确认。");
            if (await purchaseOrders.SetConfirmationAsync(uid, command.ExpectedVersion, false, null, null, currentUser.UserName) != 1)
                throw new ProcurementException("取消确认发生并发冲突，请刷新后重试。");
            await WriteLogAsync(head, command.CommandId, "PurchaseOrder.ConfirmationCancelled", "采购确认已取消", command.Reason.Trim(), "采购单已恢复待确认。", "{\"ConfirmationStatus\":\"待确认\"}", null);
            var result = Result(head, ConfirmationStatusEnum.待确认, "采购确认已取消。"); result.Version = head.Version + 1; return result;
        }, cancellationToken);
    }

    public async Task<ServiceResult<ProcurementCommandResult>> ReportProgressAsync(Guid uid, SupplierProgressReportCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.PurchaseOrderLineId == Guid.Empty || command.ExpectedPurchaseOrderVersion <= 0 || command.ExpectedLineVersion <= 0)
            return ServiceResult<ProcurementCommandResult>.Fail("采购单、采购行、命令标识和版本不能为空。");
        if (!Enum.IsDefined(command.ArrangementStatus) || command.ReadyQty < 0 || command.SupplierReportedDate == default)
            return ServiceResult<ProcurementCommandResult>.Fail("供应商安排状态、已备好数量或反馈日期无效。");
        return await ExecuteMutationAsync(uid, command.CommandId, "SupplierProgress.Reported", async () =>
        {
            var head = await RequireOperableHeadAsync(uid, command.ExpectedPurchaseOrderVersion, ConfirmationStatusEnum.已确认);
            var lines = await purchaseOrders.GetLinesForUpdateAsync(uid);
            var line = lines.SingleOrDefault(x => x.Uid == command.PurchaseOrderLineId) ?? throw new ProcurementException("采购行不存在。");
            if (line.Version != command.ExpectedLineVersion) throw new ProcurementException("采购行已被其他用户修改，请刷新后重试。");
            if (command.ReadyQty > line.QTY) throw new ProcurementException("累计已备好数量不能超过采购行当前有效数量。");
            var latest = await progressReports.GetLatestForUpdateAsync(line.Uid);
            var correction = latest is not null && command.ReadyQty < latest.ReadyQty;
            if (correction && string.IsNullOrWhiteSpace(command.CorrectionReason)) throw new ProcurementException("累计已备好数量下降时必须填写纠错原因。");
            var report = new SupplierProgressReport
            {
                Uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid(),
                PurchaseOrderLineId = line.Uid,
                Sequence = (latest?.Sequence ?? 0) + 1,
                ArrangementStatus = command.ArrangementStatus,
                ReadyQty = command.ReadyQty,
                ExpectedReadyDate = command.ExpectedReadyDate,
                SupplierReportedDate = command.SupplierReportedDate,
                Note = Normalize(command.Note, 2000),
                IsCorrection = correction,
                CorrectionReason = correction ? Normalize(command.CorrectionReason, 500) : null,
                CommandId = command.CommandId
            };
            if (await progressReports.InsertAsync(report) != 1) throw new ProcurementException("供应商反馈保存失败。");
            await WriteLogAsync(head, command.CommandId, "SupplierProgress.Reported", "供应商进度已登记", report.CorrectionReason,
                $"采购行 {line.LineNumber} 累计已备好 {report.ReadyQty}。",
                JsonSerializer.Serialize(new { report.ArrangementStatus, report.ReadyQty, report.ExpectedReadyDate, report.SupplierReportedDate, report.IsCorrection }),
                JsonSerializer.Serialize(new { PurchaseOrderLineId = line.Uid, line.LineNumber, line.Sku }));
            var result = Result(head, ConfirmationStatusEnum.已确认, "供应商反馈已登记。"); result.SupplierProgressReportId = report.Uid; return result;
        }, cancellationToken);
    }

    private async Task<ServiceResult<ProcurementCommandResult>> ExecuteMutationAsync(Guid uid, Guid commandId, string operationType, Func<Task<ProcurementCommandResult>> action, CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null) return ServiceResult<ProcurementCommandResult>.Fail("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var prior = await operationLogs.GetByCommandAsync(Module, operationType, commandId);
            if (prior is not null)
            {
                if (prior.AggregateUid != uid) throw new ProcurementException("同一 CommandId 已用于其他采购单。");
                var current = await purchaseOrders.GetHeadForUpdateAsync(uid) ?? throw new ProcurementException("采购单不存在。");
                var replay = Result(current, current.ConfirmationStatus, "命令已执行，本次返回原结果。");
                replay.Outcome = ProcurementCommandOutcomeEnum.AlreadyApplied;
                if (operationType == "SupplierProgress.Reported") replay.SupplierProgressReportId = (await progressReports.GetByCommandAsync(commandId))?.Uid;
                _session.Transaction.Commit();
                return ServiceResult<ProcurementCommandResult>.Ok(replay);
            }
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            _session.Transaction.Commit();
            return ServiceResult<ProcurementCommandResult>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _session.Transaction.Rollback(); throw; }
        catch (ProcurementException ex) { _session.Transaction.Rollback(); return ServiceResult<ProcurementCommandResult>.Fail(ex.Message); }
        catch (Exception ex) { _session.Transaction.Rollback(); _logger.LogError(ex, "采购命令 {OperationType}/{CommandId} 执行失败。", operationType, commandId); return ServiceResult<ProcurementCommandResult>.Fail("采购命令执行失败。"); }
        finally { _session.Transaction.Dispose(); _session.Transaction = null; }
    }

    private async Task<NetDemandPurchaseDraftHead> RequireOperableHeadAsync(Guid uid, int expectedVersion, ConfirmationStatusEnum expectedStatus)
    {
        var head = await purchaseOrders.GetHeadForUpdateAsync(uid) ?? throw new ProcurementException("采购单不存在。");
        if (head.Version != expectedVersion) throw new ProcurementException("采购单已被其他用户修改，请刷新后重试。");
        if (head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active || head.ConfirmationStatus != expectedStatus)
            throw new ProcurementException("采购单当前状态不允许执行此操作。");
        if (!head.SupplierId.HasValue || string.IsNullOrWhiteSpace(head.DemandSourceType) || !head.DemandSourceUid.HasValue || string.IsNullOrWhiteSpace(head.DemandSourceNo) || !head.ExecutionRequestId.HasValue)
            throw new ProcurementException("遗留采购单缺少新链路所需关系，只允许查看。");
        return head;
    }

    private async Task WriteLogAsync(NetDemandPurchaseDraftHead head, Guid commandId, string operationType, string operationName, string? reason, string summary, string? changedFieldsJson, string? relatedObjectsJson)
    {
        var log = new BusinessOperationLog
        {
            Uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlationContext.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId,
            Module = Module,
            AggregateType = "PurchaseOrder",
            AggregateUid = head.Uid,
            AggregateNoSnapshot = head.OrderNo,
            OperationType = operationType,
            OperationName = operationName,
            Reason = Normalize(reason, 1000),
            Summary = Normalize(summary, 2000),
            ChangedFieldsJson = changedFieldsJson,
            RelatedObjectsJson = relatedObjectsJson,
            UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1) throw new ProcurementException("业务操作日志写入失败。");
    }

    private static ProcurementCommandResult Result(NetDemandPurchaseDraftHead head, ConfirmationStatusEnum status, string message)
        => new() { Outcome = ProcurementCommandOutcomeEnum.Succeeded, PurchaseOrderId = head.Uid, Version = head.Version, ConfirmationStatus = status, Message = message };
    private static string? Normalize(string? value, int maxLength) { var text = value?.Trim(); if (string.IsNullOrEmpty(text)) return null; return text.Length <= maxLength ? text : text[..maxLength]; }
    private sealed class ProcurementException(string message) : Exception(message);
}
