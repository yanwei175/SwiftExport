using Dapper;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>采购反向事务的事实读取和持久化；不自行开启事务，不修改库存。</summary>
public sealed class ProcurementReversalRepository(RepositoryBaseContext context) : IProcurementReversalRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken ct) =>
        new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购反向需要事务。"), cancellationToken: ct);

    /// <inheritdoc/>
    public async Task<ProcurementReversalFacts> LockAsync(Guid uid, CancellationToken ct)
    {
        // 发现阶段只读取已提交身份，不持有采购锁；实际使用前按固定锁序重新读取并验证集合。
        const string discover = """
            SELECT DISTINCT n.GrossDemandId FROM dbo.DemandExecutionLink l WITH(READCOMMITTED)
            JOIN dbo.NetDemand n WITH(READCOMMITTED) ON n.Uid=l.NetDemandId
            WHERE l.TargetType=N'PO' AND (l.TargetHeadId=@uid OR l.TargetLineId IN
              (SELECT Uid FROM dbo.PurchaseOrderLine WITH(READCOMMITTED) WHERE PurchaseOrderId=@uid));
            """;
        var sourceIds = (await context.Session.Connection.QueryAsync<Guid>(Command(discover, new { uid }, ct))).OrderBy(x => x).ToArray();
        var facts = new ProcurementReversalFacts();
        foreach (var id in sourceIds)
        {
            var source = await context.Session.Connection.QuerySingleOrDefaultAsync<GrossDemand>(Command(
                "SELECT * FROM dbo.GrossDemand WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@id;", new { id }, ct));
            if (source is null) throw new InvalidOperationException("采购来源需求缺失，不能解除引用。");
            facts.Sources.Add(source);
        }
        foreach (var id in sourceIds)
            facts.Demands.AddRange(await context.Session.Connection.QueryAsync<NetDemand>(Command(
                "SELECT * FROM dbo.NetDemand WITH(UPDLOCK,HOLDLOCK) WHERE GrossDemandId=@id ORDER BY Uid;", new { id }, ct)));
        var demandIds = facts.Demands.Select(x => x.Uid).ToArray();
        if (demandIds.Length > 0)
            facts.Links.AddRange(await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(
                "SELECT * FROM dbo.DemandExecutionLink WITH(UPDLOCK,HOLDLOCK) WHERE NetDemandId IN @demandIds ORDER BY Uid;", new { demandIds }, ct)));
        // 即使来源已损坏也读取本单全部关系；不能把缺失 JOIN 的引用误当独立采购。
        const string ownLinkSql = """
            SELECT * FROM dbo.DemandExecutionLink WITH(UPDLOCK,HOLDLOCK)
            WHERE TargetType=N'PO' AND (TargetHeadId=@uid OR TargetLineId IN
                (SELECT Uid FROM dbo.PurchaseOrderLine WITH(READCOMMITTED) WHERE PurchaseOrderId=@uid)) ORDER BY Uid;
            """;
        var ownLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (ownLinks.Any(x => !demandIds.Contains(x.NetDemandId)))
            throw new ProcurementConcurrencyException("采购引用已变化或物料来源缺失，请重新预览。");
        facts.Links = facts.Links.Concat(ownLinks).DistinctBy(x => x.Uid).ToList();
        facts.Head = await context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command(
            "SELECT * FROM dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@uid;", new { uid }, ct));
        facts.Lines = (await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command(
            "SELECT l.*,l.QTY AS Quantity FROM dbo.PurchaseOrderLine l WITH(UPDLOCK,HOLDLOCK) WHERE PurchaseOrderId=@uid ORDER BY Uid;", new { uid }, ct))).ToList();
        // 头行锁到位后复核集合，发现阶段后追加的新来源必须重走完整锁序，不能在此补锁来源。
        var finalLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (!ownLinks.Select(x => (x.Uid, x.Version, x.NetDemandId, x.TargetLineId))
            .ToHashSet().SetEquals(finalLinks.Select(x => (x.Uid, x.Version, x.NetDemandId, x.TargetLineId))))
            throw new ProcurementConcurrencyException("锁定过程中采购引用集合发生变化，请重新预览。");
        // 采购头锁与入库过账/反过账共用；草稿新引用亦受可串行化范围检查保护。
        var lineIds = facts.Lines.Select(x => x.Uid).ToArray();
        facts.HasUnattributedInbound = await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(
                SELECT 1 FROM dbo.InboundDocument h WITH(HOLDLOCK)
                JOIN dbo.InboundDocumentLine l WITH(HOLDLOCK) ON l.InboundDocumentId=h.Uid
                LEFT JOIN dbo.PurchaseOrderLine p WITH(HOLDLOCK) ON p.Uid=l.SourceLineId
                WHERE h.IsDeleted=0 AND l.IsDeleted=0 AND
                    ((h.PostingStatus IN(1,8) AND h.DocumentType IN(11,12)) OR
                     (h.DocumentType=10 AND p.Uid IS NULL AND (h.PostingStatus IN(1,8) OR h.LifecycleStatus=0))))
                THEN 1 ELSE 0 END);
            """, null, ct));
        if (lineIds.Length == 0) return facts;
        facts.Inbounds = (await context.Session.Connection.QueryAsync<InboundDocument>(Command("""
            SELECT h.* FROM dbo.InboundDocument h WITH(UPDLOCK,HOLDLOCK)
            WHERE EXISTS(SELECT 1 FROM dbo.InboundDocumentLine l WITH(HOLDLOCK)
                         WHERE l.InboundDocumentId=h.Uid AND l.SourceLineId IN @lineIds) ORDER BY h.Uid;
            """, new { lineIds }, ct))).ToList();
        var inboundIds = facts.Inbounds.Select(x => x.Uid).ToArray();
        if (inboundIds.Length > 0)
        {
            facts.InboundLines = (await context.Session.Connection.QueryAsync<InboundDocumentLine>(Command(
                "SELECT * FROM dbo.InboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE InboundDocumentId IN @inboundIds ORDER BY Uid;", new { inboundIds }, ct))).ToList();
            facts.Ledgers = (await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command("""
                SELECT e.* FROM dbo.InventoryLedgerEntry e WITH(HOLDLOCK)
                WHERE e.SourceDocumentId IN @inboundIds OR e.SourceLineId IN
                    (SELECT Uid FROM dbo.InboundDocumentLine WHERE InboundDocumentId IN @inboundIds)
                   OR e.ReversalOfLedgerId IN (SELECT Uid FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId IN @inboundIds)
                   OR e.ReplacementOfLedgerId IN (SELECT Uid FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId IN @inboundIds);
                """, new { inboundIds }, ct))).ToList();
        }
        facts.Reports = (await context.Session.Connection.QueryAsync<SupplierProgressReport>(Command(
            "SELECT * FROM dbo.SupplierProgressReport WITH(HOLDLOCK) WHERE PurchaseOrderLineId IN @lineIds AND IsDeleted=0 ORDER BY PurchaseOrderLineId,Sequence;", new { lineIds }, ct))).ToList();
        return facts;
    }

    /// <inheritdoc/>
    public async Task ApplyAsync(ProcurementReversalFacts facts, ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds, string reason, CancellationToken ct)
    {
        var head = facts.Head!;
        context.AuditStamp.StampForUpdate(head);
        var cancel = operation == ProcurementReversalOperation.CancelConfirmation;
        var delete = operation == ProcurementReversalOperation.DeleteLines;
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PurchaseOrderHead SET LifecycleStatus=CASE WHEN @Void=1 THEN 9 ELSE LifecycleStatus END,
                ConfirmationStatus=CASE WHEN @Cancel=1 THEN 0 ELSE ConfirmationStatus END,
                ActiveConfirmationId=CASE WHEN @Cancel=1 THEN NULL ELSE ActiveConfirmationId END,
                ConfirmedDate=CASE WHEN @Cancel=1 THEN NULL ELSE ConfirmedDate END,
                ConfirmedBy=CASE WHEN @Cancel=1 THEN NULL ELSE ConfirmedBy END,
                UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@Version AND LifecycleStatus=0 AND IsDeleted=0;
            """, new { head.Uid, head.Version, head.UpdatedDate, head.UpdatedBy, Cancel = cancel, Void = operation == ProcurementReversalOperation.Void }, ct)) != 1)
            throw new ProcurementConcurrencyException("采购状态已变化，反向操作未执行。");
        head.Version++;
        if (cancel)
        {
            head.ConfirmationStatus = ConfirmationStatusEnum.待确认;
            head.ActiveConfirmationId = null; head.ConfirmedDate = null; head.ConfirmedBy = null;
        }
        else
        {
            foreach (var link in facts.Links.Where(x => x.TargetType == "PO" && x.TargetHeadId == head.Uid && lineIds.Contains(x.TargetLineId) && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active))
            {
                context.AuditStamp.StampForUpdate(link);
                if (await context.Session.Connection.ExecuteAsync(Command("""
                    UPDATE dbo.DemandExecutionLink SET LinkStatus=8,ReleasedQty=AllocatedQty,CancelledAt=@UpdatedDate,
                        CancelledBy=@UpdatedBy,CancelledReason=@reason,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
                    WHERE Uid=@Uid AND Version=@Version AND LinkStatus=0 AND IsDeleted=0;
                    """, new { link.Uid, link.Version, link.UpdatedDate, link.UpdatedBy, reason }, ct)) != 1)
                    throw new ProcurementConcurrencyException("采购贡献已变化，解除引用未执行。");
                link.Version++; link.LinkStatus = DemandExecutionLinkStatusEnum.Cancelled; link.ReleasedQty = link.AllocatedQty;
                link.CancelledAt = link.UpdatedDate; link.CancelledBy = link.UpdatedBy; link.CancelledReason = reason;
            }
            if (delete)
            {
                foreach (var line in facts.Lines.Where(x => lineIds.Contains(x.Uid)))
                {
                    context.AuditStamp.StampForUpdate(line);
                    if (await context.Session.Connection.ExecuteAsync(Command("""
                        UPDATE dbo.PurchaseOrderLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                        WHERE Uid=@Uid AND PurchaseOrderId=@PurchaseOrderId AND Version=@Version AND IsDeleted=0;
                        """, line, ct)) != 1) throw new ProcurementConcurrencyException("采购行已变化，删除未执行。");
                    line.IsDeleted = true; line.Version++;
                }
                head.ExpectedDate = facts.Lines.Where(x => !x.IsDeleted).Min(x => x.PlannedDeliveryDate);
                await context.Session.Connection.ExecuteAsync(Command(
                    "UPDATE dbo.PurchaseOrderHead SET ExpectedDate=@ExpectedDate WHERE Uid=@Uid;", new { head.Uid, head.ExpectedDate }, ct));
            }
            else head.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
        }
    }
}
