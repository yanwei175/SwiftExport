using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Contexts;
using System.Data;



using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text.Json;



namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;

/// <summary>读取待建立来源与采购候选，复用原候选资格 SQL。</summary>
public sealed class MaterialDemandSourceRepository(RepositoryBaseContext context) : IMaterialDemandSourceRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;

    /// <summary>
    /// 按来源稳定身份聚合全部待建立来源行；不从分页候选结果二次去重，也不依赖来源兼容状态。
    /// </summary>
    public async Task<IReadOnlyList<SourceDemandCandidateGroupDto>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[SourceType],d.[SourceUid],MAX(d.[SourceNo]) AS [SourceNo],COUNT(1) AS [CandidateCount]
FROM [dbo].[GrossDemand] d
WHERE d.[LifecycleStatus]=0 AND d.[IsDeleted]=0
  AND NOT EXISTS (
      SELECT 1
      FROM [dbo].[NetDemand] nd
      WHERE nd.[GrossDemandId]=d.[Uid] AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
GROUP BY d.[SourceType],d.[SourceUid]
ORDER BY MAX(d.[SourceNo]),d.[SourceType],d.[SourceUid];";
        var rows = await Connection.QueryAsync<SourceDemandCandidateGroupDto>(new CommandDefinition(
            sql, transaction: Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>
    /// 使用来源类型与来源 Uid 双重边界完整返回一张来源单的待建立行，避免同号来源被误合并。
    /// </summary>
    public async Task<IReadOnlyList<SourceDemandCandidateDto>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[Uid] AS [GrossDemandId],d.[Version] AS [SourceDemandVersion],d.[SourceVersion],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],
       d.[SkuSnapshot] AS [Sku],d.[LinkSkuSnapshot] AS [LinkSku],d.[CustomerSkuSnapshot] AS [CustomerSku],
       d.[DescriptionSnapshot] AS [Description],d.[ImagePathSnapshot] AS [ImagePath],d.[DemandQty],d.[RequiredDate]
FROM [dbo].[GrossDemand] d
WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid
  AND d.[LifecycleStatus]=0 AND d.[IsDeleted]=0
  AND NOT EXISTS (
      SELECT 1
      FROM [dbo].[NetDemand] nd
      WHERE nd.[GrossDemandId]=d.[Uid] AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
ORDER BY CASE WHEN d.[RequiredDate] IS NULL THEN 1 ELSE 0 END,d.[RequiredDate],d.[SourceLineNumber],d.[Uid];";
        var rows = await Connection.QueryAsync<SourceDemandCandidateDto>(new CommandDefinition(
            sql, new { SourceType = sourceType, SourceUid = sourceUid }, Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>按采购候选统一资格汇总全部来源分组，不读取 Allocation。</summary>
    public async Task<IReadOnlyList<PurchaseDraftCandidateSourceGroupDto>> GetPurchaseCandidateSourceGroupsAsync(
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
        var rows = await Connection.QueryAsync<PurchaseDraftCandidateSourceGroupDto>(new CommandDefinition(
            sql,
            new { PurchaseTargetType = "PO", PurchaseSupplyType = SupplyTypeEnums.采购 },
            Transaction,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>
    /// 按稳定来源身份或全部来源完整查询待采购物料；图片和产品型号只取当前物料产品，不猜用来源父产品快照。
    /// </summary>
    public async Task<IReadOnlyList<PurchaseDraftCandidateDto>> GetPurchaseCandidatesAsync(
        PurchaseDraftCandidateQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceFilter = request.SourceUid.HasValue
            ? "AND gd.[SourceType]=@SourceType AND gd.[SourceUid]=@SourceUid"
            : string.Empty;
        var sql = $@"
SELECT nd.[Uid],nd.[Version],nd.[LifecycleStatus],nd.[GrossDemandId],nd.[DecompositionRevision],nd.[LineNumber],
       gd.[CustomerId],customer.[CustomerCode],customer.[CustomerName],
       gd.[SourceType],gd.[SourceUid],gd.[SourceNo],gd.[SourceLineNumber],gd.[SourceLineId],nd.[RootNetDemandId],
       root.[SkuSnapshot] AS [RootSku],CAST(CASE WHEN nd.[DeliveryMode]=1 THEN 1 ELSE 0 END AS bit) AS [IsCustomerSupplied],
       nd.[DeliveryTargetNetDemandId],nd.[ProductId],CAST(CASE WHEN nd.[ProductId] IS NULL THEN 1 ELSE 0 END AS bit) AS [IsNonStandard],
       nd.[SkuSnapshot] AS [Sku],COALESCE(p.[LinkSku],N'') AS [LinkSku],nd.[CustomerSkuSnapshot] AS [CustomerSku],
       nd.[DescriptionSnapshot] AS [Description],nd.[ColorSnapshot] AS [Color],nd.[SpecificationSnapshot] AS [Specification],
       COALESCE(p.[MainImage],N'') AS [ImagePath],nd.[MaterialTypeSnapshot] AS [MaterialType],
       NULLIF(LTRIM(RTRIM(nd.[UnitSnapshot])),N'') AS [Unit],nd.[SupplyTypeSnapshot] AS [SupplyType],
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
       supplierProduct.[SupplierSku] AS [SuggestedSupplierSku],
       supplierProduct.[UnitPrice] AS [SuggestedUnitPrice],supplierProduct.[TaxPrice] AS [SuggestedTaxPrice],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate] AS [DemandRequiredDate],
       COALESCE(purchaseFacts.[AssociatedPurchaseQty],0) AS [AssociatedPurchaseQty],purchaseNumbers.[PurchaseOrderNos],
       COALESCE((
           SELECT facts.[SupplierId],MAX(facts.[SupplierCode]) AS [SupplierCode],MAX(facts.[SupplierName]) AS [SupplierName],
                  CAST(MAX(CASE WHEN activeSupplier.[Uid] IS NOT NULL THEN 1 ELSE 0 END) AS bit) AS [IsActive],
                  STRING_AGG(CAST(facts.[OrderNo] AS nvarchar(max)),N',') AS [PurchaseOrderNos]
           FROM
           (
               SELECT DISTINCT purchaseHead.[SupplierId],purchaseHead.[SupplierCode],purchaseHead.[SupplierName],purchaseHead.[OrderNo]
               FROM [dbo].[DemandExecutionLink] parentLink
               INNER JOIN [dbo].[PurchaseOrderHead] purchaseHead
                 ON purchaseHead.[Uid]=parentLink.[TargetHeadId]
                AND purchaseHead.[LifecycleStatus]=0 AND ISNULL(purchaseHead.[IsDeleted],0)=0
               INNER JOIN [dbo].[PurchaseOrderLine] purchaseLine
                 ON purchaseLine.[Uid]=parentLink.[TargetLineId]
                AND purchaseLine.[PurchaseOrderId]=purchaseHead.[Uid] AND ISNULL(purchaseLine.[IsDeleted],0)=0
               WHERE parentLink.[NetDemandId]=deliveryTarget.[Uid]
                 AND parentLink.[TargetType]=@PurchaseTargetType AND parentLink.[LinkStatus]=0 AND ISNULL(parentLink.[IsDeleted],0)=0
           ) facts
           LEFT JOIN [dbo].[Supplier] activeSupplier
             ON activeSupplier.[Uid]=facts.[SupplierId] AND activeSupplier.[IsActive]=1 AND ISNULL(activeSupplier.[IsDeleted],0)=0
           GROUP BY facts.[SupplierId]
           ORDER BY MAX(facts.[SupplierCode]),facts.[SupplierId]
           FOR JSON PATH
       ),N'[]') AS [ParentActualPurchaseSuppliersJson]
FROM [dbo].[NetDemand] nd
INNER JOIN [dbo].[GrossDemand] gd ON gd.[Uid]=nd.[GrossDemandId]
LEFT JOIN [dbo].[Customer] customer ON customer.[Uid]=gd.[CustomerId] AND ISNULL(customer.[IsDeleted],0)=0
LEFT JOIN [dbo].[NetDemand] root ON root.[Uid]=nd.[RootNetDemandId]
LEFT JOIN [dbo].[Product] p ON p.[Uid]=nd.[ProductId] AND ISNULL(p.[IsDeleted],0)=0
LEFT JOIN [dbo].[Supplier] suggestedSupplier
  ON suggestedSupplier.[Uid]=nd.[SuggestedPurchaseSupplierId] AND ISNULL(suggestedSupplier.[IsDeleted],0)=0
LEFT JOIN [dbo].[SupplierProduct] supplierProduct
  ON supplierProduct.[SupplierId]=nd.[SuggestedPurchaseSupplierId]
 AND supplierProduct.[SkuId]=nd.[ProductId] AND ISNULL(supplierProduct.[IsDeleted],0)=0
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
        var rows = await Connection.QueryAsync<PurchaseDraftCandidateQueryRow>(new CommandDefinition(
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
        var result = rows.ToList();
        foreach (var row in result)
        {
            row.ParentActualPurchaseSuppliers = JsonSerializer.Deserialize<List<PurchaseDraftActualSupplierFactDto>>(
                row.ParentActualPurchaseSuppliersJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        return result;
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
    /// <summary>承接 SQL 内部 JSON 列；解析后只向上层暴露结构化的采购供应商事实。</summary>
    private sealed class PurchaseDraftCandidateQueryRow : PurchaseDraftCandidateDto
    {
        public string ParentActualPurchaseSuppliersJson { get; set; } = "[]";
    }
}
