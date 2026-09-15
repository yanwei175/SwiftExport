/* 只读后检：目录结构、目标表初始行数、例外表行数及关键孤立关系。 */
SET NOCOUNT ON;
DECLARE @Targets TABLE(TableName sysname PRIMARY KEY);
INSERT @Targets VALUES
(N'SupplierProduct'),(N'ProductMaster'),(N'ProductPackage'),(N'Bom'),(N'SysDicCurrencyExchange'),(N'DocumentSequence'),
(N'SalesOrderHeader'),(N'SalesOrderLine'),(N'GrossDemand'),(N'NetDemand'),(N'NetDemandAllocation'),(N'NetDemandPlanCommandLog'),(N'DemandExecutionLink'),
(N'PurchaseOrderHead'),(N'PurchaseOrderLine'),(N'SupplierProgressReport'),(N'WarehouseSite'),(N'InventoryBalance'),(N'InventoryLedgerEntry'),
(N'InboundDocument'),(N'InboundDocumentLine'),(N'OutboundDocument'),(N'OutboundDocumentLine'),(N'PackingListHead'),(N'PackingListLine'),(N'BusinessOperationLog');

SELECT CASE WHEN t.object_id IS NULL THEN N'BLOCKER' ELSE N'OK' END Result,x.TableName
FROM @Targets x LEFT JOIN sys.tables t ON t.name=x.TableName AND SCHEMA_NAME(t.schema_id)=N'dbo' ORDER BY x.TableName;

SELECT t.name TableName,c.name ColumnName,ty.name SqlType,c.max_length,c.precision,c.scale,c.is_nullable,dc.definition DefaultDefinition
FROM sys.tables t JOIN @Targets x ON x.TableName=t.name
JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
ORDER BY t.name,c.column_id;

SELECT t.name TableName,i.name IndexName,i.is_unique,i.has_filter,i.filter_definition
FROM sys.tables t JOIN @Targets x ON x.TableName=t.name JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
ORDER BY t.name,i.name;
SELECT t.name TableName,cc.name ConstraintName,cc.definition
FROM sys.tables t JOIN @Targets x ON x.TableName=t.name JOIN sys.check_constraints cc ON cc.parent_object_id=t.object_id
ORDER BY t.name,cc.name;

SELECT t.name TableName,SUM(p.rows) RowCount
FROM sys.tables t JOIN @Targets x ON x.TableName=t.name JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1)
GROUP BY t.name ORDER BY t.name;

SELECT N'BLOCKER' Result,N'ProductDetails.ProductId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.ProductDetails d LEFT JOIN dbo.Product p ON p.Uid=d.ProductId WHERE d.ProductId IS NOT NULL AND p.Uid IS NULL HAVING COUNT_BIG(*)>0;
SELECT N'BLOCKER' Result,N'CustomerPoConfig.CustomerId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig c LEFT JOIN dbo.Customer x ON x.Uid=c.CustomerId WHERE c.CustomerId IS NOT NULL AND x.Uid IS NULL HAVING COUNT_BIG(*)>0;
SELECT N'BLOCKER' Result,N'CustomerPoConfig.SupplierId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig c LEFT JOIN dbo.Supplier x ON x.Uid=c.SupplierId WHERE c.SupplierId IS NOT NULL AND x.Uid IS NULL HAVING COUNT_BIG(*)>0;

SELECT name LegacyTableStillPresent FROM sys.tables
WHERE SCHEMA_NAME(schema_id)=N'dbo' AND name IN
(N'IndependentDemand',N'DependentDemand',N'FollowUpLog',N'Warehouse',N'Inventory',N'InventoryTransaction',N'PurchaseReceiveHead',N'PurchaseReceiveLine',N'OutboundHead',N'OutboundLine',N'PackingListDocument',N'PackingListContributionLine');
