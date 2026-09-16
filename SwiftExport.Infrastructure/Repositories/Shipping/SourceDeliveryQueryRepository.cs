using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>以 GrossDemand 为入口读取来源，并以有效已过账 PackingList 行计算正式交付净额。</summary>
public sealed class SourceDeliveryQueryRepository(RepositoryBaseContext context) : ISourceDeliveryQueryRepository
{
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.GrossDemand',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.PackingListHead',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.PackingListLine',N'U') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'IsNonStandardSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'SourceTypeSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'SourceDocumentNoSnapshot') IS NOT NULL
              THEN 1 ELSE 0 END);
            """, null, cancellationToken));

    /// <inheritdoc/>
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var sql = Projection(false) + """
            SELECT * INTO #SourceDelivery FROM SourceDelivery
            WHERE IsActive=1 AND CustomerId IS NOT NULL AND RemainingQty>0
              AND (@SourceType IS NULL OR SourceType=@SourceType)
              AND (@CustomerId IS NULL OR CustomerId=@CustomerId)
              AND (@SourceNo IS NULL OR SourceNo LIKE @SourceNo ESCAPE '~')
              AND (@Sku IS NULL OR Sku LIKE @Sku ESCAPE '~')
              AND (@Keyword IS NULL OR SourceNo LIKE @Keyword ESCAPE '~' OR Sku LIKE @Keyword ESCAPE '~'
                OR LinkSku LIKE @Keyword ESCAPE '~' OR CustomerSku LIKE @Keyword ESCAPE '~'
                OR Description LIKE @Keyword ESCAPE '~' OR Color LIKE @Keyword ESCAPE '~');
            SELECT COUNT(1) FROM #SourceDelivery;
            SELECT * FROM #SourceDelivery
            ORDER BY SourceNo,SourceType,ISNULL(SourceLineNumber,2147483647),GrossDemandId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #SourceDelivery;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, Parameters(
            request.SourceType,
            request.CustomerId,
            request.SourceNo,
            request.Sku,
            request.Keyword,
            request.PageIndex,
            request.PageSize), cancellationToken));
        return new PagedResponseBase<SourceDemandDeliveryItemDto>
        {
            TotalCount = await reader.ReadSingleAsync<int>(),
            Items = (await reader.ReadAsync<SourceDemandDeliveryItemDto>()).ToList(),
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <inheritdoc/>
    public Task<SourceDemandDeliveryItemDto?> GetAsync(Guid grossDemandId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SourceDemandDeliveryItemDto>(Command(
            Projection(false) + "SELECT * FROM SourceDelivery WHERE GrossDemandId=@GrossDemandId;",
            new
            {
                GrossDemandId = grossDemandId,
                Posted = PackingListPostingStatusEnum.Posted,
                Active = GrossDemandLifecycleStatusEnum.Active
            }, cancellationToken));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default)
    {
        if (context.Session.Transaction is null)
            throw new InvalidOperationException("来源交付额度复核必须加入调用方业务事务。");
        var ids = grossDemandIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return Array.Empty<SourceDemandDeliveryItemDto>();
        var rows = await context.Session.Connection.QueryAsync<SourceDemandDeliveryItemDto>(Command(
            Projection(true) + "SELECT * FROM SourceDelivery WHERE GrossDemandId IN @Ids ORDER BY GrossDemandId;",
            new
            {
                Ids = ids,
                Posted = PackingListPostingStatusEnum.Posted,
                Active = GrossDemandLifecycleStatusEnum.Active
            }, cancellationToken));
        return rows.ToList();
    }

    /// <summary>构造唯一交付投影；写入校验时锁定来源和参与净额计算的头行。</summary>
    private static string Projection(bool forUpdate)
    {
        var demandHint = forUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var lineHint = forUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var headHint = forUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var deliveredScope = forUpdate ? " AND l.GrossDemandId IN @Ids" : string.Empty;
        var demandScope = forUpdate ? " AND g.Uid IN @Ids" : string.Empty;
        return $$"""
            ;WITH Delivered AS (
                SELECT l.GrossDemandId,SUM(l.Qty) AS DeliveredQty
                FROM dbo.PackingListLine l{{lineHint}}
                INNER JOIN dbo.PackingListHead h{{headHint}} ON h.Uid=l.PackingListHeadId
                WHERE l.IsDeleted=0 AND h.IsDeleted=0 AND h.PostingStatus=@Posted{{deliveredScope}}
                GROUP BY l.GrossDemandId
            ), SourceDelivery AS (
                SELECT g.Uid AS GrossDemandId,g.Version AS DemandVersion,g.SourceType,g.SourceUid,g.SourceNo,
                       g.SourceLineId,g.SourceLineNumber,g.SourceVersion,
                       CASE WHEN c.Uid IS NOT NULL THEN g.CustomerId END AS CustomerId,c.CustomerCode,
                       c.CustomerName,g.ProductId,g.IsNonStandard,g.SkuSnapshot AS Sku,
                       g.LinkSkuSnapshot AS LinkSku,g.CustomerSkuSnapshot AS CustomerSku,
                       g.DescriptionSnapshot AS Description,g.ColorSnapshot AS Color,
                       g.SpecificationSnapshot AS Specification,g.ImagePathSnapshot AS ImagePath,
                       g.UnitSnapshot AS Unit,g.DemandQty,COALESCE(d.DeliveredQty,0) AS DeliveredQty,
                       g.DemandQty-COALESCE(d.DeliveredQty,0) AS RemainingQty,g.LifecycleStatus,
                       CONVERT(bit,CASE WHEN g.LifecycleStatus=@Active THEN 1 ELSE 0 END) AS IsActive,
                       g.RequiredDate
                FROM dbo.GrossDemand g{{demandHint}}
                LEFT JOIN dbo.Customer c ON c.Uid=g.CustomerId AND c.IsDeleted=0
                LEFT JOIN dbo.Product p ON p.Uid=g.ProductId AND p.IsDeleted=0
                LEFT JOIN Delivered d ON d.GrossDemandId=g.Uid
                WHERE g.IsDeleted=0
                  AND ((g.IsNonStandard=1 AND g.ProductId IS NULL)
                    OR (g.IsNonStandard=0 AND g.ProductId IS NOT NULL AND p.Uid IS NOT NULL)){{demandScope}}
            )
            """;
    }

    /// <summary>标准化查询参数并按 SQL Server LIKE ESCAPE 规则转义用户文本。</summary>
    private static object Parameters(string? sourceType, Guid? customerId, string? sourceNo,
        string? sku, string? keyword, int pageIndex, int pageSize) => new
    {
        SourceType = Clean(sourceType),
        CustomerId = customerId,
        SourceNo = Like(sourceNo),
        Sku = Like(sku),
        Keyword = Like(keyword),
        Skip = (long)(pageIndex - 1) * pageSize,
        Take = pageSize,
        Posted = PackingListPostingStatusEnum.Posted,
        Active = GrossDemandLifecycleStatusEnum.Active
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal) + "%";
}
