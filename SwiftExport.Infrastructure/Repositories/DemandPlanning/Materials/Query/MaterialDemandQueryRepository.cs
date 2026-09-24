using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Contexts;
using System.Data;



using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text.Json;



namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;

/// <summary>读取物料需求工作台和来源导航。</summary>
public sealed class MaterialDemandQueryRepository(RepositoryBaseContext context) : IMaterialDemandQueryRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;

    /// <summary>
    /// 查询物料需求工作台，只读取物料、来源、产品与有效采购关联事实。
    /// </summary>
    /// <remarks>
    /// 本查询只读取物料需求事实。推荐采购方读取物料自身字段；客供件建议送达方仅沿同一有效拆解树目标派生，
    /// 不能替代采购域保存的实际采购方和送达方。
    /// </remarks>
    public async Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
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
    gd.[CustomerId],customer.[CustomerCode],customer.[CustomerName],
    gd.[SourceType],gd.[SourceUid],gd.[SourceNo],gd.[SourceLineNumber],gd.[SourceLineId],nd.[RootNetDemandId],
    root.[SkuSnapshot] AS [RootSku],CAST(CASE WHEN nd.[DeliveryMode]=1 THEN 1 ELSE 0 END AS bit) AS [IsCustomerSupplied],nd.[DeliveryTargetNetDemandId],
    nd.[ProductId],CAST(CASE WHEN nd.[ProductId] IS NULL THEN 1 ELSE 0 END AS bit) AS [IsNonStandard],
    nd.[SkuSnapshot] AS [Sku],COALESCE(p.[LinkSku],gd.[LinkSkuSnapshot]) AS [LinkSku],
    nd.[CustomerSkuSnapshot] AS [CustomerSku],nd.[DescriptionSnapshot] AS [Description],
    nd.[ColorSnapshot] AS [Color],nd.[SpecificationSnapshot] AS [Specification],
    CASE WHEN nd.[DeliveryMode]=1
         THEN COALESCE(p.[MainImage],N'')
         ELSE COALESCE(NULLIF(LTRIM(RTRIM(gd.[ImagePathSnapshot])),N''),p.[MainImage],N'')
    END AS [ImagePath],nd.[MaterialTypeSnapshot] AS [MaterialType],
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
LEFT JOIN [dbo].[Customer] customer ON customer.[Uid]=gd.[CustomerId] AND ISNULL(customer.[IsDeleted],0)=0
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
       [CustomerId],[CustomerCode],[CustomerName],
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
        var items = (await multi.ReadAsync<MaterialDemandManagerItemDto>()).ToList();
        return new PagedResponseBase<MaterialDemandManagerItemDto>
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

    private static List<string> CreateMaterialWorkspaceWhere(
        MaterialDemandManagerSearchRequest request,
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

    /// <summary>追加包含开始日、包含结束日的日期范围参数。</summary>
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

    /// <summary>按非空且去重的值集合追加参数化集合筛选。</summary>
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
