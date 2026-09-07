/*
  来源需求交付接线历史映射预览。只读检查，不修改生产数据。
  不按 SKU 推断 GrossDemand、销售行或订单池归属；多解和缺口必须人工核实。
*/
SET NOCOUNT ON;

-- 只读取元数据判断 025 目标列是否存在，旧结构执行本预检时不会静态引用尚未创建的列。
SELECT CASE WHEN COL_LENGTH(N'dbo.PackingListContributionLine',N'SourceDemandVersionSnapshot') IS NULL
            THEN N'待执行 025 增量结构' ELSE N'025 目标列已存在' END AS DeliverySchemaState;

SELECT pl.Uid AS ContributionId,pl.PackingListDocumentId,pl.IsDeleted,pl.GrossDemandId,
       pl.NetDemandId,pl.NetDemandAllocationId,
       CASE WHEN gd.Uid IS NULL THEN N'缺少来源需求'
            WHEN duplicateRows.ActiveCount>1 THEN N'来源身份存在重复活动需求'
            WHEN pl.GrossDemandId IS NULL THEN N'历史贡献缺少来源需求身份'
            ELSE N'来源需求身份可确定' END AS MappingResult
FROM dbo.PackingListContributionLine pl
LEFT JOIN dbo.GrossDemand gd ON gd.Uid=pl.GrossDemandId
OUTER APPLY(
    SELECT COUNT_BIG(1) AS ActiveCount
    FROM dbo.GrossDemand sameSource
    WHERE sameSource.SourceType=gd.SourceType AND sameSource.SourceUid=gd.SourceUid
      AND sameSource.SourceLineId=gd.SourceLineId AND sameSource.IsDeleted=0 AND sameSource.LifecycleStatus=0
) duplicateRows
ORDER BY pl.PackingListDocumentId,pl.LineNumber,pl.Uid;

SELECT gd.Uid AS GrossDemandId,gd.SourceType,gd.SourceUid,gd.SourceLineId,gd.SourceVersion,
       so.Version AS CurrentSalesHeaderVersionForReview,
       CASE WHEN gd.SourceVersion IS NULL AND gd.SourceType=N'SO' AND so.Uid IS NOT NULL
                      AND sol.Uid IS NOT NULL AND duplicateRows.ActiveCount<=1
            THEN so.Version ELSE NULL END AS SuggestedCutoverSourceVersion,
       CASE WHEN gd.SourceType=N'SO' THEN gd.SourceUid ELSE NULL END AS ProvenOrderPoolSalesOrderId,
       CASE WHEN gd.SourceVersion IS NOT NULL THEN N'已有来源版本'
            WHEN gd.SourceType=N'SO' AND so.Uid IS NULL THEN N'销售订单头不存在，不能建立版本基线'
            WHEN gd.SourceType=N'SO' AND sol.Uid IS NULL THEN N'销售订单行不存在，不能建立版本基线'
            WHEN duplicateRows.ActiveCount>1 THEN N'来源身份存在重复活动需求，不能建立版本基线'
            WHEN gd.SourceType=N'SO' THEN N'可采用当前销售头版本作为切换基线候选；该值不代表历史接收时点，需单独授权回填'
            ELSE N'来源版本无法由现有来源事实确定，不自动回填' END AS SourceVersionMappingResult,
       CASE WHEN gd.SourceType=N'CR' THEN N'售后来源缺少显式订单池归属，正式库存动作必须阻断'
            WHEN gd.SourceType=N'SO' AND so.Uid IS NULL THEN N'销售订单归属不存在'
            WHEN gd.SourceType=N'SO' AND sol.Uid IS NULL THEN N'销售订单行归属不存在'
            WHEN duplicateRows.ActiveCount>1 THEN N'来源身份存在重复活动需求'
            WHEN gd.SourceVersion IS NULL THEN N'来源版本尚未建立，正式提交必须阻断'
            WHEN gd.CustomerId IS NULL THEN N'缺少交付客户'
            WHEN gd.IsNonStandard=0 AND gd.ProductId IS NULL THEN N'标准件缺少正式产品身份'
            WHEN gd.IsNonStandard=0 AND NULLIF(LTRIM(RTRIM(gd.UnitSnapshot)),N'') IS NULL THEN N'标准件缺少单位快照'
            ELSE N'具备来源交付基础身份' END AS DeliveryReadiness
FROM dbo.GrossDemand gd
LEFT JOIN dbo.SalesOrderHeader so ON gd.SourceType=N'SO' AND so.Uid=gd.SourceUid AND so.IsDeleted=0
LEFT JOIN dbo.SalesOrderLine sol ON gd.SourceType=N'SO' AND sol.Uid=gd.SourceLineId
                               AND sol.SalesOrderId=gd.SourceUid AND sol.IsDeleted=0
OUTER APPLY(
    SELECT COUNT_BIG(1) AS ActiveCount
    FROM dbo.GrossDemand sameSource
    WHERE sameSource.SourceType=gd.SourceType AND sameSource.SourceUid=gd.SourceUid
      AND sameSource.SourceLineId=gd.SourceLineId AND sameSource.IsDeleted=0 AND sameSource.LifecycleStatus=0
) duplicateRows
WHERE gd.IsDeleted=0 AND gd.SourceType IN(N'SO',N'CR')
ORDER BY gd.SourceType,gd.SourceNo,gd.SourceLineNumber,gd.Uid;
