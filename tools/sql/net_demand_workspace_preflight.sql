/*
    净需求工作台迁移前只读审计。
    仅使用 SELECT/系统目录读取；不得在本脚本中加入 DDL、DML 或测试数据清理。
*/
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT N'DATABASE' AS AuditSection, DB_NAME() AS DatabaseName, SYSDATETIME() AS AuditedAt;

SELECT N'TABLE_COUNTS' AS AuditSection,N'Product' AS TableName,COUNT_BIG(1) AS [RowCount] FROM dbo.Product
UNION ALL SELECT N'TABLE_COUNTS',N'Supplier',COUNT_BIG(1) FROM dbo.Supplier
UNION ALL SELECT N'TABLE_COUNTS',N'SupplierProduct',COUNT_BIG(1) FROM dbo.SupplierProduct
UNION ALL SELECT N'TABLE_COUNTS',N'IndependentDemand',COUNT_BIG(1) FROM dbo.IndependentDemand
UNION ALL SELECT N'TABLE_COUNTS',N'DependentDemand',COUNT_BIG(1) FROM dbo.DependentDemand
UNION ALL SELECT N'TABLE_COUNTS',N'DemandExecutionLink',COUNT_BIG(1) FROM dbo.DemandExecutionLink
UNION ALL SELECT N'TABLE_COUNTS',N'PurchaseOrderHead',COUNT_BIG(1) FROM dbo.PurchaseOrderHead
UNION ALL SELECT N'TABLE_COUNTS',N'PurchaseOrderLine',COUNT_BIG(1) FROM dbo.PurchaseOrderLine;

SELECT N'DUPLICATE_ACTIVE_SUPPLIER_CODE' AS AuditSection,
       LTRIM(RTRIM(SupplierCode)) AS SupplierCode, COUNT_BIG(1) AS MatchCount
FROM dbo.Supplier
WHERE ISNULL(IsDeleted, 0)=0 AND NULLIF(LTRIM(RTRIM(SupplierCode)), N'') IS NOT NULL
GROUP BY LTRIM(RTRIM(SupplierCode))
HAVING COUNT_BIG(1)>1
ORDER BY SupplierCode;

SELECT N'PRODUCT_DEFAULT_SUPPLIER_MATCH' AS AuditSection,
       p.Uid AS ProductId,p.Sku,p.DefaultSupplierCode,COUNT(s.Uid) AS ActiveCodeMatches
FROM dbo.Product p
LEFT JOIN dbo.Supplier s
  ON LTRIM(RTRIM(s.SupplierCode))=LTRIM(RTRIM(p.DefaultSupplierCode)) AND ISNULL(s.IsDeleted,0)=0
WHERE ISNULL(p.IsDeleted,0)=0 AND NULLIF(LTRIM(RTRIM(p.DefaultSupplierCode)),N'') IS NOT NULL
GROUP BY p.Uid,p.Sku,p.DefaultSupplierCode
ORDER BY ActiveCodeMatches,p.Sku;

IF COL_LENGTH(N'dbo.Product',N'DefaultSupplierId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''PRODUCT_DEFAULT_SUPPLIER_UID_ANOMALY'' AS AuditSection,p.Uid AS ProductId,p.Sku,
               p.DefaultSupplierId,p.DefaultSupplierCode,s.SupplierCode AS CurrentSupplierCode
        FROM dbo.Product p
        LEFT JOIN dbo.Supplier s ON s.Uid=p.DefaultSupplierId AND ISNULL(s.IsDeleted,0)=0
        WHERE ISNULL(p.IsDeleted,0)=0 AND p.DefaultSupplierId IS NOT NULL
          AND (s.Uid IS NULL OR (NULLIF(LTRIM(RTRIM(p.DefaultSupplierCode)),N'''') IS NOT NULL
               AND LTRIM(RTRIM(p.DefaultSupplierCode))<>LTRIM(RTRIM(s.SupplierCode))))
        ORDER BY p.Sku;';

SELECT N'ACTIVE_NET_DEMAND_RELATION_ANOMALY' AS AuditSection,
       nd.Uid AS NetDemandId,nd.IndependentDemandId AS GrossDemandId,nd.LineNumber,
       nd.ParentNetDemandId,nd.RootNetDemandId,nd.DeliveryTargetNetDemandId,nd.DeliveryMode
FROM dbo.DependentDemand nd
LEFT JOIN dbo.IndependentDemand gd ON gd.Uid=nd.IndependentDemandId
LEFT JOIN dbo.DependentDemand root ON root.Uid=nd.RootNetDemandId
LEFT JOIN dbo.DependentDemand target ON target.Uid=nd.DeliveryTargetNetDemandId
WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0
  AND (gd.Uid IS NULL OR root.Uid IS NULL
       OR (nd.DeliveryMode=0 AND nd.DeliveryTargetNetDemandId IS NOT NULL)
       OR (nd.DeliveryMode=1 AND (nd.ParentNetDemandId IS NULL OR target.Uid IS NULL)))
ORDER BY nd.IndependentDemandId,nd.LineNumber;

IF OBJECT_ID(N'dbo.NetDemandAllocation',N'U') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''ACTIVE_ALLOCATION_ANOMALY'' AS AuditSection,nd.Uid AS NetDemandId,nd.NetQty,
               COUNT(a.Uid) AS ActiveAllocationCount,COALESCE(SUM(a.PlannedQty),0) AS ActivePlannedQty
        FROM dbo.DependentDemand nd
        LEFT JOIN dbo.NetDemandAllocation a
          ON a.NetDemandId=nd.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
        WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0
        GROUP BY nd.Uid,nd.NetQty
        HAVING COUNT(a.Uid)<>1 OR COALESCE(SUM(a.PlannedQty),0)<>nd.NetQty
        ORDER BY nd.Uid;';

SELECT N'ACTIVE_LINK_MAPPING' AS AuditSection,l.Uid AS LinkId,l.DependentDemandId,
       l.TargetType,l.TargetHeadId,l.TargetNo,l.TargetLineId,l.AllocatedQty,
       CASE WHEN po.Uid IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS TargetPurchaseLineExists
FROM dbo.DemandExecutionLink l
LEFT JOIN dbo.PurchaseOrderLine po ON po.Uid=l.TargetLineId
WHERE l.LinkStatus=0 AND l.IsDeleted=0
ORDER BY l.DependentDemandId,l.TargetNo,l.TargetLineId;

SELECT N'PURCHASE_QTY_COLUMN' AS AuditSection,t.name AS SqlType,c.max_length,c.precision,c.scale,c.is_nullable
FROM sys.columns c
JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND c.name=N'QTY';

SELECT N'PURCHASE_LINE_UNIQUE_INDEX' AS AuditSection,i.name AS IndexName,i.is_unique,i.has_filter,i.filter_definition,
       STRING_AGG(CASE WHEN ic.is_included_column=0 THEN c.name END,N',')
         WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND i.is_unique=1
GROUP BY i.name,i.is_unique,i.has_filter,i.filter_definition
ORDER BY i.name;

SELECT N'PURCHASE_SOURCE_QUALITY' AS AuditSection,h.Uid,h.OrderNo,h.SupplierCode,h.SalesOrderNo,h.BoNo,
       COUNT(l.Uid) AS LineCount
FROM dbo.PurchaseOrderHead h
LEFT JOIN dbo.PurchaseOrderLine l ON l.PurchaseOrderId=h.Uid AND ISNULL(l.IsDeleted,0)=0
WHERE ISNULL(h.IsDeleted,0)=0
GROUP BY h.Uid,h.OrderNo,h.SupplierCode,h.SalesOrderNo,h.BoNo
ORDER BY h.OrderNo,h.Uid;

SELECT N'ADDITIVE_COLUMN_FACTS' AS AuditSection,OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName,
       OBJECT_NAME(c.object_id) AS TableName,c.name AS ColumnName,t.name AS SqlType,c.precision,c.scale,c.is_nullable
FROM sys.columns c
JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id IN (OBJECT_ID(N'dbo.Product'),OBJECT_ID(N'dbo.DependentDemand'),
                      OBJECT_ID(N'dbo.DemandExecutionLink'),OBJECT_ID(N'dbo.PurchaseOrderHead'),
                      OBJECT_ID(N'dbo.PurchaseOrderLine'),OBJECT_ID(N'dbo.NetDemandAllocation'))
ORDER BY TableName,c.column_id;
