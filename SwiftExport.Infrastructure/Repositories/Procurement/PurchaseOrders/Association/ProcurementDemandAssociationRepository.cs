using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Association;

/// <summary>采购补关联的窄查询和版本写入；使用当前 DbSession，不读入库或改库存。</summary>
public sealed class ProcurementDemandAssociationRepository(RepositoryBaseContext context) : IProcurementDemandAssociationRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken ct) =>
        new(sql, args, context.Session.Transaction, cancellationToken: ct);

    /// <inheritdoc/>
    public async Task<PagedResponseBase<ProcurementDemandCandidateDto>> SearchCandidatesAsync(Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken ct)
    {
        // 候选允许未知单位显示，提交时明确拒绝；不把 PCS 或当前产品单位伪装成拆解快照。
        const string sql = """
            SELECT n.Uid AS NetDemandId,n.Version,g.Uid AS GrossDemandId,g.Version AS SourceDemandVersion,
                   n.DecompositionRevision,g.SourceType,g.SourceUid,g.SourceNo,g.SourceLineNumber,n.ProductId,
                   g.IsNonStandard,n.SkuSnapshot AS Sku,n.CustomerSkuSnapshot AS CustomerSku,
                   n.DescriptionSnapshot AS Description,n.MaterialTypeSnapshot AS MaterialType,
                   n.SupplyTypeSnapshot AS SupplyType,n.UnitSnapshot AS Unit,n.DeliveryTargetNetDemandId,
                   n.NetQty,COALESCE(used.Qty,0) AS LinkedQty,n.NetQty-COALESCE(used.Qty,0) AS RemainingQty
            INTO #Candidates
            FROM dbo.NetDemand n JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            JOIN dbo.PurchaseOrderHead h ON h.Uid=@uid AND h.CustomerId=g.CustomerId
            JOIN dbo.PurchaseOrderLine p ON p.PurchaseOrderId=h.Uid AND p.Uid=@LineId
            OUTER APPLY(SELECT SUM(l.AllocatedQty-l.ReleasedQty) AS Qty FROM dbo.DemandExecutionLink l
                WHERE l.NetDemandId=n.Uid AND l.TargetType=N'PO' AND l.IsDeleted=0 AND l.AllocatedQty>l.ReleasedQty) used
            WHERE n.IsDeleted=0 AND n.LifecycleStatus=0 AND g.IsDeleted=0 AND g.LifecycleStatus=0
              AND h.IsDeleted=0 AND h.LifecycleStatus=0 AND h.ConfirmationStatus IN(0,1) AND p.IsDeleted=0
              AND p.UnlinkedQty>0 AND n.NetQty>COALESCE(used.Qty,0) AND n.DecompositionRevision>0
              AND n.SourceDemandBasisFingerprintSnapshot=g.DemandBasisFingerprint
              AND NULLIF(g.DemandBasisFingerprint,N'') IS NOT NULL
              AND ((p.ProductId=n.ProductId AND g.IsNonStandard=0
                      AND p.MaterialType=n.MaterialTypeSnapshot AND p.SupplyType=n.SupplyTypeSnapshot)
                   OR (p.ProductId IS NULL AND n.ProductId IS NULL AND g.IsNonStandard=1))
              AND n.SupplyTypeSnapshot=N'采购'
              AND (@SourceUid IS NULL OR g.SourceUid=@SourceUid)
              AND (@SourceType IS NULL OR g.SourceType=@SourceType)
              AND (h.DemandSourceUid IS NULL OR (g.SourceUid=h.DemandSourceUid AND g.SourceType=h.DemandSourceType))
              AND (@Keyword IS NULL OR g.SourceNo LIKE @Keyword ESCAPE '~' OR n.SkuSnapshot LIKE @Keyword ESCAPE '~'
                   OR n.CustomerSkuSnapshot LIKE @Keyword ESCAPE '~' OR n.DescriptionSnapshot LIKE @Keyword ESCAPE '~');
            SELECT COUNT(*) FROM #Candidates;
            SELECT NetDemandId,Version,GrossDemandId,SourceDemandVersion,DecompositionRevision,SourceType,SourceUid,
                   SourceNo,SourceLineNumber,ProductId,IsNonStandard,Sku,CustomerSku,Description,MaterialType,SupplyType,
                   Unit,DeliveryTargetNetDemandId,NetQty,LinkedQty,RemainingQty
            FROM #Candidates ORDER BY SourceNo,SourceType,SourceUid,SourceLineNumber,NetDemandId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #Candidates;
            """;
        var keyword = request.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword)) keyword = "%" + keyword.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new
        {
            uid, LineId = request.PurchaseOrderLineId, request.SourceUid,
            SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? null : request.SourceType.Trim(),
            Keyword = string.IsNullOrEmpty(keyword) ? null : keyword,
            Skip = (long)(request.PageIndex-1)*request.PageSize, Take = request.PageSize
        }, ct));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<ProcurementDemandCandidateDto>()).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProcurementAssociationTarget>> ReadTargetsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct)
    {
        var result = new List<ProcurementAssociationTarget>();
        foreach (var ids in lineIds.Distinct().Order().Chunk(500))
            result.AddRange(await context.Session.Connection.QueryAsync<ProcurementAssociationTarget>(Command("""
                SELECT p.Uid AS LineId,h.Uid AS HeadId,h.SupplierId,h.Version AS HeadVersion,p.Version AS LineVersion,
                       p.ProductId,p.DeliveryTargetSupplierId
                FROM dbo.PurchaseOrderLine p JOIN dbo.PurchaseOrderHead h ON h.Uid=p.PurchaseOrderId
                WHERE p.Uid IN @ids AND p.IsDeleted=0 AND h.IsDeleted=0 AND h.LifecycleStatus=0
                ORDER BY h.Uid,p.Uid;
                """, new { ids }, ct)));
        return result;
    }

    /// <inheritdoc/>
    public async Task<RepositoryWriteResult> ApplyAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("采购关联写入必须加入公共事务。");
        var version = head.Version;
        context.AuditStamp.StampForUpdate(head);
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PurchaseOrderHead SET DemandSourceType=@DemandSourceType,DemandSourceUid=@DemandSourceUid,
                DemandSourceNo=@DemandSourceNo,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND LifecycleStatus=0 AND ConfirmationStatus IN(0,1);
            """, head, ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购版本或状态已变化，请重新预览。");
        head.Version++;
        foreach (var line in lines.OrderBy(x => x.Uid))
        {
            context.AuditStamp.StampForUpdate(line);
            if (await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.PurchaseOrderLine SET UnlinkedQty=@UnlinkedQty,SourceType=@SourceType,SourceUid=@SourceUid,
                    SourceNo=@SourceNo,SourceLineId=@SourceLineId,SourceLineNumber=@SourceLineNumber,
                    Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND PurchaseOrderId=@PurchaseOrderId AND Version=@Version AND IsDeleted=0;
                """, line, ct)) != 1)
                return RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "采购行版本已变化，请重新预览。");
            line.Version++;
        }
        return RepositoryWriteResult.Ok(head.Uid, version, head.Version);
    }
}
