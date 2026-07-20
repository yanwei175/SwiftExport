/*
  在旧数据库中执行。除临时表外不创建对象，不修改任何源库数据或结构。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedSourceDatabaseName sysname=N'dv';
DECLARE @PlannedTargetDatabaseName sysname=N'dv_next';

IF NULLIF(@ExpectedSourceDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedSourceDatabaseName
    THROW 52200,N'当前数据库不是 @ExpectedSourceDatabaseName 指定的源库。',1;
IF NULLIF(@PlannedTargetDatabaseName,N'') IS NULL
    THROW 52201,N'必须填写 @PlannedTargetDatabaseName。',1;
IF @ExpectedSourceDatabaseName=@PlannedTargetDatabaseName
    THROW 52202,N'源库与目标库不能同名。',1;

CREATE TABLE #Issues
(
    Severity nvarchar(20) NOT NULL,
    TableName sysname NULL,
    Problem nvarchar(400) NOT NULL,
    ProblemCount bigint NULL
);

DECLARE @Whitelist TABLE(RowNo int NOT NULL PRIMARY KEY,TableName sysname NOT NULL UNIQUE);
INSERT @Whitelist(RowNo,TableName) VALUES
 (10,N'Customer'),(20,N'Supplier'),(30,N'ProductCategory'),(40,N'ProductColor'),(50,N'Product'),
 (60,N'CustomerProduct'),(70,N'ProductDetails'),(80,N'CustomerPoConfig'),
 (110,N'SysDicCurrency'),(120,N'SysDictType'),(130,N'SysDictData'),(140,N'SysRoles'),
 (150,N'SysUser'),(160,N'SysUser_Roles'),(170,N'SysViewRoute');

INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N'BLOCKER',w.TableName,N'源库缺少白名单表',NULL
FROM @Whitelist w
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(w.TableName),N'U') IS NULL;

INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N'BLOCKER',w.TableName,N'缺少 Uid、Version 或 IsDeleted 基础列',NULL
FROM @Whitelist w
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(w.TableName),N'U') IS NOT NULL
  AND
  (
      COL_LENGTH(N'dbo.'+w.TableName,N'Uid') IS NULL OR
      COL_LENGTH(N'dbo.'+w.TableName,N'Version') IS NULL OR
      COL_LENGTH(N'dbo.'+w.TableName,N'IsDeleted') IS NULL
  );

DECLARE @RequiredColumns TABLE(TableName sysname NOT NULL,ColumnName sysname NOT NULL,PRIMARY KEY(TableName,ColumnName));
INSERT @RequiredColumns(TableName,ColumnName) VALUES
 (N'Customer',N'CustomerCode'),(N'Supplier',N'SupplierCode'),
 (N'ProductCategory',N'CategoryCode'),(N'ProductColor',N'ColorCodeUnique'),
 (N'Product',N'Sku'),(N'Product',N'DefaultSupplierId'),
 (N'CustomerProduct',N'CustomerId'),(N'CustomerProduct',N'CustomerSku'),(N'CustomerProduct',N'SkuId'),
 (N'ProductDetails',N'ProductSKU'),
 (N'CustomerPoConfig',N'CustomerCode'),(N'CustomerPoConfig',N'SupplierCode'),
 (N'SysDicCurrency',N'CurrencyCode'),(N'SysDictType',N'DictCode'),(N'SysDictData',N'DictCode'),
 (N'SysRoles',N'RoleName'),(N'SysUser',N'UserName'),
 (N'SysUser_Roles',N'UserId'),(N'SysUser_Roles',N'RoleId'),(N'SysViewRoute',N'TargetViewKey');
INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N'BLOCKER',r.TableName,N'缺少活动代码所需列：'+r.ColumnName,NULL
FROM @RequiredColumns r
WHERE COL_LENGTH(N'dbo.'+r.TableName,r.ColumnName) IS NULL;

DECLARE @sql nvarchar(max)=N'';
SELECT @sql+=N'
INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N''BLOCKER'',N'''+REPLACE(w.TableName,'''','''''')+N''',N''Uid 为空'',COUNT_BIG(*)
FROM dbo.'+QUOTENAME(w.TableName)+N' WHERE Uid IS NULL HAVING COUNT_BIG(*)>0;
INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N''BLOCKER'',N'''+REPLACE(w.TableName,'''','''''')+N''',N''Uid 重复'',COUNT_BIG(*)
FROM (SELECT Uid FROM dbo.'+QUOTENAME(w.TableName)+N' GROUP BY Uid HAVING COUNT_BIG(*)>1)d HAVING COUNT_BIG(*)>0;
INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N''BLOCKER'',N'''+REPLACE(w.TableName,'''','''''')+N''',N''Version 为空或不大于 0'',COUNT_BIG(*)
FROM dbo.'+QUOTENAME(w.TableName)+N' WHERE Version IS NULL OR Version<=0 HAVING COUNT_BIG(*)>0;
INSERT #Issues(Severity,TableName,Problem,ProblemCount)
SELECT N''BLOCKER'',N'''+REPLACE(w.TableName,'''','''''')+N''',N''IsDeleted 为空'',COUNT_BIG(*)
FROM dbo.'+QUOTENAME(w.TableName)+N' WHERE IsDeleted IS NULL HAVING COUNT_BIG(*)>0;'
FROM @Whitelist w
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(w.TableName),N'U') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+w.TableName,N'Uid') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+w.TableName,N'Version') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+w.TableName,N'IsDeleted') IS NOT NULL;
EXEC sys.sp_executesql @sql;

/* 活动业务键必须满足目标唯一索引。 */
INSERT #Issues SELECT N'BLOCKER',N'Product',N'活动 Sku 为空或重复',COUNT_BIG(*)
FROM
(
    SELECT Sku FROM dbo.Product WHERE ISNULL(IsDeleted,0)=0
    GROUP BY Sku HAVING NULLIF(LTRIM(RTRIM(Sku)),N'') IS NULL OR COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'Customer',N'活动 CustomerCode 为空或重复',COUNT_BIG(*)
FROM
(
    SELECT CustomerCode FROM dbo.Customer WHERE ISNULL(IsDeleted,0)=0
    GROUP BY CustomerCode HAVING NULLIF(LTRIM(RTRIM(CustomerCode)),N'') IS NULL OR COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'Supplier',N'活动 SupplierCode 为空或重复',COUNT_BIG(*)
FROM
(
    SELECT SupplierCode FROM dbo.Supplier WHERE ISNULL(IsDeleted,0)=0
    GROUP BY SupplierCode HAVING NULLIF(LTRIM(RTRIM(SupplierCode)),N'') IS NULL OR COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'CustomerProduct',N'活动 CustomerId + CustomerSku 重复',COUNT_BIG(*)
FROM
(
    SELECT CustomerId,CustomerSku FROM dbo.CustomerProduct WHERE ISNULL(IsDeleted,0)=0
    GROUP BY CustomerId,CustomerSku HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'CustomerPoConfig',N'活动 CustomerCode + SupplierCode 重复',COUNT_BIG(*)
FROM
(
    SELECT CustomerCode,SupplierCode FROM dbo.CustomerPoConfig WHERE ISNULL(IsDeleted,0)=0
    GROUP BY CustomerCode,SupplierCode HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'ProductCategory',N'活动 CategoryCode 重复',COUNT_BIG(*)
FROM
(
    SELECT CategoryCode FROM dbo.ProductCategory WHERE ISNULL(IsDeleted,0)=0 AND CategoryCode IS NOT NULL
    GROUP BY CategoryCode HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'ProductColor',N'活动 ColorCodeUnique 重复',COUNT_BIG(*)
FROM
(
    SELECT ColorCodeUnique FROM dbo.ProductColor WHERE ISNULL(IsDeleted,0)=0
    GROUP BY ColorCodeUnique HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysDicCurrency',N'活动 CurrencyCode 重复',COUNT_BIG(*)
FROM
(
    SELECT CurrencyCode FROM dbo.SysDicCurrency WHERE ISNULL(IsDeleted,0)=0
    GROUP BY CurrencyCode HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysDictType',N'活动 DictCode 重复',COUNT_BIG(*)
FROM
(
    SELECT DictCode FROM dbo.SysDictType WHERE ISNULL(IsDeleted,0)=0
    GROUP BY DictCode HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysRoles',N'活动 RoleName 重复',COUNT_BIG(*)
FROM
(
    SELECT RoleName FROM dbo.SysRoles WHERE ISNULL(IsDeleted,0)=0
    GROUP BY RoleName HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysUser',N'活动 UserName 重复',COUNT_BIG(*)
FROM
(
    SELECT UserName FROM dbo.SysUser WHERE ISNULL(IsDeleted,0)=0
    GROUP BY UserName HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysUser_Roles',N'活动 UserId + RoleId 重复',COUNT_BIG(*)
FROM
(
    SELECT UserId,RoleId FROM dbo.SysUser_Roles WHERE ISNULL(IsDeleted,0)=0
    GROUP BY UserId,RoleId HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;
INSERT #Issues SELECT N'BLOCKER',N'SysViewRoute',N'活动 TargetViewKey 重复',COUNT_BIG(*)
FROM
(
    SELECT TargetViewKey FROM dbo.SysViewRoute WHERE ISNULL(IsDeleted,0)=0
    GROUP BY TargetViewKey HAVING COUNT_BIG(*)>1
)d HAVING COUNT_BIG(*)>0;

/* 新增 Uid 关系允许无法匹配，迁入新库后保持 NULL 并报告。 */
INSERT #Issues
SELECT N'WARNING',N'ProductDetails',N'ProductSKU 不能唯一匹配活动 Product；目标 ProductId 将保持 NULL',COUNT_BIG(*)
FROM dbo.ProductDetails d
OUTER APPLY
(
    SELECT COUNT_BIG(*) Matches FROM dbo.Product p
    WHERE p.Sku=d.ProductSKU AND ISNULL(p.IsDeleted,0)=0
)m
WHERE m.Matches<>1
HAVING COUNT_BIG(*)>0;

INSERT #Issues
SELECT N'WARNING',N'CustomerPoConfig',N'CustomerCode 不能唯一匹配活动 Customer；目标 CustomerId 将保持 NULL',COUNT_BIG(*)
FROM dbo.CustomerPoConfig c
OUTER APPLY
(
    SELECT COUNT_BIG(*) Matches FROM dbo.Customer x
    WHERE x.CustomerCode=c.CustomerCode AND ISNULL(x.IsDeleted,0)=0
)m
WHERE NULLIF(LTRIM(RTRIM(c.CustomerCode)),N'') IS NOT NULL AND m.Matches<>1
HAVING COUNT_BIG(*)>0;

INSERT #Issues
SELECT N'WARNING',N'CustomerPoConfig',N'SupplierCode 不能唯一匹配活动 Supplier；目标 SupplierId 将保持 NULL',COUNT_BIG(*)
FROM dbo.CustomerPoConfig c
OUTER APPLY
(
    SELECT COUNT_BIG(*) Matches FROM dbo.Supplier x
    WHERE x.SupplierCode=c.SupplierCode AND ISNULL(x.IsDeleted,0)=0
)m
WHERE NULLIF(LTRIM(RTRIM(c.SupplierCode)),N'') IS NOT NULL AND m.Matches<>1
HAVING COUNT_BIG(*)>0;

IF DB_ID(@PlannedTargetDatabaseName) IS NOT NULL
    INSERT #Issues VALUES(N'BLOCKER',NULL,N'计划目标数据库已经存在；创建脚本不会覆盖它',NULL);

SELECT Severity,TableName,Problem,ProblemCount
FROM #Issues
ORDER BY CASE Severity WHEN N'BLOCKER' THEN 0 ELSE 1 END,TableName,Problem;

CREATE TABLE #SourceSnapshot(TableName sysname NOT NULL,[RowCount] bigint NOT NULL,UidChecksum int NULL);
SET @sql=N'';
SELECT @sql+=N'
INSERT #SourceSnapshot(TableName,[RowCount],UidChecksum)
SELECT N'''+REPLACE(w.TableName,'''','''''')+N''',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(Uid))
FROM dbo.'+QUOTENAME(w.TableName)+N';'
FROM @Whitelist w
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(w.TableName),N'U') IS NOT NULL
  AND COL_LENGTH(N'dbo.'+w.TableName,N'Uid') IS NOT NULL;
EXEC sys.sp_executesql @sql;

SELECT DB_NAME() SourceDatabaseName,@PlannedTargetDatabaseName PlannedTargetDatabaseName,SYSUTCDATETIME() CheckedAtUtc;
SELECT TableName,[RowCount],UidChecksum FROM #SourceSnapshot ORDER BY TableName;

IF EXISTS(SELECT 1 FROM #Issues WHERE Severity=N'BLOCKER')
    THROW 52203,N'源库预检存在 BLOCKER，禁止继续创建和迁移。',1;
