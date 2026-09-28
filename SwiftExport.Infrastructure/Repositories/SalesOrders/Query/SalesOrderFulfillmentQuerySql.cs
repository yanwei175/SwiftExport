namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>在候选销售订单集合上计算同一套行级来源交付事实，供列表和详情共用。</summary>
internal static class SalesOrderFulfillmentQuerySql
{
    /// <summary>要求调用方先建立含销售头关键字段的 #CandidateOrders，结果写入 #SalesLineFacts。</summary>
    internal const string CreateLineFacts = """
        SELECT l.SalesOrderId, l.Uid AS SalesOrderLineId, l.LineNumber, l.Qty,
               l.UnitSnapshot, CONVERT(date, COALESCE(l.ExpectedDeliveryDate, h.DeliveryDate)) AS EffectiveDueDate,
               delivery.ShippedQty,
               CASE WHEN delivery.ShippedQty IS NULL THEN NULL
                    WHEN delivery.ShippedQty >= l.Qty THEN CONVERT(decimal(18,4), 0)
                    ELSE l.Qty - delivery.ShippedQty END AS UnshippedQty,
               reason.ReviewReason
        INTO #SalesLineFacts
        FROM #CandidateOrders h
        INNER JOIN dbo.SalesOrderLine l ON l.SalesOrderId=h.Uid AND l.IsDeleted=0
        OUTER APPLY (
            SELECT TOP (1) ranked.*,
                   CASE WHEN ranked.ActiveCount>0 THEN ranked.ActiveCount ELSE ranked.AllCount END AS CandidateCount
            FROM (
                SELECT g.*,
                       SUM(CASE WHEN g.LifecycleStatus=@DemandActive THEN 1 ELSE 0 END) OVER () AS ActiveCount,
                       COUNT(*) OVER () AS AllCount
                FROM dbo.GrossDemand g
                WHERE g.SourceType=@SalesSourceType AND g.SourceUid=h.Uid
                  AND g.SourceLineId=l.Uid AND g.IsDeleted=0
                  AND g.LifecycleStatus IN (@DemandActive,@DemandVoided)
            ) ranked
            ORDER BY CASE WHEN ranked.LifecycleStatus=@DemandActive THEN 0 ELSE 1 END,
                     ranked.SourceVersion DESC, ranked.Uid
        ) g
        OUTER APPLY (
            SELECT COALESCE(SUM(CASE WHEN pl.IsDeleted=0 AND ph.IsDeleted=0 AND ph.PostingStatus=@Posted
                                     THEN pl.Qty ELSE 0 END), 0) AS DeliveredQty
            FROM dbo.PackingListLine pl
            LEFT JOIN dbo.PackingListHead ph ON ph.Uid=pl.PackingListHeadId
            WHERE pl.GrossDemandId=g.Uid
        ) packing
        OUTER APPLY (
            SELECT COUNT(1) AS InvalidHistoryCount
            FROM dbo.PackingListLine pl
            LEFT JOIN dbo.PackingListHead ph ON ph.Uid=pl.PackingListHeadId
            WHERE pl.GrossDemandId=g.Uid AND (ph.Uid IS NULL
                OR (pl.IsDeleted=1 AND ph.PostingStatus=@Posted)
                OR (pl.IsDeleted=0 AND (ph.IsDeleted=1
                    OR ph.PostingStatus NOT IN (@Unposted,@Posted,@Reversed)
                    OR ph.CustomerId<>h.CustomerId
                    OR (ph.PostingStatus=@Posted AND ph.PostedDate IS NULL)
                    OR (ph.PostingStatus=@Reversed AND ph.ReversedDate IS NULL)
                    OR pl.Qty<=0 OR NULLIF(LTRIM(RTRIM(pl.UnitSnapshot)),N'') IS NULL
                    OR pl.UnitSnapshot<>l.UnitSnapshot
                    OR pl.IsNonStandardSnapshot<>l.IsNonStandard
                    OR ISNULL(pl.ProductId,'00000000-0000-0000-0000-000000000000')
                       <>ISNULL(l.ProductId,'00000000-0000-0000-0000-000000000000')
                    OR pl.SkuSnapshot<>l.Sku
                    OR pl.SourceTypeSnapshot<>@SalesSourceType
                    OR pl.SourceDocumentNoSnapshot<>h.OrderNo
                    OR pl.SourceLineNumberSnapshot IS NULL
                    OR pl.SourceLineNumberSnapshot<>l.LineNumber)))
        ) history
        CROSS APPLY (
            SELECT CASE
                WHEN h.DocumentStatus=@OrderOpen AND h.ConfirmationStatus=@Pending AND g.Uid IS NULL
                    THEN CONVERT(bit,1)
                WHEN h.ConfirmationStatus<>@Confirmed THEN CONVERT(bit,0)
                WHEN g.Uid IS NULL OR g.CandidateCount<>1 THEN CONVERT(bit,0)
                WHEN g.LifecycleStatus<>@DemandActive
                     AND (h.DocumentStatus<>@OrderClosed OR g.LifecycleStatus<>@DemandVoided)
                    THEN CONVERT(bit,0)
                WHEN g.SourceType<>@SalesSourceType OR g.SourceUid<>h.Uid OR g.SourceNo<>h.OrderNo
                  OR h.CustomerId IS NULL OR g.CustomerId IS NULL OR g.CustomerId<>h.CustomerId
                  OR g.SourceLineNumber IS NULL OR g.SourceLineNumber<>l.LineNumber
                  OR g.IsNonStandard<>l.IsNonStandard
                  OR ISNULL(g.ProductId,'00000000-0000-0000-0000-000000000000')
                     <>ISNULL(l.ProductId,'00000000-0000-0000-0000-000000000000')
                  OR g.SkuSnapshot<>l.Sku
                  OR ISNULL(g.LinkSkuSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.LinkSku)),N''),N'')
                  OR ISNULL(g.CustomerSkuSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.CustomerSku)),N''),N'')
                  OR ISNULL(g.DescriptionSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.DescriptionEn)),N''),N'')
                  OR ISNULL(g.ColorSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.ColorNameEn)),N''),N'')
                  OR ISNULL(g.SpecificationSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.FeaturesEn)),N''),N'')
                  OR ISNULL(g.ImagePathSnapshot,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.MainImage)),N''),N'')
                  OR ISNULL(g.Remark,N'')<>ISNULL(NULLIF(LTRIM(RTRIM(l.SubNote)),N''),N'')
                  OR NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                  OR NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') IS NULL
                  OR g.UnitSnapshot<>l.UnitSnapshot
                  OR g.DemandQty<>l.Qty OR ISNULL(g.RequiredDate,'0001-01-01')
                     <>ISNULL(COALESCE(l.ExpectedDeliveryDate,h.DeliveryDate),'0001-01-01')
                    THEN CONVERT(bit,0)
                WHEN g.SourceVersion IS NULL OR g.SourceVersion<=0 OR g.SourceVersion>h.Version
                  OR g.DemandBasisFingerprint IS NULL OR LEN(g.DemandBasisFingerprint)<>64
                  OR g.DemandBasisFingerprint COLLATE Latin1_General_BIN2 LIKE N'%[^0-9A-F]%'
                  OR history.InvalidHistoryCount>0 THEN CONVERT(bit,0)
                ELSE CONVERT(bit,1) END AS IsVerifiable
        ) verified
        CROSS APPLY (
            SELECT CASE WHEN verified.IsVerifiable=0 THEN NULL
                        WHEN h.ConfirmationStatus=@Pending THEN CONVERT(decimal(18,4),0)
                        ELSE CONVERT(decimal(18,4),packing.DeliveredQty) END AS ShippedQty
        ) delivery
        CROSS APPLY (
            SELECT CASE
                WHEN verified.IsVerifiable=0 AND h.ConfirmationStatus=@Pending THEN N'待确认仍有关联来源'
                WHEN verified.IsVerifiable=0 AND g.Uid IS NULL THEN N'来源需求缺失'
                WHEN verified.IsVerifiable=0 AND g.CandidateCount<>1 THEN N'来源需求重复'
                WHEN verified.IsVerifiable=0 AND g.LifecycleStatus<>@DemandActive
                     AND (h.DocumentStatus<>@OrderClosed OR g.LifecycleStatus<>@DemandVoided)
                    THEN N'来源状态不一致'
                WHEN verified.IsVerifiable=0 AND (g.SourceVersion IS NULL OR g.SourceVersion<=0
                    OR g.SourceVersion>h.Version OR g.DemandBasisFingerprint IS NULL
                    OR LEN(g.DemandBasisFingerprint)<>64
                    OR g.DemandBasisFingerprint COLLATE Latin1_General_BIN2 LIKE N'%[^0-9A-F]%')
                    THEN N'旧来源证据缺失'
                WHEN verified.IsVerifiable=0 AND history.InvalidHistoryCount>0 THEN N'装箱过账历史异常'
                WHEN verified.IsVerifiable=0 THEN N'来源身份或单位不一致'
                WHEN delivery.ShippedQty>l.Qty THEN N'超发需核实'
                ELSE NULL END AS ReviewReason
        ) reason;
        """;

    /// <summary>把行级事实汇总为每单一行；提醒仅使用可核实的商业未交量。</summary>
    internal const string CreateOrderFacts = """
        SELECT h.Uid,
               COUNT(l.SalesOrderLineId) AS LineCount,
               COALESCE(SUM(CASE WHEN l.ReviewReason IS NOT NULL THEN 1 ELSE 0 END),0) AS ReviewCount,
               COALESCE(SUM(CASE WHEN l.SalesOrderLineId IS NOT NULL AND l.ShippedQty IS NULL
                                 THEN 1 ELSE 0 END),0) AS UnknownCount,
               COUNT(DISTINCT NULLIF(l.UnitSnapshot,N'')) AS UnitCount,
               COALESCE(SUM(CASE WHEN NULLIF(l.UnitSnapshot,N'') IS NULL THEN 1 ELSE 0 END),0) AS MissingUnitCount,
               COALESCE(SUM(CASE WHEN l.ShippedQty IS NOT NULL AND l.UnshippedQty>0 THEN 1 ELSE 0 END),0) AS OutstandingLineCount,
               COALESCE(SUM(CASE WHEN l.ShippedQty IS NOT NULL AND l.UnshippedQty>0
                                    AND h.DocumentStatus=@OrderOpen AND h.ConfirmationStatus=@Confirmed
                                    AND l.EffectiveDueDate<@Today THEN 1 ELSE 0 END),0) AS OverdueLineCount,
               COALESCE(SUM(CASE WHEN l.ShippedQty IS NOT NULL AND l.UnshippedQty>0
                                    AND h.DocumentStatus=@OrderOpen AND h.ConfirmationStatus=@Confirmed
                                    AND l.EffectiveDueDate>=@Today AND l.EffectiveDueDate<=@SoonEnd
                                    THEN 1 ELSE 0 END),0) AS DueSoonLineCount,
               COALESCE(SUM(CASE WHEN h.ConfirmationStatus=@Pending AND l.UnshippedQty>0
                                    AND l.EffectiveDueDate<@Today THEN 1 ELSE 0 END),0) AS PendingPastDueLineCount,
               MIN(CASE WHEN l.ShippedQty IS NULL OR l.UnshippedQty>0
                        THEN l.EffectiveDueDate END) AS EarliestOpenDueDate,
               CONVERT(bit,MAX(CASE WHEN l.SalesOrderLineId IS NOT NULL
                                    AND (l.ShippedQty IS NULL OR l.UnshippedQty>0)
                                    AND l.EffectiveDueDate IS NULL THEN 1 ELSE 0 END)) AS HasUndatedOpenLine,
               MIN(CASE WHEN l.ShippedQty IS NOT NULL AND l.UnshippedQty>0
                             AND h.DocumentStatus=@OrderOpen AND h.ConfirmationStatus=@Confirmed
                             AND l.EffectiveDueDate<@Today THEN l.EffectiveDueDate END) AS EarliestOverdueDate,
               MIN(CASE WHEN l.ShippedQty IS NOT NULL AND l.UnshippedQty>0
                             AND h.DocumentStatus=@OrderOpen AND h.ConfirmationStatus=@Confirmed
                             AND l.EffectiveDueDate>=@Today AND l.EffectiveDueDate<=@SoonEnd
                             THEN l.EffectiveDueDate END) AS EarliestDueSoonDate,
               SUM(l.Qty) AS OrderedQty, SUM(l.ShippedQty) AS ShippedQty,
               SUM(l.UnshippedQty) AS UnshippedQty, MAX(l.UnitSnapshot) AS UnitSnapshot
        INTO #SalesOrderFacts
        FROM #CandidateOrders h
        LEFT JOIN #SalesLineFacts l ON l.SalesOrderId=h.Uid
        GROUP BY h.Uid;
        """;
}
