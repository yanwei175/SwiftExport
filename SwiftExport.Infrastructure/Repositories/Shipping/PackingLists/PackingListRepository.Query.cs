using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>读取装箱单列表、详情与来源展示补充，不写入业务事实。</summary>
public sealed partial class PackingListRepository
{
    /// <inheritdoc/>
    public async Task<PagedResponseBase<PackingListManagerListItemDto>> SearchAsync(
        PackingListManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM dbo.PackingListHead h
            WHERE h.IsDeleted=0
              AND (@CustomerId IS NULL OR h.CustomerId=@CustomerId)
              AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus)
              AND (@DateFrom IS NULL OR h.InvoiceDate>=@DateFrom)
              AND (@DateTo IS NULL OR h.InvoiceDate<DATEADD(day,1,@DateTo))
              AND (@Keyword IS NULL OR h.PackingListNo LIKE @Keyword ESCAPE '~'
                OR h.CustomerNameSnapshot LIKE @Keyword ESCAPE '~'
                OR h.ContainerNo LIKE @Keyword ESCAPE '~'
                OR h.SealNo LIKE @Keyword ESCAPE '~'
                OR EXISTS(SELECT 1 FROM dbo.PackingListLine kl
                    WHERE kl.PackingListHeadId=h.Uid AND kl.IsDeleted=0
                      AND kl.InvoiceNo LIKE @Keyword ESCAPE '~'))
            """;
        var sql = "SELECT COUNT(1) " + filter + "; SELECT " + HeaderListColumns + "," + InvoiceSummary + " AS InvoiceNosSummary\n"
            + filter + " ORDER BY h.InvoiceDate DESC,h.Uid DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new
        {
            request.CustomerId,
            request.PostingStatus,
            DateFrom = request.InvoiceDateFrom?.Date,
            DateTo = request.InvoiceDateTo?.Date,
            Keyword = Like(request.Keyword),
            Skip = (long)(request.PageIndex - 1) * request.PageSize,
            Take = request.PageSize
        }, cancellationToken));
        return new PagedResponseBase<PackingListManagerListItemDto>
        {
            TotalCount = await reader.ReadSingleAsync<int>(),
            Items = (await reader.ReadAsync<PackingListManagerListItemDto>()).ToList(),
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <inheritdoc/>
    public async Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var sql = "SELECT " + HeaderDetailColumns + "," + InvoiceSummary + " AS InvoiceNosSummary FROM dbo.PackingListHead h WHERE h.Uid=@Uid AND h.IsDeleted=0;\n"
            + """
            SELECT l.Uid,l.Version,l.IsDeleted,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,
                   l.PackingListHeadId,l.LineNumber,l.GrossDemandId,l.ProductId,l.IsNonStandardSnapshot,
                   l.SourceTypeSnapshot,l.SourceDocumentNoSnapshot,l.SourceLineNumberSnapshot,l.SkuSnapshot,
                   l.LinkSkuSnapshot,l.CustomerSkuSnapshot,l.DescriptionEnSnapshot,l.ColorNameEnSnapshot,
                   l.PackingGroupId,l.UnitSnapshot,l.Qty,l.PcsPerPackage,l.PackageCount,l.NwPerPackage,l.GwPerPackage,
                   l.CbmPerPackage,l.SubTotalNw,l.SubTotalGw,l.SubTotalCbm,l.CustomerPoNo,l.InvoiceNo,
                   l.Marks,l.Note,
                   g.ImagePathSnapshot AS ImagePath, CASE WHEN g.SourceType=N'SO' THEN soh.InvoiceNo WHEN g.SourceType=N'CR' THEN ash.AfterSalesNo END AS SourceInvoiceNo
            FROM dbo.PackingListLine l
            LEFT JOIN dbo.GrossDemand g ON g.Uid=l.GrossDemandId AND g.IsDeleted=0
            LEFT JOIN dbo.SalesOrderHeader soh ON g.SourceType=N'SO' AND soh.Uid=g.SourceUid AND soh.CustomerId=g.CustomerId AND soh.IsDeleted=0
            LEFT JOIN dbo.AfterSalesHeader ash ON g.SourceType=N'CR' AND ash.Uid=g.SourceUid AND ash.CustomerId=g.CustomerId AND ash.IsDeleted=0
            WHERE l.PackingListHeadId=@Uid AND l.IsDeleted=0
            ORDER BY l.LineNumber,l.Uid;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new { Uid = uid }, cancellationToken));
        var header = await reader.ReadSingleOrDefaultAsync<PackingListHeaderDto>();
        var lines = (await reader.ReadAsync<PackingListLineDto>()).ToList();
        return header is null ? null : new PackingListDetailDto { Header = header, Lines = lines };
    }

    /// <summary>读取默认包装与客户参考号；销售取客户 PO，售后取单头客户投诉单号，仅作箱单行快照补充。</summary>
    public async Task<IReadOnlyList<PackingListSourceEnrichment>> GetSourceEnrichmentsAsync(
        IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default)
    {
        if (grossDemandIds.Count == 0) return Array.Empty<PackingListSourceEnrichment>();
        var rows = await context.Session.Connection.QueryAsync<PackingListSourceEnrichment>(Command("""
            SELECT g.Uid AS GrossDemandId,
                   CASE WHEN pkg.PackageCount=1 THEN CONVERT(decimal(18,4),pkg.PcsPerPackage) END AS PcsPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.NwPerPackage END AS NwPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.GwPerPackage END AS GwPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.CbmPerPackage END AS CbmPerPackage,
                   CASE WHEN g.SourceType=N'SO' AND soh.Uid IS NOT NULL AND sol.Uid IS NOT NULL
                        THEN soh.CustomerPoNo
                        WHEN g.SourceType=N'CR' AND ash.Uid IS NOT NULL AND asl.Uid IS NOT NULL
                        THEN ash.CustomerComplaintNo END AS CustomerPoNo,
                   CASE WHEN g.SourceType=N'SO' AND soh.Uid IS NOT NULL AND sol.Uid IS NOT NULL THEN soh.InvoiceNo
                        WHEN g.SourceType=N'CR' AND ash.Uid IS NOT NULL AND asl.Uid IS NOT NULL THEN ash.AfterSalesNo END AS InvoiceNo
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.SalesOrderHeader soh ON g.SourceType=N'SO' AND soh.Uid=g.SourceUid
                AND soh.CustomerId=g.CustomerId AND soh.IsDeleted=0
            LEFT JOIN dbo.SalesOrderLine sol ON g.SourceType=N'SO' AND sol.Uid=g.SourceLineId
                AND sol.SalesOrderId=soh.Uid AND sol.ProductId=g.ProductId
                AND sol.IsNonStandard=g.IsNonStandard AND sol.IsDeleted=0
            LEFT JOIN dbo.AfterSalesHeader ash ON g.SourceType=N'CR' AND ash.Uid=g.SourceUid
                AND ash.CustomerId=g.CustomerId AND ash.IsDeleted=0
            LEFT JOIN dbo.AfterSalesLine asl ON g.SourceType=N'CR' AND asl.Uid=g.SourceLineId
                AND asl.HeaderId=ash.Uid AND asl.IsDeleted=0
            OUTER APPLY(
                SELECT COUNT(1) AS PackageCount,MAX(pp.PcsPerPackage) AS PcsPerPackage,
                       MAX(pp.Nw_KG) AS NwPerPackage,MAX(pp.Gw_KG) AS GwPerPackage,MAX(pp.Cbm) AS CbmPerPackage
                FROM dbo.ProductPackage pp
                WHERE g.IsNonStandard=0 AND pp.ProductId=g.ProductId AND pp.IsDeleted=0 AND pp.IsDefault=1
            ) pkg
            WHERE g.Uid IN @Ids AND g.IsDeleted=0;
            """, new { Ids = grossDemandIds }, cancellationToken));
        return rows.ToList();
    }

}
