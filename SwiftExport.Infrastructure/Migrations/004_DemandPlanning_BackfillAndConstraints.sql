/*
    Demand Planning Phase 2：可证明的历史回填、NOT NULL、检查约束、唯一索引和强关系。
    必须在 demand_planning_preflight.sql 与 003 成功后执行。
    对无法证明的 Product/BOM/根净需求/执行目标映射直接 THROW，不猜测历史关系。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51400, N'Refusing to run demand planning migration against a system database.', 1;

IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
 OR OBJECT_ID(N'dbo.Bom', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesOrderHeader', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesOrderLine', N'U') IS NULL
 OR OBJECT_ID(N'dbo.IndependentDemand', N'U') IS NULL
 OR OBJECT_ID(N'dbo.DependentDemand', N'U') IS NULL
 OR OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NULL
    THROW 51401, N'Required demand planning tables are missing. Run 003 first.', 1;

IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleStatus') IS NULL
 OR COL_LENGTH(N'dbo.IndependentDemand', N'DecompositionStatus') IS NULL
 OR COL_LENGTH(N'dbo.DependentDemand', N'RootNetDemandId') IS NULL
 OR COL_LENGTH(N'dbo.DemandExecutionLink', N'LinkStatus') IS NULL
    THROW 51402, N'Demand planning additive columns are missing. Run 003 first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    /*
        003 为迁移期间查询建立的普通索引会依赖当前可空列。
        SQL Server 不允许在依赖索引存在时把这些列改为 NOT NULL，
        因此在同一事务内临时移除，并在约束完成后按原定义重建。
    */
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'IX_Bom_DemandPlanningLookup')
        DROP INDEX IX_Bom_DemandPlanningLookup ON dbo.Bom;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'IX_IndependentDemand_DemandPlanningSearch')
        DROP INDEX IX_IndependentDemand_DemandPlanningSearch ON dbo.IndependentDemand;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_GrossDemandLifecycle')
        DROP INDEX IX_DependentDemand_GrossDemandLifecycle ON dbo.DependentDemand;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'IX_DemandExecutionLink_NetDemandStatus')
        DROP INDEX IX_DemandExecutionLink_NetDemandStatus ON dbo.DemandExecutionLink;

    /* ---------- BOM 可证明回填 ---------- */
    IF EXISTS (
        SELECT 1
        FROM dbo.Bom bom
        WHERE (
                (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'') IS NOT NULL AND
                 (SELECT COUNT(*) FROM dbo.Product product WHERE product.Sku = bom.ParentSku) <> 1)
             OR (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'') IS NULL AND
                 (SELECT COUNT(*) FROM dbo.Product product WHERE product.BomNo = bom.BomNo) <> 1)
          )
    )
        THROW 51422, N'A BOM parent product/version mapping is not unique.', 1;

    UPDATE bom
       SET ParentSkuId = product.Uid,
           ParentSku = COALESCE(NULLIF(LTRIM(RTRIM(bom.ParentSku)), N''), product.Sku),
           BomVersion = product.BomVersion
    FROM dbo.Bom bom
    JOIN dbo.Product product
      ON (
            (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'') IS NOT NULL AND product.Sku = bom.ParentSku)
         OR (NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'') IS NULL AND product.BomNo = bom.BomNo)
     )
    WHERE bom.ParentSkuId IS NULL
       OR NULLIF(LTRIM(RTRIM(bom.ParentSku)), N'') IS NULL
       OR bom.BomVersion IS NULL;

    UPDATE bom
       SET SubSkuId = product_match.Uid
    FROM dbo.Bom bom
    CROSS APPLY
    (
        SELECT CONVERT(uniqueidentifier, MIN(CONVERT(char(36), product.Uid))) AS Uid,
               COUNT(*) AS MatchCount
        FROM dbo.Product product
        WHERE product.Sku = bom.SubSku
    ) product_match
    WHERE bom.SubSkuId IS NULL AND product_match.MatchCount = 1;

    ;WITH missing_line AS
    (
        SELECT bom.Uid,
               ISNULL(existing_line.MaxLineNumber, 0)
                   + ROW_NUMBER() OVER (PARTITION BY bom.BomNo, bom.BomVersion ORDER BY bom.Uid) AS GeneratedLineNumber
        FROM dbo.Bom bom
        OUTER APPLY
        (
            SELECT MAX(other.LineNumber) AS MaxLineNumber
            FROM dbo.Bom other
            WHERE other.BomNo = bom.BomNo
              AND other.BomVersion = bom.BomVersion
              AND other.LineNumber IS NOT NULL
        ) existing_line
        WHERE bom.LineNumber IS NULL
    )
    UPDATE bom
       SET LineNumber = missing_line.GeneratedLineNumber
    FROM dbo.Bom bom
    JOIN missing_line ON missing_line.Uid = bom.Uid;

    IF EXISTS (
        SELECT 1
        FROM dbo.Bom
        WHERE BomVersion IS NULL
           OR ParentSkuId IS NULL
           OR NULLIF(LTRIM(RTRIM(ParentSku)), N'') IS NULL
           OR SubSkuId IS NULL
           OR LineNumber IS NULL
    )
        THROW 51403, N'One or more BOM rows cannot be mapped to a unique parent product/version.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.Bom
        GROUP BY BomNo, BomVersion, LineNumber
        HAVING COUNT(*) > 1
    )
        THROW 51404, N'Duplicate BOM line numbers exist within one BOM version.', 1;

    /* ---------- 毛需求来源、快照和目标状态回填 ---------- */
    UPDATE gross
       SET ProductId = CASE WHEN gross.IsNonStandard = 0 THEN COALESCE(gross.ProductId, line.ProductId) ELSE NULL END,
           SourceLineNumber = line.LineNumber,
           LinkSkuSnapshot = COALESCE(gross.LinkSkuSnapshot, line.LinkSku),
           ImagePathSnapshot = COALESCE(gross.ImagePathSnapshot, line.MainImage)
    FROM dbo.IndependentDemand gross
    JOIN dbo.SalesOrderLine line ON line.Uid = gross.SourceLineId
    WHERE gross.SourceType = N'SO';

    UPDATE gross
       SET ProductId = product_match.Uid
    FROM dbo.IndependentDemand gross
    CROSS APPLY
    (
        SELECT CONVERT(uniqueidentifier, MIN(CONVERT(char(36), product.Uid))) AS Uid, COUNT(*) AS MatchCount
        FROM dbo.Product product
        WHERE product.Sku = gross.Sku
          AND (product.IsDeleted = 0 OR gross.IsDeleted = 1)
    ) product_match
    WHERE gross.IsNonStandard = 0
      AND gross.ProductId IS NULL
      AND product_match.MatchCount = 1;

    UPDATE dbo.IndependentDemand
       SET SourceUid = Uid,
           SourceLineId = Uid
    WHERE SourceType = N'MD';

    UPDATE gross
       SET LifecycleStatus = COALESCE(gross.LifecycleStatus, CASE WHEN gross.IsDeleted = 1 THEN 9 ELSE 0 END),
           DecompositionStatus = COALESCE(gross.DecompositionStatus, CASE WHEN active_net.HasActiveNetDemand = 1 THEN 1 ELSE 0 END),
           DecompositionRevision = COALESCE(gross.DecompositionRevision, CASE WHEN active_net.HasActiveNetDemand = 1 THEN 1 ELSE 0 END)
    FROM dbo.IndependentDemand gross
    OUTER APPLY
    (
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.DependentDemand net
            WHERE net.IndependentDemandId = gross.Uid AND net.IsDeleted = 0
        ) THEN 1 ELSE 0 END AS HasActiveNetDemand
    ) active_net;

    /* 已关闭 SO 且没有下游的旧毛需求可证明为作废；有下游时必须人工处理。 */
    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand gross
        JOIN dbo.SalesOrderHeader head ON head.Uid = gross.SourceUid
        WHERE gross.SourceType = N'SO'
          AND gross.IsDeleted = 0
          AND head.IsDeleted = 0
          AND head.DocumentStatus = 9
          AND EXISTS (
              SELECT 1 FROM dbo.DependentDemand net
              WHERE net.IndependentDemandId = gross.Uid AND net.IsDeleted = 0
          )
    )
        THROW 51405, N'A closed sales order has active downstream demand history; manual resolution is required.', 1;

    UPDATE gross
       SET LifecycleStatus = 9,
           LifecycleChangedAt = COALESCE(gross.LifecycleChangedAt, head.ClosedDate, SYSUTCDATETIME()),
           LifecycleChangedBy = COALESCE(gross.LifecycleChangedBy, head.ClosedBy, N'migration'),
           LifecycleChangedReason = COALESCE(gross.LifecycleChangedReason, head.CloseReason, N'历史销售订单已关闭')
    FROM dbo.IndependentDemand gross
    JOIN dbo.SalesOrderHeader head ON head.Uid = gross.SourceUid
    WHERE gross.SourceType = N'SO'
      AND gross.IsDeleted = 0
      AND head.IsDeleted = 0
      AND head.DocumentStatus = 9
      AND NOT EXISTS (
          SELECT 1 FROM dbo.DependentDemand net
          WHERE net.IndependentDemandId = gross.Uid AND net.IsDeleted = 0
      );

    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand gross
        WHERE gross.IsDeleted = 0 AND gross.IsNonStandard = 0 AND gross.ProductId IS NULL
    )
        THROW 51406, N'A standard gross demand cannot be mapped to one product.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand gross
        WHERE gross.IsDeleted = 0 AND gross.IsNonStandard = 1 AND gross.ProductId IS NOT NULL
    )
        THROW 51407, N'A non-standard gross demand must not reference Product.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand gross
        WHERE gross.IsDeleted = 0
          AND gross.SourceType = N'MD'
          AND (gross.SourceUid <> gross.Uid OR gross.SourceLineId <> gross.Uid)
    )
        THROW 51408, N'Manual gross demand self-source mapping is invalid.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand gross
        WHERE gross.SourceType = N'CR'
          AND (gross.SourceUid IS NULL OR gross.SourceLineId IS NULL)
    )
        THROW 51409, N'Complaint replenishment source mapping is not provable.', 1;

    /* ---------- 历史净需求版本、物料和根关系回填 ---------- */
    UPDATE net
       SET LifecycleStatus = COALESCE(net.LifecycleStatus, CASE WHEN net.IsDeleted = 1 THEN 9 ELSE 0 END),
           DecompositionRevision = COALESCE(net.DecompositionRevision, 1),
           DeliveryMode = COALESCE(net.DeliveryMode, 0),
           ProductId = CASE
               WHEN net.IsNonStandard = 0 AND product_match.MatchCount = 1 THEN COALESCE(net.ProductId, product_match.Uid)
               WHEN net.IsNonStandard = 0 THEN net.ProductId
               ELSE NULL
           END,
           MaterialTypeSnapshot = CASE
               WHEN net.IsNonStandard = 0 AND product_match.MatchCount = 1 THEN COALESCE(net.MaterialTypeSnapshot, product_match.MaterialType)
               WHEN net.IsNonStandard = 0 THEN net.MaterialTypeSnapshot
               ELSE NULL
           END,
           PreferredSupplierCode = COALESCE(net.PreferredSupplierCode, net.SupplierCode)
    FROM dbo.DependentDemand net
    OUTER APPLY
    (
        SELECT CONVERT(uniqueidentifier, MIN(CONVERT(char(36), product.Uid))) AS Uid,
               MIN(product.MaterialType) AS MaterialType,
               COUNT(*) AS MatchCount
        FROM dbo.Product product
        WHERE product.Sku = net.Sku
          AND (product.IsDeleted = 0 OR net.IsDeleted = 1)
    ) product_match;

    IF EXISTS (
        SELECT 1
        FROM dbo.DependentDemand net
        WHERE net.IsDeleted = 0 AND net.IsNonStandard = 0 AND net.ProductId IS NULL
    )
        THROW 51410, N'A standard historical net demand cannot be mapped to one product.', 1;

    ;WITH numbered AS
    (
        SELECT Uid,
               ROW_NUMBER() OVER (
                   PARTITION BY IndependentDemandId, DecompositionRevision
                   ORDER BY CASE WHEN BomLevel = 0 THEN 0 ELSE 1 END, BomLevel, CreatedDate, Uid
               ) AS GeneratedLineNumber
        FROM dbo.DependentDemand
    )
    UPDATE net
       SET LineNumber = numbered.GeneratedLineNumber
    FROM dbo.DependentDemand net
    JOIN numbered ON numbered.Uid = net.Uid
    WHERE net.LineNumber IS NULL;

    ;WITH roots AS
    (
        SELECT IndependentDemandId,
               DecompositionRevision,
               CONVERT(uniqueidentifier, MIN(CASE WHEN BomLevel = 0 THEN CONVERT(char(36), Uid) END)) AS RootId,
               SUM(CASE WHEN BomLevel = 0 THEN 1 ELSE 0 END) AS RootCount
        FROM dbo.DependentDemand
        GROUP BY IndependentDemandId, DecompositionRevision
    )
    UPDATE net
       SET RootNetDemandId = COALESCE(net.RootNetDemandId, roots.RootId),
           ParentNetDemandId = CASE
               WHEN net.BomLevel = 1 AND net.ParentNetDemandId IS NULL THEN roots.RootId
               ELSE net.ParentNetDemandId
           END,
           DeliveryTargetNetDemandId = CASE
               WHEN net.BomLevel = 1 AND net.MaterialTypeSnapshot = N'客供件' THEN roots.RootId
               ELSE net.DeliveryTargetNetDemandId
           END,
           DeliveryMode = CASE
               WHEN net.BomLevel = 1 AND net.MaterialTypeSnapshot = N'客供件' THEN 1
               ELSE 0
           END
    FROM dbo.DependentDemand net
    JOIN roots
      ON roots.IndependentDemandId = net.IndependentDemandId
     AND roots.DecompositionRevision = net.DecompositionRevision
    WHERE roots.RootCount = 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.DependentDemand
        WHERE RootNetDemandId IS NULL
    )
        THROW 51411, N'Historical net demand root relationship is not provable.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.DependentDemand
        GROUP BY IndependentDemandId, DecompositionRevision, LineNumber
        HAVING COUNT(*) > 1
    )
        THROW 51412, N'Duplicate net demand revision line exists.', 1;

    /* ---------- 旧 DemandExecutionLink 兼容回填 ---------- */
    UPDATE dbo.DemandExecutionLink
       SET Version = COALESCE(Version, 1),
           IsDeleted = COALESCE(IsDeleted, 0),
           LinkStatus = COALESCE(LinkStatus, CASE WHEN IsDeleted = 1 THEN 8 ELSE 0 END),
           AllocatedQty = COALESCE(AllocatedQty, ExecutedQty, 0),
           CompletedQty = COALESCE(CompletedQty, ReadyQty, 0),
           ExecutedQty = COALESCE(ExecutedQty, AllocatedQty, 0),
           ReadyQty = COALESCE(ReadyQty, CompletedQty, 0),
           ReceivedQty = COALESCE(ReceivedQty, 0);

    IF EXISTS (
        SELECT 1
        FROM dbo.DemandExecutionLink link
        LEFT JOIN dbo.DependentDemand net ON net.Uid = link.DependentDemandId
        WHERE net.Uid IS NULL
    )
        THROW 51413, N'DemandExecutionLink contains an orphaned net demand reference.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.DemandExecutionLink
        WHERE DependentDemandId IS NULL
           OR NULLIF(LTRIM(RTRIM(TargetType)), N'') IS NULL
           OR TargetHeadId IS NULL
           OR NULLIF(LTRIM(RTRIM(TargetNo)), N'') IS NULL
           OR TargetLineId IS NULL
           OR LinkStatus NOT IN (0, 8, 9)
           OR AllocatedQty < 0
           OR CompletedQty < 0
           OR ReceivedQty < 0
    )
        THROW 51414, N'DemandExecutionLink contains an incomplete or invalid target mapping.', 1;

    /* ---------- 提交前业务断言 ---------- */
    IF EXISTS (
        SELECT 1 FROM dbo.IndependentDemand
        WHERE (IsDeleted = 0 AND OriginalQty <= 0)
           OR LifecycleStatus NOT IN (0, 8, 9)
           OR DecompositionStatus NOT IN (0, 1)
           OR DecompositionRevision < 0
    )
        THROW 51415, N'Gross demand quantity or target state is invalid.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.DependentDemand
        WHERE GrossQty <= 0
           OR NetQty < 0
           OR LifecycleStatus NOT IN (0, 8, 9)
           OR DecompositionRevision <= 0
           OR LineNumber <= 0
           OR (DeliveryMode = 0 AND DeliveryTargetNetDemandId IS NOT NULL)
           OR (DeliveryMode = 1 AND (ParentNetDemandId IS NULL OR DeliveryTargetNetDemandId IS NULL))
    )
        THROW 51416, N'Net demand quantity, target state, or delivery relation is invalid.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.Product
        WHERE IsDeleted = 0
          AND MaterialType = N'客供件'
          AND (SupplyType <> N'采购' OR BomVersion <> 0 OR NULLIF(LTRIM(RTRIM(BomNo)), N'') IS NOT NULL)
    )
        THROW 51417, N'Customer-supplied Product must be purchased and terminal.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.Product product
        JOIN dbo.Bom bom ON bom.ParentSku = product.Sku AND bom.IsDeleted = 0
        WHERE product.IsDeleted = 0 AND product.MaterialType = N'客供件'
    )
        THROW 51418, N'Customer-supplied Product must not have an active BOM.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.Bom
        WHERE Numerator <= 0 OR Denominator <= 0 OR ParentSkuId = SubSkuId
    )
        THROW 51419, N'BOM ratio or parent-child relation is invalid.', 1;

    /* ---------- 必填、默认和检查约束 ---------- */
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'BomVersion' AND is_nullable = 1)
        ALTER TABLE dbo.Bom ALTER COLUMN BomVersion int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'ParentSkuId' AND is_nullable = 1)
        ALTER TABLE dbo.Bom ALTER COLUMN ParentSkuId uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'ParentSku' AND is_nullable = 1)
        ALTER TABLE dbo.Bom ALTER COLUMN ParentSku nvarchar(255) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'LineNumber' AND is_nullable = 1)
        ALTER TABLE dbo.Bom ALTER COLUMN LineNumber int NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'LifecycleStatus' AND is_nullable = 1)
        ALTER TABLE dbo.IndependentDemand ALTER COLUMN LifecycleStatus int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'DecompositionStatus' AND is_nullable = 1)
        ALTER TABLE dbo.IndependentDemand ALTER COLUMN DecompositionStatus int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'DecompositionRevision' AND is_nullable = 1)
        ALTER TABLE dbo.IndependentDemand ALTER COLUMN DecompositionRevision int NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'LifecycleStatus' AND is_nullable = 1)
        ALTER TABLE dbo.DependentDemand ALTER COLUMN LifecycleStatus int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'DecompositionRevision' AND is_nullable = 1)
        ALTER TABLE dbo.DependentDemand ALTER COLUMN DecompositionRevision int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'LineNumber' AND is_nullable = 1)
        ALTER TABLE dbo.DependentDemand ALTER COLUMN LineNumber int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'RootNetDemandId' AND is_nullable = 1)
        ALTER TABLE dbo.DependentDemand ALTER COLUMN RootNetDemandId uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'DeliveryMode' AND is_nullable = 1)
        ALTER TABLE dbo.DependentDemand ALTER COLUMN DeliveryMode int NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'Version' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN Version int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'IsDeleted' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN IsDeleted bit NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'DependentDemandId' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN DependentDemandId uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'TargetType' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN TargetType nvarchar(30) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'TargetHeadId' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN TargetHeadId uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'TargetNo' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN TargetNo nvarchar(50) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'TargetLineId' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN TargetLineId uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'LinkStatus' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN LinkStatus int NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'AllocatedQty' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN AllocatedQty decimal(18,4) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'CompletedQty' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN CompletedQty decimal(18,4) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'ReceivedQty' AND is_nullable = 1)
        ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN ReceivedQty decimal(18,4) NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'DF_IndependentDemand_LifecycleStatus')
        ALTER TABLE dbo.IndependentDemand ADD CONSTRAINT DF_IndependentDemand_LifecycleStatus DEFAULT (0) FOR LifecycleStatus;
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'DF_IndependentDemand_DecompositionStatus')
        ALTER TABLE dbo.IndependentDemand ADD CONSTRAINT DF_IndependentDemand_DecompositionStatus DEFAULT (0) FOR DecompositionStatus;
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'DF_IndependentDemand_DecompositionRevision')
        ALTER TABLE dbo.IndependentDemand ADD CONSTRAINT DF_IndependentDemand_DecompositionRevision DEFAULT (0) FOR DecompositionRevision;
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'DF_DependentDemand_LifecycleStatus')
        ALTER TABLE dbo.DependentDemand ADD CONSTRAINT DF_DependentDemand_LifecycleStatus DEFAULT (0) FOR LifecycleStatus;
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'DF_DependentDemand_DeliveryMode')
        ALTER TABLE dbo.DependentDemand ADD CONSTRAINT DF_DependentDemand_DeliveryMode DEFAULT (0) FOR DeliveryMode;

    IF OBJECT_ID(N'dbo.CK_IndependentDemand_DemandQty', N'C') IS NULL
        ALTER TABLE dbo.IndependentDemand WITH CHECK ADD CONSTRAINT CK_IndependentDemand_DemandQty CHECK (IsDeleted = 1 OR OriginalQty > 0);
    IF OBJECT_ID(N'dbo.CK_IndependentDemand_TargetStatuses', N'C') IS NULL
        ALTER TABLE dbo.IndependentDemand WITH CHECK ADD CONSTRAINT CK_IndependentDemand_TargetStatuses CHECK (LifecycleStatus IN (0,8,9) AND DecompositionStatus IN (0,1) AND DecompositionRevision >= 0);
    IF OBJECT_ID(N'dbo.CK_IndependentDemand_StandardProduct', N'C') IS NULL
        ALTER TABLE dbo.IndependentDemand WITH CHECK ADD CONSTRAINT CK_IndependentDemand_StandardProduct CHECK (IsDeleted = 1 OR (IsNonStandard = 0 AND ProductId IS NOT NULL) OR (IsNonStandard = 1 AND ProductId IS NULL));
    IF OBJECT_ID(N'dbo.CK_IndependentDemand_ManualSelfSource', N'C') IS NULL
        ALTER TABLE dbo.IndependentDemand WITH CHECK ADD CONSTRAINT CK_IndependentDemand_ManualSelfSource CHECK (IsDeleted = 1 OR SourceType <> N'MD' OR (SourceUid = Uid AND SourceLineId = Uid));

    IF OBJECT_ID(N'dbo.CK_Product_CustomerSuppliedTerminal', N'C') IS NULL
        ALTER TABLE dbo.Product WITH CHECK ADD CONSTRAINT CK_Product_CustomerSuppliedTerminal CHECK (IsDeleted = 1 OR MaterialType <> N'客供件' OR (SupplyType = N'采购' AND BomVersion = 0 AND NULLIF(LTRIM(RTRIM(BomNo)), N'') IS NULL));
    IF OBJECT_ID(N'dbo.CK_Bom_PositiveRatio', N'C') IS NULL
        ALTER TABLE dbo.Bom WITH CHECK ADD CONSTRAINT CK_Bom_PositiveRatio CHECK (Numerator > 0 AND Denominator > 0);
    IF OBJECT_ID(N'dbo.CK_Bom_ParentDiffersFromChild', N'C') IS NULL
        ALTER TABLE dbo.Bom WITH CHECK ADD CONSTRAINT CK_Bom_ParentDiffersFromChild CHECK (SubSkuId IS NULL OR ParentSkuId <> SubSkuId);

    IF OBJECT_ID(N'dbo.CK_DependentDemand_TargetState', N'C') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT CK_DependentDemand_TargetState CHECK (LifecycleStatus IN (0,8,9) AND DecompositionRevision > 0 AND LineNumber > 0);
    IF OBJECT_ID(N'dbo.CK_DependentDemand_Quantities', N'C') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT CK_DependentDemand_Quantities CHECK (GrossQty > 0 AND NetQty >= 0);
    IF OBJECT_ID(N'dbo.CK_DependentDemand_DeliveryRelation', N'C') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT CK_DependentDemand_DeliveryRelation CHECK ((DeliveryMode = 0 AND DeliveryTargetNetDemandId IS NULL) OR (DeliveryMode = 1 AND ParentNetDemandId IS NOT NULL AND DeliveryTargetNetDemandId IS NOT NULL));

    IF OBJECT_ID(N'dbo.CK_DemandExecutionLink_TargetState', N'C') IS NULL
        ALTER TABLE dbo.DemandExecutionLink WITH CHECK ADD CONSTRAINT CK_DemandExecutionLink_TargetState CHECK (LinkStatus IN (0,8,9) AND AllocatedQty >= 0 AND CompletedQty >= 0 AND ReceivedQty >= 0);

    /* ---------- 唯一性、查询索引与强关系 ---------- */
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'IX_Bom_DemandPlanningLookup')
        CREATE INDEX IX_Bom_DemandPlanningLookup
            ON dbo.Bom(BomNo, BomVersion, LineNumber);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'IX_IndependentDemand_DemandPlanningSearch')
        CREATE INDEX IX_IndependentDemand_DemandPlanningSearch
            ON dbo.IndependentDemand(LifecycleStatus, DecompositionStatus, HopefulDate, Uid);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_GrossDemandLifecycle')
        CREATE INDEX IX_DependentDemand_GrossDemandLifecycle
            ON dbo.DependentDemand(IndependentDemandId, LifecycleStatus, DecompositionRevision);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'IX_DemandExecutionLink_NetDemandStatus')
        CREATE INDEX IX_DemandExecutionLink_NetDemandStatus
            ON dbo.DemandExecutionLink(DependentDemandId, LinkStatus, IsDeleted);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'UX_IndependentDemand_ActiveSourceLine')
        CREATE UNIQUE INDEX UX_IndependentDemand_ActiveSourceLine
            ON dbo.IndependentDemand(SourceType, SourceLineId)
            WHERE IsDeleted = 0 AND LifecycleStatus = 0;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'UX_DependentDemand_GrossRevisionLine')
        CREATE UNIQUE INDEX UX_DependentDemand_GrossRevisionLine
            ON dbo.DependentDemand(IndependentDemandId, DecompositionRevision, LineNumber);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_ParentNetDemandId')
        CREATE INDEX IX_DependentDemand_ParentNetDemandId ON dbo.DependentDemand(ParentNetDemandId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_RootNetDemandId')
        CREATE INDEX IX_DependentDemand_RootNetDemandId ON dbo.DependentDemand(RootNetDemandId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_DeliveryTargetNetDemandId')
        CREATE INDEX IX_DependentDemand_DeliveryTargetNetDemandId ON dbo.DependentDemand(DeliveryTargetNetDemandId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'UX_DemandExecutionLink_ActiveTargetLine')
        CREATE UNIQUE INDEX UX_DemandExecutionLink_ActiveTargetLine
            ON dbo.DemandExecutionLink(DependentDemandId, TargetType, TargetLineId)
            WHERE IsDeleted = 0 AND LinkStatus = 0;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'IX_DemandExecutionLink_Target')
        CREATE INDEX IX_DemandExecutionLink_Target
            ON dbo.DemandExecutionLink(TargetType, TargetHeadId, TargetLineId);

    IF OBJECT_ID(N'dbo.FK_DependentDemand_IndependentDemand', N'F') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT FK_DependentDemand_IndependentDemand FOREIGN KEY (IndependentDemandId) REFERENCES dbo.IndependentDemand(Uid) ON DELETE NO ACTION ON UPDATE NO ACTION;
    IF OBJECT_ID(N'dbo.FK_DependentDemand_Parent', N'F') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT FK_DependentDemand_Parent FOREIGN KEY (ParentNetDemandId) REFERENCES dbo.DependentDemand(Uid) ON DELETE NO ACTION ON UPDATE NO ACTION;
    IF OBJECT_ID(N'dbo.FK_DependentDemand_Root', N'F') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT FK_DependentDemand_Root FOREIGN KEY (RootNetDemandId) REFERENCES dbo.DependentDemand(Uid) ON DELETE NO ACTION ON UPDATE NO ACTION;
    IF OBJECT_ID(N'dbo.FK_DependentDemand_DeliveryTarget', N'F') IS NULL
        ALTER TABLE dbo.DependentDemand WITH CHECK ADD CONSTRAINT FK_DependentDemand_DeliveryTarget FOREIGN KEY (DeliveryTargetNetDemandId) REFERENCES dbo.DependentDemand(Uid) ON DELETE NO ACTION ON UPDATE NO ACTION;
    IF OBJECT_ID(N'dbo.FK_DemandExecutionLink_DependentDemand', N'F') IS NULL
        ALTER TABLE dbo.DemandExecutionLink WITH CHECK ADD CONSTRAINT FK_DemandExecutionLink_DependentDemand FOREIGN KEY (DependentDemandId) REFERENCES dbo.DependentDemand(Uid) ON DELETE NO ACTION ON UPDATE NO ACTION;

    /* 最终断言：约束创建后仍不得出现孤儿或重复活动来源。 */
    IF EXISTS (
        SELECT 1
        FROM dbo.DependentDemand net
        LEFT JOIN dbo.IndependentDemand gross ON gross.Uid = net.IndependentDemandId
        WHERE gross.Uid IS NULL
    )
        THROW 51420, N'Post-migration assertion failed: orphaned net demand.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.IndependentDemand
        WHERE IsDeleted = 0 AND LifecycleStatus = 0
        GROUP BY SourceType, SourceLineId
        HAVING COUNT(*) > 1
    )
        THROW 51421, N'Post-migration assertion failed: duplicate active gross demand source.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
