using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed partial class NetDemandRepository
{
    /// <summary>
    /// 查询物料需求工作台，只读取物料、来源、产品与有效采购关联事实。
    /// </summary>
    /// <remarks>
    /// 本查询刻意不读取 NetDemandAllocation。推荐采购方读取物料自身字段；客供件建议送达方仅沿同一有效拆解树目标派生，
    /// 不能替代采购域保存的实际采购方和送达方。
    /// </remarks>
    public async Task<PagedResponseBase<MaterialDemandWorkspaceItemDto>> SearchMaterialWorkspaceAsync(
        MaterialDemandWorkspaceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var paged = !request.SourceUid.HasValue;
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize,
            PurchaseTargetType = "PO"
        });
        var where = CreateMaterialWorkspaceWhere(request, parameters);
        var pageClause = paged ? "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY" : string.Empty;
        var sql = $@"
IF OBJECT_ID(N'tempdb..#MaterialWorkspace',N'U') IS NOT NULL DROP TABLE #MaterialWorkspace;

SELECT
    nd.[Uid],nd.[Version],nd.[LifecycleStatus],nd.[GrossDemandId],nd.[DecompositionRevision],nd.[LineNumber],
    gd.[SourceType],gd.[SourceUid],gd.[SourceNo],gd.[SourceLineNumber],gd.[SourceLineId],nd.[RootNetDemandId],
    root.[SkuSnapshot] AS [RootSku],CAST(CASE WHEN nd.[DeliveryMode]=1 THEN 1 ELSE 0 END AS bit) AS [IsCustomerSupplied],nd.[DeliveryTargetNetDemandId],
    nd.[ProductId],CAST(CASE WHEN nd.[ProductId] IS NULL THEN 1 ELSE 0 END AS bit) AS [IsNonStandard],
    nd.[SkuSnapshot] AS [Sku],COALESCE(p.[LinkSku],gd.[LinkSkuSnapshot]) AS [LinkSku],
    nd.[CustomerSkuSnapshot] AS [CustomerSku],nd.[DescriptionSnapshot] AS [Description],
    nd.[ColorSnapshot] AS [Color],nd.[SpecificationSnapshot] AS [Specification],
    COALESCE(gd.[ImagePathSnapshot],p.[MainImage]) AS [ImagePath],nd.[MaterialTypeSnapshot] AS [MaterialType],
    COALESCE(NULLIF(LTRIM(RTRIM(p.[Unit])),N''),N'PCS') AS [Unit],nd.[SupplyTypeSnapshot] AS [SupplyType],
    nd.[SuggestedPurchaseSupplierId],suggestedSupplier.[SupplierCode] AS [SuggestedPurchaseSupplierCode],
    suggestedSupplier.[SupplierName] AS [SuggestedPurchaseSupplierName],
    CASE WHEN nd.[DeliveryMode]=1 THEN deliveryTarget.[SuggestedPurchaseSupplierId] END AS [SuggestedDeliverySupplierId],
    CASE WHEN nd.[DeliveryMode]=1 THEN deliverySupplier.[SupplierCode] END AS [SuggestedDeliverySupplierCode],
    CASE WHEN nd.[DeliveryMode]=1 THEN deliverySupplier.[SupplierName] END AS [SuggestedDeliverySupplierName],
    CASE WHEN nd.[DeliveryMode]<>1 THEN NULL
         WHEN deliveryTarget.[Uid] IS NULL THEN N'送达目标关系无效'
         WHEN deliveryTarget.[SuggestedPurchaseSupplierId] IS NULL THEN N'父件尚未设置推荐采购供应商'
         WHEN deliverySupplier.[Uid] IS NULL THEN N'父件推荐采购供应商已失效'
         ELSE NULL END AS [SuggestedDeliverySupplierHint],
    nd.[GrossQty],nd.[NetQty],nd.[RequiredDate] AS [DemandRequiredDate],
    COALESCE(purchaseFacts.[AssociatedPurchaseQty],0) AS [AssociatedPurchaseQty],
    purchaseNumbers.[PurchaseOrderNos]
INTO #MaterialWorkspace
FROM [dbo].[NetDemand] nd
INNER JOIN [dbo].[GrossDemand] gd ON gd.[Uid]=nd.[GrossDemandId]
LEFT JOIN [dbo].[NetDemand] root ON root.[Uid]=nd.[RootNetDemandId]
LEFT JOIN [dbo].[Product] p ON p.[Uid]=nd.[ProductId] AND ISNULL(p.[IsDeleted],0)=0
LEFT JOIN [dbo].[Supplier] suggestedSupplier
  ON suggestedSupplier.[Uid]=nd.[SuggestedPurchaseSupplierId] AND ISNULL(suggestedSupplier.[IsDeleted],0)=0
LEFT JOIN [dbo].[NetDemand] deliveryTarget
  ON deliveryTarget.[Uid]=nd.[DeliveryTargetNetDemandId]
 AND deliveryTarget.[Uid]=nd.[ParentNetDemandId]
 AND deliveryTarget.[GrossDemandId]=nd.[GrossDemandId]
 AND deliveryTarget.[RootNetDemandId]=nd.[RootNetDemandId]
 AND deliveryTarget.[DecompositionRevision]=nd.[DecompositionRevision]
 AND deliveryTarget.[LifecycleStatus]=0 AND deliveryTarget.[IsDeleted]=0
LEFT JOIN [dbo].[Supplier] deliverySupplier
  ON deliverySupplier.[Uid]=deliveryTarget.[SuggestedPurchaseSupplierId] AND ISNULL(deliverySupplier.[IsDeleted],0)=0
OUTER APPLY
(
    SELECT SUM(link.[AllocatedQty]-link.[ReleasedQty]) AS [AssociatedPurchaseQty]
    FROM [dbo].[DemandExecutionLink] link
    INNER JOIN [dbo].[PurchaseOrderHead] purchaseHead
      ON purchaseHead.[Uid]=link.[TargetHeadId]
     AND purchaseHead.[LifecycleStatus]=0 AND ISNULL(purchaseHead.[IsDeleted],0)=0
    INNER JOIN [dbo].[PurchaseOrderLine] purchaseLine
      ON purchaseLine.[Uid]=link.[TargetLineId]
     AND purchaseLine.[PurchaseOrderId]=purchaseHead.[Uid] AND ISNULL(purchaseLine.[IsDeleted],0)=0
    WHERE link.[NetDemandId]=nd.[Uid]
      AND link.[TargetType]=@PurchaseTargetType AND link.[LinkStatus]=0 AND link.[IsDeleted]=0
) purchaseFacts
OUTER APPLY
(
    SELECT STRING_AGG(numbers.[OrderNo],N',') AS [PurchaseOrderNos]
    FROM
    (
        SELECT DISTINCT purchaseHead.[OrderNo]
        FROM [dbo].[DemandExecutionLink] link
        INNER JOIN [dbo].[PurchaseOrderHead] purchaseHead
          ON purchaseHead.[Uid]=link.[TargetHeadId]
         AND purchaseHead.[LifecycleStatus]=0 AND ISNULL(purchaseHead.[IsDeleted],0)=0
        INNER JOIN [dbo].[PurchaseOrderLine] purchaseLine
          ON purchaseLine.[Uid]=link.[TargetLineId]
         AND purchaseLine.[PurchaseOrderId]=purchaseHead.[Uid] AND ISNULL(purchaseLine.[IsDeleted],0)=0
        WHERE link.[NetDemandId]=nd.[Uid]
          AND link.[TargetType]=@PurchaseTargetType AND link.[LinkStatus]=0 AND link.[IsDeleted]=0
    ) numbers
) purchaseNumbers
WHERE {string.Join(" AND ", where)};

SELECT COUNT(1) FROM #MaterialWorkspace;
SELECT [Uid],[Version],[LifecycleStatus],[GrossDemandId],[DecompositionRevision],[LineNumber],
       [SourceType],[SourceUid],[SourceNo],[SourceLineNumber],[SourceLineId],[RootNetDemandId],[RootSku],
       [IsCustomerSupplied],[DeliveryTargetNetDemandId],[ProductId],[IsNonStandard],[Sku],[LinkSku],[CustomerSku],[Description],
       [Color],[Specification],[ImagePath],[MaterialType],[Unit],[SupplyType],
       [SuggestedPurchaseSupplierId],[SuggestedPurchaseSupplierCode],[SuggestedPurchaseSupplierName],
       [SuggestedDeliverySupplierId],[SuggestedDeliverySupplierCode],[SuggestedDeliverySupplierName],
       [SuggestedDeliverySupplierHint],[GrossQty],[NetQty],[DemandRequiredDate],
       [AssociatedPurchaseQty],[PurchaseOrderNos]
FROM #MaterialWorkspace
ORDER BY [SourceNo],[SourceLineNumber],[LineNumber],[Uid]
{pageClause};
DROP TABLE #MaterialWorkspace;";

        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, Transaction, cancellationToken: cancellationToken));
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = (await multi.ReadAsync<MaterialDemandWorkspaceItemDto>()).ToList();
        return new PagedResponseBase<MaterialDemandWorkspaceItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = paged ? pageIndex : 1,
            PageSize = paged ? pageSize : Math.Max(1, totalCount)
        };
    }

    /// <summary>按来源类型汇总活动物料需求，不读取计划状态或分配表。</summary>
    public async Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new DynamicParameters();
        var where = new List<string>
        {
            "nd.[LifecycleStatus]=0",
            "nd.[IsDeleted]=0",
            "gd.[LifecycleStatus]=0",
            "gd.[IsDeleted]=0"
        };
        AddListFilter(request.SourceTypes, "gd.[SourceType]", "SourceTypes", where, parameters);
        var sql = $@"
SELECT gd.[SourceUid],MAX(gd.[SourceType]) AS [SourceType],MAX(gd.[SourceNo]) AS [SourceNo],COUNT(1) AS [DemandCount]
FROM [dbo].[NetDemand] nd
INNER JOIN [dbo].[GrossDemand] gd ON gd.[Uid]=nd.[GrossDemandId]
WHERE {string.Join(" AND ", where)}
GROUP BY gd.[SourceUid]
ORDER BY MAX(gd.[SourceType]),MAX(gd.[SourceNo]),gd.[SourceUid];";
        var groups = await Connection.QueryAsync<NetDemandSourceGroupDto>(new CommandDefinition(
            sql, parameters, Transaction, cancellationToken: cancellationToken));
        return groups.ToList();
    }

    /// <summary>按采购候选统一资格汇总全部来源分组，不读取 Allocation。</summary>
    public async Task<IReadOnlyList<PurchaseCandidateSourceGroupDto>> GetPurchaseCandidateSourceGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var sql = $@"
SELECT gd.[SourceType],gd.[SourceUid],MAX(gd.[SourceNo]) AS [SourceNo],COUNT(1) AS [CandidateCount]
FROM [dbo].[NetDemand] nd
INNER JOIN [dbo].[GrossDemand] gd ON gd.[Uid]=nd.[GrossDemandId]
{PurchaseCandidatePurchaseFactsSql}
WHERE {PurchaseCandidateEligibilitySql}
GROUP BY gd.[SourceType],gd.[SourceUid]
ORDER BY gd.[SourceType],MAX(gd.[SourceNo]),gd.[SourceUid];";
        var rows = await Connection.QueryAsync<PurchaseCandidateSourceGroupDto>(new CommandDefinition(
            sql,
            new { PurchaseTargetType = "PO", PurchaseSupplyType = SupplyTypeEnums.采购 },
            Transaction,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>
    /// 按稳定来源身份或全部来源完整查询待采购物料；图片和产品型号只取当前物料产品，不猜用来源父产品快照。
    /// </summary>
    public async Task<IReadOnlyList<MaterialDemandWorkspaceItemDto>> GetPurchaseCandidatesAsync(
        PurchaseCandidateQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceFilter = request.SourceUid.HasValue
            ? "AND gd.[SourceType]=@SourceType AND gd.[SourceUid]=@SourceUid"
            : string.Empty;
        var sql = $@"
SELECT nd.[Uid],nd.[Version],nd.[LifecycleStatus],nd.[GrossDemandId],nd.[DecompositionRevision],nd.[LineNumber],
       gd.[SourceType],gd.[SourceUid],gd.[SourceNo],gd.[SourceLineNumber],gd.[SourceLineId],nd.[RootNetDemandId],
       root.[SkuSnapshot] AS [RootSku],CAST(CASE WHEN nd.[DeliveryMode]=1 THEN 1 ELSE 0 END AS bit) AS [IsCustomerSupplied],
       nd.[DeliveryTargetNetDemandId],nd.[ProductId],CAST(CASE WHEN nd.[ProductId] IS NULL THEN 1 ELSE 0 END AS bit) AS [IsNonStandard],
       nd.[SkuSnapshot] AS [Sku],COALESCE(p.[LinkSku],N'') AS [LinkSku],nd.[CustomerSkuSnapshot] AS [CustomerSku],
       nd.[DescriptionSnapshot] AS [Description],nd.[ColorSnapshot] AS [Color],nd.[SpecificationSnapshot] AS [Specification],
       COALESCE(p.[MainImage],N'') AS [ImagePath],nd.[MaterialTypeSnapshot] AS [MaterialType],
       COALESCE(NULLIF(LTRIM(RTRIM(p.[Unit])),N''),N'PCS') AS [Unit],nd.[SupplyTypeSnapshot] AS [SupplyType],
       nd.[SuggestedPurchaseSupplierId],suggestedSupplier.[SupplierCode] AS [SuggestedPurchaseSupplierCode],
       suggestedSupplier.[SupplierName] AS [SuggestedPurchaseSupplierName],
       CASE WHEN nd.[DeliveryMode]=1 THEN deliveryTarget.[SuggestedPurchaseSupplierId] END AS [SuggestedDeliverySupplierId],
       CASE WHEN nd.[DeliveryMode]=1 THEN deliverySupplier.[SupplierCode] END AS [SuggestedDeliverySupplierCode],
       CASE WHEN nd.[DeliveryMode]=1 THEN deliverySupplier.[SupplierName] END AS [SuggestedDeliverySupplierName],
       CASE WHEN nd.[DeliveryMode]<>1 THEN NULL
            WHEN deliveryTarget.[Uid] IS NULL THEN N'送达目标关系无效'
            WHEN deliveryTarget.[SuggestedPurchaseSupplierId] IS NULL THEN N'父件尚未设置推荐采购供应商'
            WHEN deliverySupplier.[Uid] IS NULL THEN N'父件推荐采购供应商已失效'
            ELSE NULL END AS [SuggestedDeliverySupplierHint],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate] AS [DemandRequiredDate],
       COALESCE(purchaseFacts.[AssociatedPurchaseQty],0) AS [AssociatedPurchaseQty],purchaseNumbers.[PurchaseOrderNos]
FROM [dbo].[NetDemand] nd
INNER JOIN [dbo].[GrossDemand] gd ON gd.[Uid]=nd.[GrossDemandId]
LEFT JOIN [dbo].[NetDemand] root ON root.[Uid]=nd.[RootNetDemandId]
LEFT JOIN [dbo].[Product] p ON p.[Uid]=nd.[ProductId] AND ISNULL(p.[IsDeleted],0)=0
LEFT JOIN [dbo].[Supplier] suggestedSupplier
  ON suggestedSupplier.[Uid]=nd.[SuggestedPurchaseSupplierId] AND ISNULL(suggestedSupplier.[IsDeleted],0)=0
LEFT JOIN [dbo].[NetDemand] deliveryTarget
  ON deliveryTarget.[Uid]=nd.[DeliveryTargetNetDemandId]
 AND deliveryTarget.[Uid]=nd.[ParentNetDemandId]
 AND deliveryTarget.[GrossDemandId]=nd.[GrossDemandId]
 AND deliveryTarget.[RootNetDemandId]=nd.[RootNetDemandId]
 AND deliveryTarget.[DecompositionRevision]=nd.[DecompositionRevision]
 AND deliveryTarget.[LifecycleStatus]=0 AND deliveryTarget.[IsDeleted]=0
LEFT JOIN [dbo].[Supplier] deliverySupplier
  ON deliverySupplier.[Uid]=deliveryTarget.[SuggestedPurchaseSupplierId] AND ISNULL(deliverySupplier.[IsDeleted],0)=0
{PurchaseCandidatePurchaseFactsSql}
OUTER APPLY
(
    SELECT STRING_AGG(numbers.[OrderNo],N',') AS [PurchaseOrderNos]
    FROM
    (
        SELECT DISTINCT purchaseHead.[OrderNo]
        FROM [dbo].[DemandExecutionLink] link
        INNER JOIN [dbo].[PurchaseOrderHead] purchaseHead
          ON purchaseHead.[Uid]=link.[TargetHeadId]
         AND purchaseHead.[LifecycleStatus]=0 AND ISNULL(purchaseHead.[IsDeleted],0)=0
        INNER JOIN [dbo].[PurchaseOrderLine] purchaseLine
          ON purchaseLine.[Uid]=link.[TargetLineId]
         AND purchaseLine.[PurchaseOrderId]=purchaseHead.[Uid] AND ISNULL(purchaseLine.[IsDeleted],0)=0
        WHERE link.[NetDemandId]=nd.[Uid]
          AND link.[TargetType]=@PurchaseTargetType AND link.[LinkStatus]=0 AND ISNULL(link.[IsDeleted],0)=0
    ) numbers
) purchaseNumbers
WHERE {PurchaseCandidateEligibilitySql}
{sourceFilter}
ORDER BY gd.[SourceType],gd.[SourceNo],gd.[SourceUid],gd.[SourceLineNumber],nd.[LineNumber],nd.[Uid];";
        var rows = await Connection.QueryAsync<MaterialDemandWorkspaceItemDto>(new CommandDefinition(
            sql,
            new
            {
                PurchaseTargetType = "PO",
                PurchaseSupplyType = SupplyTypeEnums.采购,
                request.SourceType,
                request.SourceUid
            },
            Transaction,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private const string PurchaseCandidatePurchaseFactsSql = @"
OUTER APPLY
(
    SELECT SUM(link.[AllocatedQty]-link.[ReleasedQty]) AS [AssociatedPurchaseQty]
    FROM [dbo].[DemandExecutionLink] link
    INNER JOIN [dbo].[PurchaseOrderHead] purchaseHead
      ON purchaseHead.[Uid]=link.[TargetHeadId]
     AND purchaseHead.[LifecycleStatus]=0 AND ISNULL(purchaseHead.[IsDeleted],0)=0
    INNER JOIN [dbo].[PurchaseOrderLine] purchaseLine
      ON purchaseLine.[Uid]=link.[TargetLineId]
     AND purchaseLine.[PurchaseOrderId]=purchaseHead.[Uid] AND ISNULL(purchaseLine.[IsDeleted],0)=0
    WHERE link.[NetDemandId]=nd.[Uid]
      AND link.[TargetType]=@PurchaseTargetType AND link.[LinkStatus]=0 AND ISNULL(link.[IsDeleted],0)=0
) purchaseFacts";

    private const string PurchaseCandidateEligibilitySql = @"
nd.[LifecycleStatus]=0 AND ISNULL(nd.[IsDeleted],0)=0
AND gd.[LifecycleStatus]=0 AND ISNULL(gd.[IsDeleted],0)=0
AND nd.[SupplyTypeSnapshot]=@PurchaseSupplyType
AND nd.[NetQty]>COALESCE(purchaseFacts.[AssociatedPurchaseQty],0)";

    /// <summary>构造物料事实筛选；禁止在此加入 Allocation 派生条件。</summary>
    private static List<string> CreateMaterialWorkspaceWhere(
        MaterialDemandWorkspaceSearchRequest request,
        DynamicParameters parameters)
    {
        var where = new List<string>
        {
            "nd.[LifecycleStatus]=0",
            "nd.[IsDeleted]=0",
            "gd.[LifecycleStatus]=0",
            "gd.[IsDeleted]=0"
        };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.Add(@"(gd.[SourceNo] LIKE @Keyword ESCAPE '~'
                         OR nd.[SkuSnapshot] LIKE @Keyword ESCAPE '~'
                         OR nd.[CustomerSkuSnapshot] LIKE @Keyword ESCAPE '~'
                         OR nd.[DescriptionSnapshot] LIKE @Keyword ESCAPE '~'
                         OR p.[LinkSku] LIKE @Keyword ESCAPE '~')");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        AddListFilter(request.NetDemandIds, "nd.[Uid]", "NetDemandIds", where, parameters);
        AddListFilter(request.GrossDemandIds, "nd.[GrossDemandId]", "GrossDemandIds", where, parameters);
        if (request.SourceUid.HasValue)
        {
            where.Add("gd.[SourceUid]=@SelectedSourceUid");
            parameters.Add("SelectedSourceUid", request.SourceUid.Value);
        }
        AddListFilter(request.SourceUids, "gd.[SourceUid]", "SourceUids", where, parameters);
        AddListFilter(request.SourceTypes, "gd.[SourceType]", "SourceTypes", where, parameters);
        AddListFilter(request.SourceNos, "gd.[SourceNo]", "SourceNos", where, parameters);
        AddListFilter(request.Skus, "nd.[SkuSnapshot]", "Skus", where, parameters);
        AddListFilter(request.CustomerSkus, "nd.[CustomerSkuSnapshot]", "CustomerSkus", where, parameters);
        AddDateRange(request.DemandRequiredDateFrom, request.DemandRequiredDateTo, "nd.[RequiredDate]",
            "DemandRequiredDate", where, parameters);
        if (request.IsCustomerSupplied.HasValue)
            where.Add(request.IsCustomerSupplied.Value ? "nd.[DeliveryMode]=1" : "nd.[DeliveryMode]<>1");
        if (request.IsNonStandard.HasValue)
            where.Add(request.IsNonStandard.Value ? "nd.[ProductId] IS NULL" : "nd.[ProductId] IS NOT NULL");
        return where;
    }
}
