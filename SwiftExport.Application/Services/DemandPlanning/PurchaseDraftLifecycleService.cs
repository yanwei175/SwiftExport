using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>旧需求采购执行链草稿的安全取消和减量编排，不接管新采购订单。</summary>
public sealed class PurchaseDraftLifecycleService(
    ServiceBaseContext context,
    IPurchaseOrderDraftRepository purchaseDraftRepository,
    INetDemandExecutionLinkRepository linkRepository,
    PurchaseDraftReleasePlanner releasePlanner)
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<PurchaseDraftLifecycleService> _logger =
        context.LoggerFactory.CreateLogger<PurchaseDraftLifecycleService>();

    /// <summary>安全取消旧链需求采购草稿；已取消重放也先检查执行链归属。</summary>
    public async Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(
        Guid uid,
        CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = ValidateCommand(uid, command.CommandId,
            command.ExpectedVersion, command.Reason);
        if (validation is not null) return ServiceResult<PurchaseDraftLifecycleResult>.Fail(validation);
        if (_session.Transaction is not null)
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail("当前请求存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var head = await purchaseDraftRepository.GetLifecycleDraftAsync(uid, withUpdLock: true);
            if (head is null)
                return RollbackFail<PurchaseDraftLifecycleResult>("采购草稿不存在，或属于缺少新来源字段的遗留采购单。");
            // 已取消的重放在状态门禁之前返回。
            if (head.LifecycleStatus == PurchaseOrderLifecycleStatusEnum.Voided)
            {
                _session.Transaction.Commit();
                return ServiceResult<PurchaseDraftLifecycleResult>.Ok(new PurchaseDraftLifecycleResult
                {
                    PurchaseOrderId = uid,
                    Version = head.Version,
                    LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided,
                    Outcome = NetDemandCommandOutcomeEnum.AlreadyApplied,
                    Message = "采购草稿已经取消。",
                });
            }
            EnsureMutableDraft(head, command.ExpectedVersion);
            if (await purchaseDraftRepository.HasDownstreamFactsAsync(uid, withUpdLock: true))
                return RollbackFail<PurchaseDraftLifecycleResult>("采购草稿已有收货或库存事实，不能取消。");

            var links = await linkRepository.GetPurchaseLinksByHeadAsync(uid, withUpdLock: true, cancellationToken);
            if (links.Count == 0)
                return RollbackFail<PurchaseDraftLifecycleResult>("采购草稿没有完整的新净需求 Link，不能执行专项取消。");
            if (links.Any(x => x.LinkId == Guid.Empty || x.AllocationId == Guid.Empty
                || x.PurchaseOrderLineId == Guid.Empty))
                return RollbackFail<PurchaseDraftLifecycleResult>("采购草稿混有遗留或不完整 Link，不能执行专项取消。");
            _ = await purchaseDraftRepository.GetDraftLinesForUpdateAsync(uid);
            if (await linkRepository.CancelPurchaseLinksByHeadAsync(uid, command.Reason.Trim()) != links.Count)
                throw new PurchaseDraftLifecycleException("LINK_CONCURRENCY_CONFLICT", "取消 Link 时事实已变化。");
            if (await purchaseDraftRepository.VoidDraftAsync(uid, command.ExpectedVersion, command.Reason.Trim()) != 1)
                throw new PurchaseDraftLifecycleException("PURCHASE_DRAFT_CONCURRENCY_CONFLICT", "采购草稿版本已变化。");

            _session.Transaction.Commit();
            return ServiceResult<PurchaseDraftLifecycleResult>.Ok(new PurchaseDraftLifecycleResult
            {
                PurchaseOrderId = uid,
                Version = command.ExpectedVersion + 1,
                LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided,
                Outcome = NetDemandCommandOutcomeEnum.Succeeded,
                ReleasedQty = links.Sum(x => x.EffectiveQty),
                Message = "采购草稿已取消，净需求剩余量已恢复。",
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (PurchaseDraftLifecycleException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail($"{ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "取消采购草稿 {PurchaseOrderId} 失败。", uid);
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail("取消采购草稿失败。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(
        Guid uid,
        PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult<PurchaseDraftReleasePreviewResponse>.Fail("采购草稿、版本和减量原因不能为空。");
        try
        {
            var head = await purchaseDraftRepository.GetLifecycleDraftAsync(uid);
            if (head is null)
                return ServiceResult<PurchaseDraftReleasePreviewResponse>.Fail("采购草稿不存在，或属于遗留采购单。");
            EnsureMutableDraft(head, request.ExpectedVersion);
            var links = await linkRepository.GetPurchaseLinksByHeadAsync(uid, false, cancellationToken);
            var preview = releasePlanner.Build(uid, request.ExpectedVersion, request.NewTotalQty, links);
            if (await purchaseDraftRepository.HasDownstreamFactsAsync(uid))
            {
                preview.Blockers.Add("采购草稿已有收货或库存事实，不能减量。");
                preview.HasBlockers = true;
            }
            return ServiceResult<PurchaseDraftReleasePreviewResponse>.Ok(preview);
        }
        catch (PurchaseDraftLifecycleException ex)
        {
            return ServiceResult<PurchaseDraftReleasePreviewResponse>.Fail($"{ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "预览采购草稿 {PurchaseOrderId} 减量失败。", uid);
            return ServiceResult<PurchaseDraftReleasePreviewResponse>.Fail("预览采购草稿减量失败。");
        }
    }

    public async Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(
        Guid uid,
        ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = ValidateCommand(uid, command.CommandId,
            command.ExpectedVersion, command.Reason);
        if (validation is not null || string.IsNullOrWhiteSpace(command.PreviewToken))
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail(validation ?? "预览令牌不能为空。");
        if (_session.Transaction is not null)
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail("当前请求存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var head = await purchaseDraftRepository.GetLifecycleDraftAsync(uid, withUpdLock: true)
                ?? throw new PurchaseDraftLifecycleException("LEGACY_OR_NOT_FOUND", "采购草稿不存在，或属于遗留采购单。");
            EnsureMutableDraft(head, command.ExpectedVersion);
            if (await purchaseDraftRepository.HasDownstreamFactsAsync(uid, withUpdLock: true))
                throw new PurchaseDraftLifecycleException("DOWNSTREAM_FACT_EXISTS", "采购草稿已有收货或库存事实，不能减量。");

            var links = await linkRepository.GetPurchaseLinksByHeadAsync(uid, withUpdLock: true, cancellationToken);
            var lines = await purchaseDraftRepository.GetDraftLinesForUpdateAsync(uid);
            if (lines.Sum(x => x.Quantity) != links.Sum(x => x.EffectiveQty))
                throw new PurchaseDraftLifecycleException("CORRUPTED_STATE", "采购行数量与活动 Link 有效贡献不守恒。");
            var preview = releasePlanner.Build(uid, command.ExpectedVersion, command.NewTotalQty, links);
            if (preview.HasBlockers)
                throw new PurchaseDraftLifecycleException("RELEASE_BLOCKED", string.Join("；", preview.Blockers));
            if (!string.Equals(preview.PreviewToken, command.PreviewToken, StringComparison.Ordinal))
                throw new PurchaseDraftLifecycleException("PREVIEW_EXPIRED", "采购草稿事实已变化，请重新预览减量结果。");

            var quantities = lines.ToDictionary(x => x.Uid, x => x.Quantity);
            foreach (var release in preview.Releases)
            {
                if (!quantities.TryGetValue(release.PurchaseOrderLineId, out var current))
                    throw new PurchaseDraftLifecycleException("CORRUPTED_STATE", "Link 指向的采购行不存在。");
                var next = current - release.ReleaseQty;
                if (next < 0)
                    throw new PurchaseDraftLifecycleException("CORRUPTED_STATE", "采购行数量小于待释放 Link 贡献。");
                quantities[release.PurchaseOrderLineId] = next;
            }
            var changed = quantities.Where(x => lines.Single(line => line.Uid == x.Key).Quantity != x.Value)
                .ToDictionary(x => x.Key, x => x.Value);
            if (await linkRepository.ApplyPurchaseLinkReleasesAsync(preview.Releases, command.Reason.Trim())
                != preview.Releases.Count)
                throw new PurchaseDraftLifecycleException("LINK_CONCURRENCY_CONFLICT", "释放 Link 时事实已变化。");
            if (await purchaseDraftRepository.UpdateDraftLineQuantitiesAsync(changed, command.Reason.Trim()) != changed.Count)
                throw new PurchaseDraftLifecycleException("PURCHASE_LINE_CONCURRENCY_CONFLICT", "更新采购行数量时事实已变化。");
            if (await purchaseDraftRepository.AdvanceDraftVersionAfterReleaseAsync(uid, command.ExpectedVersion) != 1)
                throw new PurchaseDraftLifecycleException("PURCHASE_DRAFT_CONCURRENCY_CONFLICT", "采购草稿版本已变化。");

            _session.Transaction.Commit();
            return ServiceResult<PurchaseDraftLifecycleResult>.Ok(new PurchaseDraftLifecycleResult
            {
                PurchaseOrderId = uid,
                Version = command.ExpectedVersion + 1,
                LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
                Outcome = NetDemandCommandOutcomeEnum.Succeeded,
                ReleasedQty = preview.ReleaseQty,
                Message = "采购草稿已减量，Link 贡献和净需求剩余量已同步。",
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (PurchaseDraftLifecycleException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail($"{ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "释放采购草稿 {PurchaseOrderId} 数量失败。", uid);
            return ServiceResult<PurchaseDraftLifecycleResult>.Fail("释放采购草稿数量失败。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private ServiceResult<T> RollbackFail<T>(string message)
    {
        _session.Transaction!.Rollback();
        return ServiceResult<T>.Fail(message);
    }

    /// <summary>验证采购草稿版本和可变状态，覆盖减量预览与实际执行。</summary>
    private static void EnsureMutableDraft(PurchaseOrderHead head, int expectedVersion)
    {
        if (head.Version != expectedVersion)
            throw new PurchaseDraftLifecycleException("CONCURRENCY_CONFLICT", "采购草稿版本已变化。");
        if (head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active
            || head.ConfirmationStatus != ConfirmationStatusEnum.待确认)
            throw new PurchaseDraftLifecycleException("DRAFT_NOT_MUTABLE", "只有活动且待确认的采购草稿允许取消或减量。");
    }

    private static string? ValidateCommand(Guid uid, Guid commandId, int version, string? reason)
    {
        if (uid == Guid.Empty) return "采购草稿 Uid 不能为空。";
        if (commandId == Guid.Empty) return "命令 Uid 不能为空。";
        if (version <= 0) return "采购草稿版本无效。";
        if (string.IsNullOrWhiteSpace(reason)) return "原因不能为空。";
        if (reason.Trim().Length > 500) return "原因不能超过 500 个字符。";
        return null;
    }
}

internal sealed class PurchaseDraftLifecycleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
