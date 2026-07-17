using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed partial class NetDemandRepository
{
    public Task<PagedResponseBase<NetDemandListItemDto>> SearchWorkspaceAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default)
        => QueryWorkspaceAsync(request ?? new NetDemandSearchRequest(), null, true, cancellationToken);

    public async Task<NetDemandListItemDto?> GetWorkspaceItemAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        var response = await QueryWorkspaceAsync(new NetDemandSearchRequest
        {
            NetDemandIds = [uid],
            PageIndex = 1,
            PageSize = 2
        }, null, true, cancellationToken);
        return response.Items.SingleOrDefault();
    }

    public async Task<IReadOnlyList<NetDemandListItemDto>> GetWorkspaceFamilyAsync(
        Guid rootNetDemandId,
        CancellationToken cancellationToken = default)
    {
        var response = await QueryWorkspaceAsync(new NetDemandSearchRequest(), rootNetDemandId, false, cancellationToken);
        return response.Items;
    }

    public async Task<IReadOnlyList<NetDemandListItemDto>> GetWorkspaceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        var response = await QueryWorkspaceAsync(new NetDemandSearchRequest
        {
            SourceTypes = [sourceType],
            SourceUids = [sourceUid]
        }, null, false, cancellationToken);
        return response.Items;
    }

    public async Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId)
    {
        const string locatorSql = @"
SELECT [IndependentDemandId] AS [GrossDemandId],[RootNetDemandId]
FROM [dbo].[DependentDemand] WITH (NOLOCK)
WHERE [Uid]=@NetDemandId AND [LifecycleStatus]=@NetDemandActive AND [IsDeleted]=0;";
        var locator = await Connection.QuerySingleOrDefaultAsync<FamilyLocator>(
            locatorSql,
            new { NetDemandId = netDemandId, NetDemandActive = SwiftExport.Contracts.SysEnums.NetDemandLifecycleStatusEnum.Active },
            Transaction);
        if (locator is null) return [];

        const string grossLockSql = @"
SELECT [Uid]
FROM [dbo].[IndependentDemand] WITH (UPDLOCK,HOLDLOCK)
WHERE [Uid]=@GrossDemandId AND [LifecycleStatus]=@GrossDemandActive AND [IsDeleted]=0;";
        var lockedGrossDemandId = await Connection.QuerySingleOrDefaultAsync<Guid?>(
            grossLockSql,
            new
            {
                locator.GrossDemandId,
                GrossDemandActive = SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Active
            }, Transaction);
        if (!lockedGrossDemandId.HasValue) return [];
        var rows = await Connection.QueryAsync<NetDemand>(NetDemandSelect + @"
FROM [dbo].[DependentDemand] nd WITH (UPDLOCK,HOLDLOCK)
WHERE nd.[IndependentDemandId]=@GrossDemandId AND nd.[RootNetDemandId]=@RootNetDemandId
  AND nd.[LifecycleStatus]=@Active AND nd.[IsDeleted]=0
ORDER BY nd.[LineNumber],nd.[Uid];",
            new
            {
                locator.GrossDemandId,
                locator.RootNetDemandId,
                Active = SwiftExport.Contracts.SysEnums.NetDemandLifecycleStatusEnum.Active
            }, Transaction);
        return [.. rows];
    }

    private sealed class FamilyLocator
    {
        public Guid GrossDemandId { get; set; }
        public Guid RootNetDemandId { get; set; }
    }

    private async Task<PagedResponseBase<NetDemandListItemDto>> QueryWorkspaceAsync(
        NetDemandSearchRequest request,
        Guid? rootNetDemandId,
        bool paged,
        CancellationToken cancellationToken)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var parameters = new DynamicParameters(new
        {
            RootNetDemandId = rootNetDemandId,
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize,
            PurchaseTargetType = "PO"
        });
        var innerWhere = new List<string>
        {
            "nd.[LifecycleStatus]=0",
            "nd.[IsDeleted]=0",
            "gd.[LifecycleStatus]=0",
            "gd.[IsDeleted]=0"
        };
        if (rootNetDemandId.HasValue) innerWhere.Add("nd.[RootNetDemandId]=@RootNetDemandId");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            innerWhere.Add(@"(gd.[SourceNo] LIKE @Keyword ESCAPE '~'
                              OR nd.[Sku] LIKE @Keyword ESCAPE '~'
                              OR nd.[SourceSku] LIKE @Keyword ESCAPE '~'
                              OR nd.[Description] LIKE @Keyword ESCAPE '~'
                              OR p.[LinkSku] LIKE @Keyword ESCAPE '~')");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        AddListFilter(request.NetDemandIds, "nd.[Uid]", "NetDemandIds", innerWhere, parameters);
        AddListFilter(request.GrossDemandIds, "nd.[IndependentDemandId]", "GrossDemandIds", innerWhere, parameters);
        AddListFilter(request.SourceUids, "gd.[SourceUid]", "SourceUids", innerWhere, parameters);
        AddListFilter(request.SourceTypes, "gd.[SourceType]", "SourceTypes", innerWhere, parameters);
        AddListFilter(request.SourceNos, "gd.[SourceNo]", "SourceNos", innerWhere, parameters);
        AddListFilter(request.Skus, "nd.[Sku]", "Skus", innerWhere, parameters);
        AddListFilter(request.CustomerSkus, "nd.[SourceSku]", "CustomerSkus", innerWhere, parameters);
        AddDateRange(request.DemandRequiredDateFrom, request.DemandRequiredDateTo, "nd.[RequiredDate]",
            "DemandRequiredDate", innerWhere, parameters);
        AddDateRange(request.PlannedDeliveryDateFrom, request.PlannedDeliveryDateTo, "a.[PlannedDeliveryDate]",
            "PlannedDeliveryDate", innerWhere, parameters);
        AddListFilter(request.ExecutionTypes, "a.[ExecutionType]", "ExecutionTypes", innerWhere, parameters);
        AddListFilter(request.PurchaseSupplierIds, "a.[PurchaseSupplierId]", "PurchaseSupplierIds", innerWhere, parameters);
        AddListFilter(request.DeliveryTargetSupplierIds, "a.[DeliveryTargetSupplierId]", "DeliveryTargetSupplierIds", innerWhere, parameters);
        if (request.IsCustomerSupplied.HasValue)
        {
            innerWhere.Add(request.IsCustomerSupplied.Value ? "nd.[DeliveryMode]=1" : "nd.[DeliveryMode]<>1");
        }
        if (request.IsNonStandard.HasValue)
        {
            innerWhere.Add(request.IsNonStandard.Value ? "nd.[ProductId] IS NULL" : "nd.[ProductId] IS NOT NULL");
        }

        var outerWhere = new List<string>();
        AddListFilter(request.PlanStatuses, "w.[PlanStatus]", "PlanStatuses", outerWhere, parameters);
        if (request.HasRemainingQty.HasValue)
            outerWhere.Add(request.HasRemainingQty.Value ? "w.[RemainingQty]>0" : "w.[RemainingQty]<=0");

        var workspaceSql = $@"
IF OBJECT_ID(N'tempdb..#Workspace',N'U') IS NOT NULL DROP TABLE #Workspace;

SELECT
        nd.[Uid],nd.[Version],nd.[LifecycleStatus],nd.[IndependentDemandId] AS [GrossDemandId],
        nd.[DecompositionRevision],nd.[LineNumber],gd.[SourceType],gd.[SourceUid],gd.[SourceNo],
        gd.[SourceLineId],gd.[SourceLineNumber],nd.[ParentNetDemandId],nd.[RootNetDemandId],
        root.[Sku] AS [RootSku],CAST(CASE WHEN nd.[DeliveryMode]=1 THEN 1 ELSE 0 END AS bit) AS [IsCustomerSupplied],
        nd.[DeliveryMode],nd.[ProductId],CAST(CASE WHEN nd.[ProductId] IS NULL THEN 1 ELSE 0 END AS bit) AS [IsNonStandard],
        nd.[Sku],COALESCE(p.[LinkSku],gd.[LinkSkuSnapshot]) AS [LinkSku],nd.[SourceSku] AS [CustomerSku],
        nd.[Description],nd.[ColorName] AS [Color],nd.[Features] AS [Specification],
        COALESCE(gd.[ImagePathSnapshot],p.[MainImage]) AS [ImagePath],nd.[MaterialTypeSnapshot] AS [MaterialType],
        nd.[SupplyType],nd.[GrossQty],nd.[NetQty],nd.[RequiredDate] AS [DemandRequiredDate],
        a.[Uid] AS [AllocationId],a.[Version] AS [AllocationVersion],a.[PlanRevision],a.[Sequence] AS [AllocationSequence],
        a.[LifecycleStatus] AS [AllocationLifecycleStatus],a.[ExecutionType],a.[AssignmentSource],a.[PlannedQty],
        COALESCE(linkFacts.[AllocatedQty],0) AS [AllocatedQty],
        COALESCE(a.[PlannedQty],0)-COALESCE(linkFacts.[AllocatedQty],0) AS [RemainingQty],a.[PlannedDeliveryDate],
        a.[PurchaseSupplierId],a.[SupplierCodeSnapshot] AS [PurchaseSupplierCode],
        a.[SupplierNameSnapshot] AS [PurchaseSupplierName],currentSupplier.[SupplierName] AS [CurrentPurchaseSupplierName],
        a.[DeliveryTargetAllocationId],a.[DeliveryTargetSupplierId],
        a.[DeliverySupplierCodeSnapshot] AS [DeliveryTargetSupplierCode],
        a.[DeliverySupplierNameSnapshot] AS [DeliveryTargetSupplierName],
        p.[DefaultSupplierId],p.[DefaultSupplierCode],defaultSupplier.[SupplierName] AS [DefaultSupplierName],
        CAST(CASE WHEN ISNULL(CONVERT(nvarchar(36),p.[DefaultSupplierId]),N'')
                            <>ISNULL(CONVERT(nvarchar(36),a.[PurchaseSupplierId]),N'') THEN 1 ELSE 0 END AS bit) AS [DefaultSupplierChanged],
        CAST(CASE
            WHEN COALESCE(allocationFacts.[ActiveCount],0)<>1 OR a.[Uid] IS NULL OR a.[PlannedQty]<>nd.[NetQty]
                 OR COALESCE(linkFacts.[AllocatedQty],0)>COALESCE(a.[PlannedQty],0) THEN 8
            WHEN a.[ExecutionType]=0 AND a.[PurchaseSupplierId] IS NOT NULL AND currentSupplier.[Uid] IS NULL THEN 8
            WHEN nd.[DeliveryMode]=1 AND
                 ((a.[DeliveryTargetAllocationId] IS NOT NULL AND deliveryTargetAllocation.[Uid] IS NULL)
                  OR (deliveryTargetAllocation.[Uid] IS NOT NULL
                      AND deliveryTargetAllocation.[NetDemandId]<>nd.[DeliveryTargetNetDemandId])
                  OR (a.[DeliveryTargetSupplierId] IS NOT NULL
                      AND deliverySupplier.[Uid] IS NULL)
                  OR (a.[DeliveryTargetSupplierId] IS NOT NULL
                      AND ISNULL(CONVERT(nvarchar(36),deliveryTargetAllocation.[PurchaseSupplierId]),N'')
                          <>CONVERT(nvarchar(36),a.[DeliveryTargetSupplierId]))) THEN 8
            WHEN a.[ExecutionType]=0 AND a.[PurchaseSupplierId] IS NULL THEN 0
            WHEN nd.[DeliveryMode]=1 AND (a.[DeliveryTargetAllocationId] IS NULL OR a.[DeliveryTargetSupplierId] IS NULL) THEN 1
            WHEN a.[ExecutionType]<>0 THEN 9
            WHEN COALESCE(linkFacts.[HasConfirmedPurchase],0)=1 THEN 6
            WHEN COALESCE(linkFacts.[HasDraftPurchase],0)=1 THEN 5
            WHEN COALESCE(linkFacts.[AllocatedQty],0)>=a.[PlannedQty] THEN 4
            WHEN COALESCE(linkFacts.[AllocatedQty],0)>0 THEN 3
            ELSE 2 END AS int) AS [PlanStatus],
        purchaseNos.[PurchaseOrderNos],
        CASE WHEN COALESCE(linkFacts.[LinkCount],0)>0
             THEN CONCAT(N'已有活动执行关联：',COALESCE(purchaseNos.[PurchaseOrderNos],N'非采购执行单')) END AS [BlockerSummary],
        CAST(CASE WHEN a.[Uid] IS NOT NULL AND allocationFacts.[ActiveCount]=1
                       AND a.[PlannedQty]=nd.[NetQty] AND COALESCE(linkFacts.[LinkCount],0)=0
                       AND (nd.[DeliveryMode]<>1 OR a.[DeliveryTargetAllocationId] IS NULL
                            OR (deliveryTargetAllocation.[Uid] IS NOT NULL
                                AND deliveryTargetAllocation.[NetDemandId]=nd.[DeliveryTargetNetDemandId])) THEN 1 ELSE 0 END AS bit) AS [CanReplacePlan],
        CAST(CASE WHEN a.[ExecutionType]=0 AND p.[DefaultSupplierId] IS NOT NULL AND defaultSupplier.[Uid] IS NOT NULL
                       AND (p.[DefaultSupplierId]<>ISNULL(a.[PurchaseSupplierId],'00000000-0000-0000-0000-000000000000')
                            OR ISNULL(a.[SupplierCodeSnapshot],N'')<>ISNULL(defaultSupplier.[SupplierCode],N'')
                            OR ISNULL(a.[SupplierNameSnapshot],N'')<>ISNULL(defaultSupplier.[SupplierName],N''))
                       AND allocationFacts.[ActiveCount]=1 AND a.[PlannedQty]=nd.[NetQty]
                       AND COALESCE(linkFacts.[LinkCount],0)=0 THEN 1 ELSE 0 END AS bit) AS [CanApplyDefaultSupplier],
        CAST(CASE WHEN a.[Uid] IS NOT NULL AND a.[ExecutionType]=0 AND a.[PurchaseSupplierId] IS NOT NULL
                       AND currentSupplier.[Uid] IS NOT NULL
                       AND (nd.[DeliveryMode]<>1 OR (a.[DeliveryTargetSupplierId] IS NOT NULL
                            AND deliveryTargetAllocation.[Uid] IS NOT NULL
                            AND deliveryTargetAllocation.[NetDemandId]=nd.[DeliveryTargetNetDemandId]
                            AND deliveryTargetAllocation.[PurchaseSupplierId]=a.[DeliveryTargetSupplierId]
                            AND deliverySupplier.[Uid] IS NOT NULL))
                       AND COALESCE(a.[PlannedQty],0)>COALESCE(linkFacts.[AllocatedQty],0)
                       AND a.[PlannedQty]=nd.[NetQty] AND allocationFacts.[ActiveCount]=1 THEN 1 ELSE 0 END AS bit) AS [CanTransferPurchase],
        nd.[CreatedDate],nd.[CreatedBy],a.[UpdatedDate],a.[UpdatedBy]
    INTO #Workspace
    FROM [dbo].[DependentDemand] nd
    INNER JOIN [dbo].[IndependentDemand] gd ON gd.[Uid]=nd.[IndependentDemandId]
    LEFT JOIN [dbo].[DependentDemand] root ON root.[Uid]=nd.[RootNetDemandId]
    LEFT JOIN [dbo].[Product] p ON p.[Uid]=nd.[ProductId] AND ISNULL(p.[IsDeleted],0)=0
    OUTER APPLY
    (
        SELECT TOP(1) activeAllocation.*
        FROM [dbo].[NetDemandAllocation] activeAllocation
        WHERE activeAllocation.[NetDemandId]=nd.[Uid]
          AND activeAllocation.[LifecycleStatus]=0 AND activeAllocation.[IsDeleted]=0
        ORDER BY activeAllocation.[PlanRevision] DESC,activeAllocation.[Sequence],activeAllocation.[Uid]
    ) a
    OUTER APPLY
    (
        SELECT COUNT_BIG(1) AS [ActiveCount],SUM(activeAllocation.[PlannedQty]) AS [ActivePlannedQty]
        FROM [dbo].[NetDemandAllocation] activeAllocation
        WHERE activeAllocation.[NetDemandId]=nd.[Uid]
          AND activeAllocation.[LifecycleStatus]=0 AND activeAllocation.[IsDeleted]=0
    ) allocationFacts
    LEFT JOIN [dbo].[NetDemandAllocation] deliveryTargetAllocation
      ON deliveryTargetAllocation.[Uid]=a.[DeliveryTargetAllocationId]
     AND deliveryTargetAllocation.[LifecycleStatus]=0 AND deliveryTargetAllocation.[IsDeleted]=0
    LEFT JOIN [dbo].[Supplier] currentSupplier
      ON currentSupplier.[Uid]=a.[PurchaseSupplierId] AND ISNULL(currentSupplier.[IsDeleted],0)=0
    LEFT JOIN [dbo].[Supplier] defaultSupplier
      ON defaultSupplier.[Uid]=p.[DefaultSupplierId] AND ISNULL(defaultSupplier.[IsDeleted],0)=0
    LEFT JOIN [dbo].[Supplier] deliverySupplier
      ON deliverySupplier.[Uid]=a.[DeliveryTargetSupplierId] AND ISNULL(deliverySupplier.[IsDeleted],0)=0
    OUTER APPLY
    (
        SELECT SUM(l.[AllocatedQty]-l.[ReleasedQty]) AS [AllocatedQty],COUNT_BIG(1) AS [LinkCount],
               MAX(CASE WHEN l.[TargetType]=@PurchaseTargetType AND poh.[LifecycleStatus]=0 AND poh.[ConfirmationStatus]=1 THEN 1 ELSE 0 END) AS [HasConfirmedPurchase],
               MAX(CASE WHEN l.[TargetType]=@PurchaseTargetType AND poh.[LifecycleStatus]=0 AND poh.[ConfirmationStatus]=0 THEN 1 ELSE 0 END) AS [HasDraftPurchase]
        FROM [dbo].[DemandExecutionLink] l
        LEFT JOIN [dbo].[PurchaseOrderHead] poh
          ON poh.[Uid]=l.[TargetHeadId] AND l.[TargetType]=@PurchaseTargetType AND ISNULL(poh.[IsDeleted],0)=0
        WHERE l.[NetDemandAllocationId]=a.[Uid] AND l.[LinkStatus]=0 AND l.[IsDeleted]=0
    ) linkFacts
    OUTER APPLY
    (
        SELECT STRING_AGG(numbers.[TargetNo],N',') AS [PurchaseOrderNos]
        FROM
        (
            SELECT DISTINCT l.[TargetNo]
            FROM [dbo].[DemandExecutionLink] l
            WHERE l.[NetDemandAllocationId]=a.[Uid] AND l.[LinkStatus]=0 AND l.[IsDeleted]=0
              AND l.[TargetType]=@PurchaseTargetType
        ) numbers
    ) purchaseNos
    WHERE {string.Join(" AND ", innerWhere)};
";
        var outerClause = outerWhere.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", outerWhere);
        var pageClause = paged
            ? "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY"
            : string.Empty;
        var sql = workspaceSql + $@"
SELECT COUNT(1) FROM #Workspace w {outerClause};
SELECT w.* FROM #Workspace w {outerClause}
ORDER BY w.[SourceNo],w.[SourceLineNumber],w.[LineNumber],w.[Uid]
{pageClause};
DROP TABLE #Workspace;";
        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, Transaction, cancellationToken: cancellationToken));
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = (await multi.ReadAsync<NetDemandListItemDto>()).ToList();
        return new PagedResponseBase<NetDemandListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = paged ? pageIndex : 1,
            PageSize = paged ? pageSize : Math.Max(1, totalCount)
        };
    }

    private static void AddDateRange(
        DateTime? from,
        DateTime? to,
        string column,
        string parameterPrefix,
        ICollection<string> where,
        DynamicParameters parameters)
    {
        if (from.HasValue)
        {
            where.Add($"{column}>=@{parameterPrefix}From");
            parameters.Add(parameterPrefix + "From", from.Value);
        }
        if (to.HasValue)
        {
            where.Add($"{column}<@{parameterPrefix}ToExclusive");
            parameters.Add(parameterPrefix + "ToExclusive", to.Value.Date.AddDays(1));
        }
    }

    private static void AddListFilter<T>(
        IEnumerable<T>? values,
        string column,
        string parameterName,
        ICollection<string> where,
        DynamicParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"{column} IN @{parameterName}");
        parameters.Add(parameterName, array);
    }
}
