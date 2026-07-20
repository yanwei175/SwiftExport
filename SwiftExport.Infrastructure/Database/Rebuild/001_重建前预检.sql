/*
  SwiftExport 目标模型重建前只读预检。
  本脚本不创建对象、不修改数据；任何 Result=BLOCKER 的结果都必须先人工处理。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Exceptions TABLE(TableName sysname NOT NULL PRIMARY KEY, BusinessKey nvarchar(400) NULL);
INSERT @Exceptions(TableName,BusinessKey) VALUES
 (N'CustomerProduct',N'CustomerId,CustomerSku'),(N'CustomerPoConfig',N'CustomerCode,SupplierCode'),
 (N'Customer',N'CustomerCode'),(N'Product',N'Sku'),(N'ProductCategory',NULL),(N'ProductColor',NULL),
 (N'ProductDetails',N'ProductSKU'),(N'Supplier',N'SupplierCode'),(N'SysDicCurrency',NULL),
 (N'SysDictData',NULL),(N'SysDictType',NULL),(N'SysRoles',NULL),(N'SysUser',N'UserName'),
 (N'SysUser_Roles',NULL),(N'SysViewRoute',NULL);

SELECT N'BLOCKER' AS Result,e.TableName,N'例外表不存在' AS Problem
FROM @Exceptions e WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(e.TableName),N'U') IS NULL;

SELECT N'BLOCKER' AS Result,e.TableName,N'例外表缺少 Uid 列' AS Problem
FROM @Exceptions e
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(e.TableName),N'U') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+e.TableName,N'Uid') IS NULL;

DECLARE @sql nvarchar(max)=N'';
SELECT @sql += N'
SELECT N''BLOCKER'' Result,N'''+REPLACE(TableName,'''','''''')+N''' TableName,N''Uid 为空'' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.'+QUOTENAME(TableName)+N' WHERE Uid IS NULL HAVING COUNT_BIG(*)>0;
SELECT N''BLOCKER'',N'''+REPLACE(TableName,'''','''''')+N''',N''Uid 重复'',COUNT_BIG(*)
FROM (SELECT Uid FROM dbo.'+QUOTENAME(TableName)+N' GROUP BY Uid HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;'
FROM @Exceptions
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(TableName),N'U') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+TableName,N'Uid') IS NOT NULL;
EXEC sys.sp_executesql @sql;

IF OBJECT_ID(N'dbo.Product',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Product',N'Sku') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'Product' TableName,N'Sku 为空或空白' Problem,COUNT_BIG(*) ProblemCount
    FROM dbo.Product WHERE NULLIF(LTRIM(RTRIM(Sku)),N'') IS NULL HAVING COUNT_BIG(*)>0;
    SELECT N'BLOCKER' Result,N'Product',N'Sku 重复',COUNT_BIG(*)
    FROM (SELECT Sku FROM dbo.Product WHERE ISNULL(IsDeleted,0)=0 GROUP BY Sku HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;

IF OBJECT_ID(N'dbo.ProductDetails',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Product',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'ProductDetails' TableName,N'ProductSKU 无唯一 Product 匹配' Problem,COUNT_BIG(*) ProblemCount
    FROM dbo.ProductDetails d
    OUTER APPLY(SELECT COUNT_BIG(*) Matches FROM dbo.Product p WHERE p.Sku=d.ProductSKU AND ISNULL(p.IsDeleted,0)=0)m
    WHERE m.Matches<>1 HAVING COUNT_BIG(*)>0;
END;

IF OBJECT_ID(N'dbo.Customer',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'Customer' TableName,N'CustomerCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT CustomerCode FROM dbo.Customer WHERE ISNULL(IsDeleted,0)=0 GROUP BY CustomerCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.Supplier',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'Supplier' TableName,N'SupplierCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT SupplierCode FROM dbo.Supplier WHERE ISNULL(IsDeleted,0)=0 GROUP BY SupplierCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.CustomerProduct',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'CustomerProduct' TableName,N'活动 CustomerId + CustomerSku 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT CustomerId,CustomerSku FROM dbo.CustomerProduct WHERE ISNULL(IsDeleted,0)=0 GROUP BY CustomerId,CustomerSku HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.CustomerPoConfig',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'CustomerPoConfig' TableName,N'活动 CustomerCode + SupplierCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT CustomerCode,SupplierCode FROM dbo.CustomerPoConfig WHERE ISNULL(IsDeleted,0)=0 GROUP BY CustomerCode,SupplierCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.ProductCategory',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'ProductCategory' TableName,N'活动 CategoryCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT CategoryCode FROM dbo.ProductCategory WHERE ISNULL(IsDeleted,0)=0 AND CategoryCode IS NOT NULL GROUP BY CategoryCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.ProductColor',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'ProductColor' TableName,N'活动 ColorCodeUnique 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT ColorCodeUnique FROM dbo.ProductColor WHERE ISNULL(IsDeleted,0)=0 GROUP BY ColorCodeUnique HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysDicCurrency',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysDicCurrency' TableName,N'活动 CurrencyCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT CurrencyCode FROM dbo.SysDicCurrency WHERE ISNULL(IsDeleted,0)=0 GROUP BY CurrencyCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysDictType',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysDictType' TableName,N'活动 DictCode 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT DictCode FROM dbo.SysDictType WHERE ISNULL(IsDeleted,0)=0 GROUP BY DictCode HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysRoles',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysRoles' TableName,N'活动 RoleName 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT RoleName FROM dbo.SysRoles WHERE ISNULL(IsDeleted,0)=0 GROUP BY RoleName HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysUser',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysUser' TableName,N'活动 UserName 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT UserName FROM dbo.SysUser WHERE ISNULL(IsDeleted,0)=0 GROUP BY UserName HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysUser_Roles',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysUser_Roles' TableName,N'活动 UserId + RoleId 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT UserId,RoleId FROM dbo.SysUser_Roles WHERE ISNULL(IsDeleted,0)=0 GROUP BY UserId,RoleId HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.SysViewRoute',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'SysViewRoute' TableName,N'活动 TargetViewKey 重复' Problem,COUNT_BIG(*) ProblemCount
    FROM (SELECT TargetViewKey FROM dbo.SysViewRoute WHERE ISNULL(IsDeleted,0)=0 GROUP BY TargetViewKey HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
END;
IF OBJECT_ID(N'dbo.CustomerPoConfig',N'U') IS NOT NULL
BEGIN
    SELECT N'BLOCKER' Result,N'CustomerPoConfig' TableName,N'CustomerCode 无唯一 Customer 匹配' Problem,COUNT_BIG(*) ProblemCount
    FROM dbo.CustomerPoConfig c
    OUTER APPLY(SELECT COUNT_BIG(*) Matches FROM dbo.Customer x WHERE x.CustomerCode=c.CustomerCode AND ISNULL(x.IsDeleted,0)=0)m
    WHERE NULLIF(LTRIM(RTRIM(c.CustomerCode)),N'') IS NOT NULL AND m.Matches<>1 HAVING COUNT_BIG(*)>0;
    SELECT N'BLOCKER' Result,N'CustomerPoConfig' TableName,N'SupplierCode 无唯一 Supplier 匹配' Problem,COUNT_BIG(*) ProblemCount
    FROM dbo.CustomerPoConfig c
    OUTER APPLY(SELECT COUNT_BIG(*) Matches FROM dbo.Supplier x WHERE x.SupplierCode=c.SupplierCode AND ISNULL(x.IsDeleted,0)=0)m
    WHERE NULLIF(LTRIM(RTRIM(c.SupplierCode)),N'') IS NOT NULL AND m.Matches<>1 HAVING COUNT_BIG(*)>0;
END;

SELECT DB_NAME() DatabaseName,SUSER_SNAME() LoginName,SYSUTCDATETIME() CheckedAtUtc;
SELECT e.TableName,p.rows AS [RowCount]
FROM @Exceptions e
LEFT JOIN sys.tables t ON t.name=e.TableName AND SCHEMA_NAME(t.schema_id)=N'dbo'
LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1)
ORDER BY e.TableName;

SELECT N'INFO' Result,t.name TableName,N'不在例外或目标白名单中；只报告，不自动删除' Problem
FROM sys.tables t
WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
AND t.name NOT IN
(N'CustomerProduct',N'CustomerPoConfig',N'Customer',N'Product',N'ProductCategory',N'ProductColor',N'ProductDetails',N'Supplier',N'SysDicCurrency',N'SysDictData',N'SysDictType',N'SysRoles',N'SysUser',N'SysUser_Roles',N'SysViewRoute',
 N'SupplierProduct',N'ProductMaster',N'ProductPackage',N'Bom',N'SysDicCurrencyExchange',N'DocumentSequence',N'SalesOrderHeader',N'SalesOrderLine',N'GrossDemand',N'NetDemand',N'NetDemandAllocation',N'NetDemandPlanCommandLog',N'DemandExecutionLink',N'PurchaseOrderHead',N'PurchaseOrderLine',N'SupplierProgressReport',N'WarehouseSite',N'InventoryBalance',N'InventoryLedgerEntry',N'InboundDocument',N'InboundDocumentLine',N'OutboundDocument',N'OutboundDocumentLine',N'PackingListDocument',N'PackingListContributionLine',N'BusinessOperationLog',
 N'IndependentDemand',N'DependentDemand',N'FollowUpLog',N'Warehouse',N'Inventory',N'InventoryTransaction',N'PurchaseReceiveHead',N'PurchaseReceiveLine',N'OutboundHead',N'OutboundLine',N'PackingListHead',N'PackingListLine')
ORDER BY t.name;
