/*
    毛需求/净需求模型迁移前只读预检。
    本脚本不创建临时表、不执行 DDL/DML；所有问题仅通过结果集输出。
    任一阻断项存在时以 THROW 结束，禁止继续执行 003/004。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @BlockingIssues int = 0;
DECLARE @Count int;
DECLARE @Sql nvarchar(max);

SELECT
    DB_NAME() AS DatabaseName,
    SYSUTCDATETIME() AS CheckedAtUtc,
    CASE
        WHEN OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NULL THEN N'Missing'
        WHEN COL_LENGTH(N'dbo.DemandExecutionLink', N'LinkStatus') IS NULL
          OR COL_LENGTH(N'dbo.DemandExecutionLink', N'AllocatedQty') IS NULL
          OR COL_LENGTH(N'dbo.DemandExecutionLink', N'CompletedQty') IS NULL THEN N'Legacy'
        ELSE N'Target'
    END AS DemandExecutionLinkSchemaState;

SELECT N'MISSING_REQUIRED_TABLE' AS IssueCode, required_table.TableName AS ObjectName
FROM (VALUES
    (N'Product'), (N'Bom'), (N'SalesOrderHeader'), (N'SalesOrderLine'),
    (N'IndependentDemand'), (N'DependentDemand')
) required_table(TableName)
WHERE OBJECT_ID(N'dbo.' + required_table.TableName, N'U') IS NULL;

SELECT @Count = COUNT(*)
FROM (VALUES
    (N'Product'), (N'Bom'), (N'SalesOrderHeader'), (N'SalesOrderLine'),
    (N'IndependentDemand'), (N'DependentDemand')
) required_table(TableName)
WHERE OBJECT_ID(N'dbo.' + required_table.TableName, N'U') IS NULL;
SET @BlockingIssues += @Count;

IF OBJECT_ID(N'dbo.IndependentDemand', N'U') IS NOT NULL
BEGIN
    SELECT N'DUPLICATE_ACTIVE_SO_SOURCE_LINE' AS IssueCode,
           SourceLineId,
           COUNT(*) AS DuplicateCount
    FROM dbo.IndependentDemand
    WHERE SourceType = N'SO' AND IsDeleted = 0
    GROUP BY SourceLineId
    HAVING COUNT(*) > 1;

    SELECT @Count = COUNT(*)
    FROM (
        SELECT SourceLineId
        FROM dbo.IndependentDemand
        WHERE SourceType = N'SO' AND IsDeleted = 0
        GROUP BY SourceLineId
        HAVING COUNT(*) > 1
    ) duplicate_source;
    SET @BlockingIssues += @Count;

    IF OBJECT_ID(N'dbo.SalesOrderHeader', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.SalesOrderLine', N'U') IS NOT NULL
    BEGIN
        SELECT N'ORPHANED_SO_GROSS_DEMAND' AS IssueCode,
               demand.Uid AS GrossDemandId,
               demand.SourceUid,
               demand.SourceLineId,
               demand.SourceNo
        FROM dbo.IndependentDemand demand
        LEFT JOIN dbo.SalesOrderHeader head ON head.Uid = demand.SourceUid
        LEFT JOIN dbo.SalesOrderLine line ON line.Uid = demand.SourceLineId
        WHERE demand.SourceType = N'SO'
          AND demand.IsDeleted = 0
          AND (head.Uid IS NULL OR line.Uid IS NULL OR line.SalesOrderId <> head.Uid);

        SELECT @Count = COUNT(*)
        FROM dbo.IndependentDemand demand
        LEFT JOIN dbo.SalesOrderHeader head ON head.Uid = demand.SourceUid
        LEFT JOIN dbo.SalesOrderLine line ON line.Uid = demand.SourceLineId
        WHERE demand.SourceType = N'SO'
          AND demand.IsDeleted = 0
          AND (head.Uid IS NULL OR line.Uid IS NULL OR line.SalesOrderId <> head.Uid);
        SET @BlockingIssues += @Count;

        SELECT N'CLOSED_SO_HAS_DOWNSTREAM_GROSS_DEMAND' AS IssueCode,
               demand.Uid AS GrossDemandId,
               head.Uid AS SalesOrderId,
               head.OrderNo
        FROM dbo.IndependentDemand demand
        JOIN dbo.SalesOrderHeader head ON head.Uid = demand.SourceUid
        WHERE demand.SourceType = N'SO'
          AND demand.IsDeleted = 0
          AND head.IsDeleted = 0
          AND head.DocumentStatus = 9
          AND EXISTS (
              SELECT 1 FROM dbo.DependentDemand net
              WHERE net.IndependentDemandId = demand.Uid AND net.IsDeleted = 0
          );

        SELECT @Count = COUNT(*)
        FROM dbo.IndependentDemand demand
        JOIN dbo.SalesOrderHeader head ON head.Uid = demand.SourceUid
        WHERE demand.SourceType = N'SO'
          AND demand.IsDeleted = 0
          AND head.IsDeleted = 0
          AND head.DocumentStatus = 9
          AND EXISTS (
              SELECT 1 FROM dbo.DependentDemand net
              WHERE net.IndependentDemandId = demand.Uid AND net.IsDeleted = 0
          );
        SET @BlockingIssues += @Count;

        IF OBJECT_ID(N'dbo.Product', N'U') IS NOT NULL
        BEGIN
            SELECT N'STANDARD_GROSS_DEMAND_PRODUCT_NOT_PROVABLE' AS IssueCode,
                   demand.Uid AS GrossDemandId,
                   demand.SourceNo,
                   demand.Sku,
                   line.ProductId AS SalesOrderLineProductId,
                   product_match.MatchCount
            FROM dbo.IndependentDemand demand
            LEFT JOIN dbo.SalesOrderLine line ON line.Uid = demand.SourceLineId
            OUTER APPLY (
                SELECT COUNT(*) AS MatchCount
                FROM dbo.Product product
                WHERE product.Sku = demand.Sku AND product.IsDeleted = 0
            ) product_match
            WHERE demand.IsDeleted = 0
              AND demand.IsNonStandard = 0
              AND line.ProductId IS NULL
              AND product_match.MatchCount <> 1;

            SELECT @Count = COUNT(*)
            FROM dbo.IndependentDemand demand
            LEFT JOIN dbo.SalesOrderLine line ON line.Uid = demand.SourceLineId
            OUTER APPLY (
                SELECT COUNT(*) AS MatchCount
                FROM dbo.Product product
                WHERE product.Sku = demand.Sku AND product.IsDeleted = 0
            ) product_match
            WHERE demand.IsDeleted = 0
              AND demand.IsNonStandard = 0
              AND line.ProductId IS NULL
              AND product_match.MatchCount <> 1;
            SET @BlockingIssues += @Count;
        END;
    END;

    IF COL_LENGTH(N'dbo.IndependentDemand', N'ProductId') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''NON_STANDARD_GROSS_DEMAND_HAS_PRODUCT'' AS IssueCode,
                   Uid AS GrossDemandId, ProductId
            FROM dbo.IndependentDemand
            WHERE IsDeleted = 0 AND IsNonStandard = 1 AND ProductId IS NOT NULL;
            SELECT @IssueCount = COUNT(*)
            FROM dbo.IndependentDemand
            WHERE IsDeleted = 0 AND IsNonStandard = 1 AND ProductId IS NOT NULL;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;

    IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleStatus') IS NOT NULL
       AND COL_LENGTH(N'dbo.IndependentDemand', N'DecompositionStatus') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''UNKNOWN_GROSS_DEMAND_TARGET_STATUS'' AS IssueCode,
                   Uid AS GrossDemandId, LifecycleStatus, DecompositionStatus
            FROM dbo.IndependentDemand
            WHERE LifecycleStatus NOT IN (0, 8, 9)
               OR DecompositionStatus NOT IN (0, 1);
            SELECT @IssueCount = COUNT(*)
            FROM dbo.IndependentDemand
            WHERE LifecycleStatus NOT IN (0, 8, 9)
               OR DecompositionStatus NOT IN (0, 1);';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;

    SELECT N'UNKNOWN_LEGACY_GROSS_DEMAND_STATUS' AS IssueCode,
           Uid AS GrossDemandId,
           OrderStatus
    FROM dbo.IndependentDemand
    WHERE OrderStatus NOT IN (0, 1, 2, 5, 6, 7, 8);

    SELECT @Count = COUNT(*)
    FROM dbo.IndependentDemand
    WHERE OrderStatus NOT IN (0, 1, 2, 5, 6, 7, 8);
    SET @BlockingIssues += @Count;
END;

IF OBJECT_ID(N'dbo.DependentDemand', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.IndependentDemand', N'U') IS NOT NULL
BEGIN
    SELECT N'ORPHANED_NET_DEMAND' AS IssueCode,
           net.Uid AS NetDemandId,
           net.IndependentDemandId AS GrossDemandId
    FROM dbo.DependentDemand net
    LEFT JOIN dbo.IndependentDemand gross ON gross.Uid = net.IndependentDemandId
    WHERE net.IsDeleted = 0 AND gross.Uid IS NULL;

    SELECT @Count = COUNT(*)
    FROM dbo.DependentDemand net
    LEFT JOIN dbo.IndependentDemand gross ON gross.Uid = net.IndependentDemandId
    WHERE net.IsDeleted = 0 AND gross.Uid IS NULL;
    SET @BlockingIssues += @Count;

    SELECT N'UNKNOWN_LEGACY_NET_DEMAND_STATUS' AS IssueCode,
           Uid AS NetDemandId,
           OrderStatus
    FROM dbo.DependentDemand
    WHERE OrderStatus NOT IN (0, 1, 3, 5, 6, 8, 9);

    SELECT @Count = COUNT(*)
    FROM dbo.DependentDemand
    WHERE OrderStatus NOT IN (0, 1, 3, 5, 6, 8, 9);
    SET @BlockingIssues += @Count;

    IF COL_LENGTH(N'dbo.DependentDemand', N'DecompositionRevision') IS NOT NULL
       AND COL_LENGTH(N'dbo.DependentDemand', N'LineNumber') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''DUPLICATE_NET_DEMAND_REVISION_LINE'' AS IssueCode,
                   IndependentDemandId AS GrossDemandId,
                   DecompositionRevision,
                   LineNumber,
                   COUNT(*) AS DuplicateCount
            FROM dbo.DependentDemand
            WHERE IsDeleted = 0
            GROUP BY IndependentDemandId, DecompositionRevision, LineNumber
            HAVING COUNT(*) > 1;
            SELECT @IssueCount = COUNT(*)
            FROM (
                SELECT IndependentDemandId, DecompositionRevision, LineNumber
                FROM dbo.DependentDemand
                WHERE IsDeleted = 0
                GROUP BY IndependentDemandId, DecompositionRevision, LineNumber
                HAVING COUNT(*) > 1
            ) duplicate_line;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;
END;

IF OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.DemandExecutionLink', N'DependentDemandId') IS NULL
    BEGIN
        SELECT N'DEMAND_EXECUTION_LINK_NET_DEMAND_COLUMN_MISSING' AS IssueCode,
               N'DependentDemandId' AS ColumnName;
        SET @BlockingIssues += 1;
    END
    ELSE IF OBJECT_ID(N'dbo.DependentDemand', N'U') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''ORPHANED_DEMAND_EXECUTION_LINK'' AS IssueCode,
                   link.Uid AS LinkId,
                   link.DependentDemandId AS NetDemandId
            FROM dbo.DemandExecutionLink link
            LEFT JOIN dbo.DependentDemand net ON net.Uid = link.DependentDemandId
            WHERE net.Uid IS NULL;
            SELECT @IssueCount = COUNT(*)
            FROM dbo.DemandExecutionLink link
            LEFT JOIN dbo.DependentDemand net ON net.Uid = link.DependentDemandId
            WHERE net.Uid IS NULL;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;
END;

IF OBJECT_ID(N'dbo.Product', N'U') IS NOT NULL
BEGIN
    SELECT N'INVALID_CUSTOMER_SUPPLIED_PRODUCT' AS IssueCode,
           Uid AS ProductId,
           Sku,
           SupplyType,
           BomNo,
           BomVersion
    FROM dbo.Product
    WHERE IsDeleted = 0
      AND MaterialType = N'客供件'
      AND (SupplyType <> N'采购' OR BomVersion <> 0 OR NULLIF(LTRIM(RTRIM(BomNo)), N'') IS NOT NULL);

    SELECT @Count = COUNT(*)
    FROM dbo.Product
    WHERE IsDeleted = 0
      AND MaterialType = N'客供件'
      AND (SupplyType <> N'采购' OR BomVersion <> 0 OR NULLIF(LTRIM(RTRIM(BomNo)), N'') IS NOT NULL);
    SET @BlockingIssues += @Count;
END;

IF OBJECT_ID(N'dbo.Bom', N'U') IS NOT NULL
BEGIN
    SELECT N'INVALID_BOM_QUANTITY_RATIO' AS IssueCode,
           Uid AS BomLineId,
           BomNo,
           Numerator,
           Denominator
    FROM dbo.Bom
    WHERE IsDeleted = 0 AND (Numerator <= 0 OR Denominator <= 0);

    SELECT @Count = COUNT(*)
    FROM dbo.Bom
    WHERE IsDeleted = 0 AND (Numerator <= 0 OR Denominator <= 0);
    SET @BlockingIssues += @Count;

    /*
        兼容三类 BOM 结构：
        1. 目标结构：ParentSkuId + SubSkuId；
        2. 中间结构：ParentSku + SubSku；
        3. 原始结构：仅 BomNo + SubSkuId/SubSku，父件由 Product.BomNo 唯一反查。
        可选列必须放入动态 SQL，否则 SQL Server 会在进入 IF 分支前解析失败。
    */
    IF COL_LENGTH(N'dbo.Bom', N'ParentSkuId') IS NOT NULL
       AND COL_LENGTH(N'dbo.Bom', N'SubSkuId') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''BOM_PARENT_EQUALS_CHILD'' AS IssueCode,
                   Uid AS BomLineId, ParentSkuId, SubSkuId
            FROM dbo.Bom
            WHERE IsDeleted = 0 AND ParentSkuId IS NOT NULL AND ParentSkuId = SubSkuId;
            SELECT @IssueCount = COUNT(*)
            FROM dbo.Bom
            WHERE IsDeleted = 0 AND ParentSkuId IS NOT NULL AND ParentSkuId = SubSkuId;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END
    ELSE IF COL_LENGTH(N'dbo.Bom', N'ParentSku') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''BOM_PARENT_EQUALS_CHILD'' AS IssueCode,
                   Uid AS BomLineId, ParentSku, SubSku
            FROM dbo.Bom
            WHERE IsDeleted = 0
              AND NULLIF(LTRIM(RTRIM(ParentSku)), N'''') IS NOT NULL
              AND ParentSku = SubSku;
            SELECT @IssueCount = COUNT(*)
            FROM dbo.Bom
            WHERE IsDeleted = 0
              AND NULLIF(LTRIM(RTRIM(ParentSku)), N'''') IS NOT NULL
              AND ParentSku = SubSku;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END
    ELSE IF OBJECT_ID(N'dbo.Product', N'U') IS NOT NULL
    BEGIN
        SELECT N'BOM_PARENT_EQUALS_CHILD' AS IssueCode,
               bom.Uid AS BomLineId,
               parent.Uid AS ParentProductId,
               bom.SubSkuId,
               bom.SubSku
        FROM dbo.Bom bom
        JOIN dbo.Product parent ON parent.BomNo = bom.BomNo
        WHERE bom.IsDeleted = 0
          AND (bom.SubSkuId = parent.Uid OR (bom.SubSkuId IS NULL AND bom.SubSku = parent.Sku));

        SELECT @Count = COUNT(*)
        FROM dbo.Bom bom
        JOIN dbo.Product parent ON parent.BomNo = bom.BomNo
        WHERE bom.IsDeleted = 0
          AND (bom.SubSkuId = parent.Uid OR (bom.SubSkuId IS NULL AND bom.SubSku = parent.Sku));
        SET @BlockingIssues += @Count;
    END;

    IF OBJECT_ID(N'dbo.Product', N'U') IS NOT NULL
    BEGIN
        /* 父件必须能唯一映射到 Product；规则与 004 的回填条件完全一致。 */
        IF COL_LENGTH(N'dbo.Bom', N'ParentSku') IS NOT NULL
        BEGIN
            SET @Sql = N'
                SELECT N''BOM_PARENT_PRODUCT_NOT_UNIQUE'' AS IssueCode,
                       bom.Uid AS BomLineId, bom.BomNo, bom.ParentSku, product_match.MatchCount
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount
                    FROM dbo.Product product
                    WHERE (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'''') IS NOT NULL AND product.Sku = bom.ParentSku)
                       OR (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'''') IS NULL AND product.BomNo = bom.BomNo)
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount
                    FROM dbo.Product product
                    WHERE (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'''') IS NOT NULL AND product.Sku = bom.ParentSku)
                       OR (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'''') IS NULL AND product.BomNo = bom.BomNo)
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;';
            SET @Count = 0;
            EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
            SET @BlockingIssues += @Count;
        END
        ELSE
        BEGIN
            SELECT N'BOM_PARENT_PRODUCT_NOT_UNIQUE' AS IssueCode,
                   bom.Uid AS BomLineId,
                   bom.BomNo,
                   product_match.MatchCount
            FROM dbo.Bom bom
            OUTER APPLY (
                SELECT COUNT(*) AS MatchCount
                FROM dbo.Product product
                WHERE product.BomNo = bom.BomNo
            ) product_match
            WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;

            SELECT @Count = COUNT(*)
            FROM dbo.Bom bom
            OUTER APPLY (
                SELECT COUNT(*) AS MatchCount
                FROM dbo.Product product
                WHERE product.BomNo = bom.BomNo
            ) product_match
            WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;
            SET @BlockingIssues += @Count;
        END;

        /* 子件以已有 SubSkuId 为权威；为空时才按 SKU 唯一映射。 */
        IF COL_LENGTH(N'dbo.Bom', N'SubSkuId') IS NOT NULL
        BEGIN
            SET @Sql = N'
                SELECT N''BOM_CHILD_PRODUCT_NOT_UNIQUE'' AS IssueCode,
                       bom.Uid AS BomLineId, bom.SubSkuId, bom.SubSku, product_match.MatchCount
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount
                    FROM dbo.Product product
                    WHERE (bom.SubSkuId IS NOT NULL AND product.Uid = bom.SubSkuId)
                       OR (bom.SubSkuId IS NULL AND product.Sku = bom.SubSku)
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount
                    FROM dbo.Product product
                    WHERE (bom.SubSkuId IS NOT NULL AND product.Uid = bom.SubSkuId)
                       OR (bom.SubSkuId IS NULL AND product.Sku = bom.SubSku)
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;';
        END
        ELSE
        BEGIN
            SET @Sql = N'
                SELECT N''BOM_CHILD_PRODUCT_NOT_UNIQUE'' AS IssueCode,
                       bom.Uid AS BomLineId, bom.SubSku, product_match.MatchCount
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount FROM dbo.Product product WHERE product.Sku = bom.SubSku
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Bom bom
                OUTER APPLY (
                    SELECT COUNT(*) AS MatchCount FROM dbo.Product product WHERE product.Sku = bom.SubSku
                ) product_match
                WHERE bom.IsDeleted = 0 AND product_match.MatchCount <> 1;';
        END;
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;

        /* 客供件不得成为 BOM 父件；根据当前结构选择关系字段。 */
        IF COL_LENGTH(N'dbo.Bom', N'ParentSkuId') IS NOT NULL
        BEGIN
            SET @Sql = N'
                SELECT N''CUSTOMER_SUPPLIED_PRODUCT_HAS_ACTIVE_BOM'' AS IssueCode,
                       product.Uid AS ProductId, product.Sku, bom.Uid AS BomLineId
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.ParentSkuId = product.Uid AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.ParentSkuId = product.Uid AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';';
        END
        ELSE IF COL_LENGTH(N'dbo.Bom', N'ParentSku') IS NOT NULL
        BEGIN
            SET @Sql = N'
                SELECT N''CUSTOMER_SUPPLIED_PRODUCT_HAS_ACTIVE_BOM'' AS IssueCode,
                       product.Uid AS ProductId, product.Sku, bom.Uid AS BomLineId
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.ParentSku = product.Sku AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.ParentSku = product.Sku AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';';
        END
        ELSE
        BEGIN
            SET @Sql = N'
                SELECT N''CUSTOMER_SUPPLIED_PRODUCT_HAS_ACTIVE_BOM'' AS IssueCode,
                       product.Uid AS ProductId, product.Sku, bom.Uid AS BomLineId
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.BomNo = product.BomNo AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';
                SELECT @IssueCount = COUNT(*)
                FROM dbo.Product product
                JOIN dbo.Bom bom ON bom.BomNo = product.BomNo AND bom.IsDeleted = 0
                WHERE product.IsDeleted = 0 AND product.MaterialType = N''客供件'';';
        END;
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;

    IF COL_LENGTH(N'dbo.Bom', N'BomVersion') IS NOT NULL
       AND COL_LENGTH(N'dbo.Bom', N'LineNumber') IS NOT NULL
    BEGIN
        SET @Sql = N'
            SELECT N''DUPLICATE_BOM_LINE_NUMBER'' AS IssueCode,
                   BomNo, BomVersion, LineNumber, COUNT(*) AS DuplicateCount
            FROM dbo.Bom
            WHERE IsDeleted = 0
            GROUP BY BomNo, BomVersion, LineNumber
            HAVING COUNT(*) > 1;
            SELECT @IssueCount = COUNT(*)
            FROM (
                SELECT BomNo, BomVersion, LineNumber
                FROM dbo.Bom
                WHERE IsDeleted = 0
                GROUP BY BomNo, BomVersion, LineNumber
                HAVING COUNT(*) > 1
            ) duplicate_line;';
        SET @Count = 0;
        EXEC sys.sp_executesql @Sql, N'@IssueCount int OUTPUT', @IssueCount = @Count OUTPUT;
        SET @BlockingIssues += @Count;
    END;
END;

SELECT @BlockingIssues AS BlockingIssueCount;

IF @BlockingIssues > 0
    THROW 51200, N'Demand planning preflight failed. Resolve all reported blocking issues before migration.', 1;
