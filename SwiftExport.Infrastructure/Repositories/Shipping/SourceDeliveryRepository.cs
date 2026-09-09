using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>在交付领域聚合来源要求和正式出库事实，并提供与来源变更共享的行级锁定协议。</summary>
public sealed class SourceDeliveryRepository(RepositoryBaseContext context) : ISourceDeliveryRepository
{
    /// <inheritdoc />
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(request.PageIndex, 1);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var parameters = Parameters(request, page, size);
        const string where = """
            WHERE g.IsDeleted=0 AND g.LifecycleStatus=@Active
              AND g.SourceType IN (@SalesOrder,@Complaint)
              AND (@SourceType IS NULL OR g.SourceType=@SourceType)
              AND (@CustomerId IS NULL OR g.CustomerId=@CustomerId)
              AND (@Keyword IS NULL OR g.SourceNo LIKE @Keyword OR g.SkuSnapshot LIKE @Keyword OR g.CustomerSkuSnapshot LIKE @Keyword)
              AND (@SourceNo IS NULL OR g.SourceNo LIKE @SourceNo)
              AND (@Sku IS NULL OR g.SkuSnapshot LIKE @Sku)
              AND g.DemandQty-COALESCE(delivery.DeliveredQty,0)>0
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM dbo.GrossDemand g {SourceDeliverySql.FinalOutboundApply} {where};",
            parameters,
            context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<SourceDemandDeliveryItemDto>(new CommandDefinition($"""
            {SelectProjection}
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.Customer customer ON customer.Uid=g.CustomerId AND customer.IsDeleted=0
            LEFT JOIN dbo.Product product ON product.Uid=g.ProductId AND product.IsDeleted=0
            {SourceDeliverySql.FinalOutboundApply}
            {where}
            ORDER BY g.RequiredDate,g.SourceNo,g.SourceLineNumber,g.Uid
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<SourceDemandDeliveryItemDto>
        {
            Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size
        };
    }

    /// <inheritdoc />
    public Task<SourceDemandDeliveryItemDto?> GetAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
        => context.Session.Connection.QuerySingleOrDefaultAsync<SourceDemandDeliveryItemDto>(new CommandDefinition($"""
            {SelectProjection}
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.Customer customer ON customer.Uid=g.CustomerId AND customer.IsDeleted=0
            LEFT JOIN dbo.Product product ON product.Uid=g.ProductId AND product.IsDeleted=0
            {SourceDeliverySql.FinalOutboundApply}
            WHERE g.Uid=@GrossDemandId AND g.IsDeleted=0;
            """, new
        {
            GrossDemandId = grossDemandId,
            SalesOrder = DocumentTypeStaticsEnum.SalesOrder,
            Complaint = DocumentTypeStaticsEnum.ComplaintReplenishment
        }, context.Session.Transaction, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(
        IEnumerable<Guid> grossDemandIds,
        CancellationToken cancellationToken = default)
    {
        var ids = grossDemandIds.Distinct().OrderBy(id => id).ToArray();
        var rows = new List<SourceDemandDeliveryItemDto>();
        foreach (var id in ids)
        {
            var row = await GetLockedAsync("g.Uid=@GrossDemandId", new
            {
                GrossDemandId = id,
                SalesOrder = DocumentTypeStaticsEnum.SalesOrder,
                Complaint = DocumentTypeStaticsEnum.ComplaintReplenishment
            }, cancellationToken);
            if (row.Count == 1) rows.Add(row[0]);
        }
        return rows;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetSourceForClosureForUpdateAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
        => GetLockedAsync("g.SourceType=@SourceType AND g.SourceUid=@SourceUid", new
        {
            SourceType = sourceType,
            SourceUid = sourceUid,
            SalesOrder = DocumentTypeStaticsEnum.SalesOrder,
            Complaint = DocumentTypeStaticsEnum.ComplaintReplenishment
        }, cancellationToken);

    /// <summary>锁定来源本体后再读取正式出库聚合；调用方事务使来源变更与交付提交串行。</summary>
    private async Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetLockedAsync(
        string predicate,
        object parameters,
        CancellationToken cancellationToken)
    {
        var rows = await context.Session.Connection.QueryAsync<SourceDemandDeliveryItemDto>(new CommandDefinition($"""
            {SelectProjection}
            FROM dbo.GrossDemand g WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.Customer customer ON customer.Uid=g.CustomerId AND customer.IsDeleted=0
            LEFT JOIN dbo.Product product ON product.Uid=g.ProductId AND product.IsDeleted=0
            {SourceDeliverySql.FinalOutboundApply}
            WHERE {predicate} AND g.IsDeleted=0
            ORDER BY g.Uid;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static object Parameters(SourceDemandDeliverySearchRequest request, int page, int size) => new
    {
        SourceType = Normalize(request.SourceType),
        request.CustomerId,
        Keyword = Like(request.Keyword),
        SourceNo = Like(request.SourceNo),
        Sku = Like(request.Sku),
        SalesOrder = DocumentTypeStaticsEnum.SalesOrder,
        Complaint = DocumentTypeStaticsEnum.ComplaintReplenishment,
        Active = GrossDemandLifecycleStatusEnum.Active,
        Offset = (page - 1) * size,
        PageSize = size
    };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? Like(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";

    private const string SelectProjection = """
        SELECT g.Uid AS GrossDemandId,g.Version AS DemandVersion,g.SourceType,g.SourceUid,g.SourceNo,
               g.SourceLineId,g.SourceLineNumber,g.SourceVersion,g.CustomerId,
               customer.CustomerCode,customer.CustomerName,g.ProductId,g.SkuSnapshot AS Sku,
               g.CustomerSkuSnapshot AS CustomerSku,g.DescriptionSnapshot AS Description,g.UnitSnapshot AS Unit,
               g.DemandQty,COALESCE(delivery.DeliveredQty,0) AS DeliveredQty,
               g.DemandQty-COALESCE(delivery.DeliveredQty,0) AS RemainingQty,g.LifecycleStatus,
               CAST(CASE WHEN g.LifecycleStatus=0 AND g.IsDeleted=0 THEN 1 ELSE 0 END AS bit) AS IsActive,
               g.RequiredDate,CASE WHEN g.SourceType=@SalesOrder THEN g.SourceUid ELSE NULL END AS OrderPoolSalesOrderId,
               CAST(CASE WHEN g.SourceType IN (@SalesOrder,@Complaint) AND g.SourceVersion IS NOT NULL
                              AND g.CustomerId IS NOT NULL AND product.Uid IS NOT NULL
                              AND NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') IS NOT NULL
                         THEN 1 ELSE 0 END AS bit) AS IsInventoryActionAllowed,
               CASE WHEN g.SourceType NOT IN (@SalesOrder,@Complaint) THEN N'SOURCE_TYPE_NOT_DELIVERABLE'
                    WHEN g.SourceVersion IS NULL THEN N'SOURCE_VERSION_MISSING'
                    WHEN g.CustomerId IS NULL THEN N'CUSTOMER_MISSING'
                    WHEN product.Uid IS NULL THEN N'PRODUCT_IDENTITY_REQUIRED'
                    WHEN NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') IS NULL THEN N'UNIT_REQUIRED'
                    ELSE NULL END AS InventoryBlockerCode
        """;
}
