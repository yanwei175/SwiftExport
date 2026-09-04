/*
  在新目标数据库中执行。只读取旧库和新库，不修改任何持久化对象或业务数据。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SourceDatabaseName sysname=N'dv';
DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next';

IF NULLIF(@SourceDatabaseName,N'') IS NULL OR DB_ID(@SourceDatabaseName) IS NULL
    THROW 52700,N'源数据库不存在。',1;
IF NULLIF(@ExpectedTargetDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedTargetDatabaseName
    THROW 52701,N'当前数据库不是指定目标数据库。',1;
IF @SourceDatabaseName=@ExpectedTargetDatabaseName
    THROW 52702,N'源数据库与目标数据库不能同名。',1;

DECLARE @Whitelist TABLE(RowNo int NOT NULL PRIMARY KEY,TableName sysname NOT NULL UNIQUE);
INSERT @Whitelist(RowNo,TableName) VALUES
 (10,N'Customer'),(20,N'Supplier'),(30,N'ProductCategory'),(40,N'ProductColor'),(50,N'Product'),
 (60,N'CustomerProduct'),(70,N'ProductDetails'),(80,N'CustomerPoConfig'),
 (110,N'SysDicCurrency'),(120,N'SysDictType'),(130,N'SysDictData'),(140,N'SysRoles'),
 (150,N'SysUser'),(160,N'SysUser_Roles'),(170,N'SysViewRoute');

CREATE TABLE #Comparison
(
    TableName sysname NOT NULL,
    SourceRowCount bigint NULL,
    TargetRowCount bigint NULL,
    SourceDataChecksum int NULL,
    TargetDataChecksum int NULL
);

DECLARE @TableName sysname;
DECLARE @ColumnList nvarchar(max);
DECLARE @metadataSql nvarchar(max);
DECLARE @sql nvarchar(max);
DECLARE whitelist_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName FROM @Whitelist ORDER BY RowNo;

OPEN whitelist_cursor;
FETCH NEXT FROM whitelist_cursor INTO @TableName;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @ColumnList=NULL;
    SET @metadataSql=N'
SELECT @ColumnList=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N'','') WITHIN GROUP(ORDER BY c.column_id)
FROM '+QUOTENAME(@SourceDatabaseName)+N'.sys.tables t
JOIN '+QUOTENAME(@SourceDatabaseName)+N'.sys.schemas s ON s.schema_id=t.schema_id
JOIN '+QUOTENAME(@SourceDatabaseName)+N'.sys.columns c ON c.object_id=t.object_id
WHERE s.name=N''dbo'' AND t.name=@TableName
  AND NOT
  (
      (@TableName=N''ProductDetails'' AND c.name=N''ProductId'') OR
      (@TableName=N''CustomerPoConfig'' AND c.name IN(N''CustomerId'',N''SupplierId'')) OR
      (@TableName=N''CustomerProduct'' AND c.name IN(N''CustomerSkuUnit'',N''Currency''))
  );';
    EXEC sys.sp_executesql @metadataSql,N'@TableName sysname,@ColumnList nvarchar(max) OUTPUT',@TableName,@ColumnList OUTPUT;

    IF @ColumnList IS NULL
    BEGIN
        INSERT #Comparison(TableName) VALUES(@TableName);
    END
    ELSE
    BEGIN
        SET @sql=N'
INSERT #Comparison(TableName,SourceRowCount,TargetRowCount,SourceDataChecksum,TargetDataChecksum)
SELECT @TableName,s.RowCountValue,t.RowCountValue,s.ChecksumValue,t.ChecksumValue
FROM
(
    SELECT COUNT_BIG(*) RowCountValue,CHECKSUM_AGG(BINARY_CHECKSUM('+@ColumnList+N')) ChecksumValue
    FROM '+QUOTENAME(@SourceDatabaseName)+N'.dbo.'+QUOTENAME(@TableName)+N'
)s
CROSS JOIN
(
    SELECT COUNT_BIG(*) RowCountValue,CHECKSUM_AGG(BINARY_CHECKSUM('+@ColumnList+N')) ChecksumValue
    FROM dbo.'+QUOTENAME(@TableName)+N'
)t;';
        EXEC sys.sp_executesql @sql,N'@TableName sysname',@TableName;
    END;

    FETCH NEXT FROM whitelist_cursor INTO @TableName;
END;
CLOSE whitelist_cursor;
DEALLOCATE whitelist_cursor;

SELECT CASE
           WHEN SourceRowCount=TargetRowCount
            AND ISNULL(SourceDataChecksum,0)=ISNULL(TargetDataChecksum,0) THEN N'PASS'
           ELSE N'BLOCKER'
       END Result,
       TableName,SourceRowCount,TargetRowCount,SourceDataChecksum,TargetDataChecksum
FROM #Comparison
ORDER BY TableName;

SELECT
    CASE
        WHEN c.name=N'Unit'
         AND TYPE_NAME(c.user_type_id)=N'nvarchar'
         AND c.max_length=200
         AND c.is_nullable=1 THEN N'PASS'
        ELSE N'BLOCKER'
    END Result,
    N'Product.Unit 必须为 nullable nvarchar(100)' Problem
FROM (VALUES(1)) expected(Value)
LEFT JOIN sys.columns c
  ON c.object_id=OBJECT_ID(N'dbo.Product')
 AND c.name=N'Unit';

SELECT N'WARNING' Result,N'Product.Unit 不在启用的 Unit 字典中' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.Product p
WHERE NULLIF(LTRIM(RTRIM(p.Unit)),N'') IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SysDictData d
      WHERE d.DictCode=N'Unit'
        AND d.IsEnabled=1
        AND ISNULL(d.IsDeleted,0)=0
        AND LTRIM(RTRIM(d.DataNameEn))=LTRIM(RTRIM(p.Unit))
  )
HAVING COUNT_BIG(*)>0;

SELECT
    CASE
        WHEN COL_LENGTH(N'dbo.CustomerProduct',N'CustomerSkuUnit') IS NULL
         AND COL_LENGTH(N'dbo.CustomerProduct',N'Currency') IS NULL THEN N'PASS'
        ELSE N'BLOCKER'
    END Result,
    N'CustomerProduct 不得保留 CustomerSkuUnit 或 Currency' Problem;

SELECT
    CASE WHEN NOT EXISTS
    (
        SELECT 1 FROM dbo.SysDictType WHERE DictCode IN(N'ProductCategory',N'ProductColor')
        UNION ALL
        SELECT 1 FROM dbo.SysDictData WHERE DictCode IN(N'ProductCategory',N'ProductColor')
    ) THEN N'PASS' ELSE N'BLOCKER' END Result,
    N'ProductCategory/ProductColor 不得保留通用字典记录' Problem;

SELECT
    CASE WHEN NOT EXISTS
    (
        SELECT required.DictCode
        FROM (VALUES(N'Incoterm'),(N'PaymentMethod'),(N'ShippedBy')) required(DictCode)
        WHERE NOT EXISTS
        (
            SELECT 1 FROM dbo.SysDictType actual
            WHERE actual.DictCode=required.DictCode AND actual.IsSystem=1 AND ISNULL(actual.IsDeleted,0)=0
        )
    ) THEN N'PASS' ELSE N'BLOCKER' END Result,
    N'客户与销售订单系统字典类型必须存在并受保护' Problem;

SELECT N'BLOCKER' Result,N'ProductDetails.ProductId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.ProductDetails d
LEFT JOIN dbo.Product p ON p.Uid=d.ProductId
WHERE d.ProductId IS NOT NULL AND p.Uid IS NULL
HAVING COUNT_BIG(*)>0;
SELECT N'WARNING' Result,N'ProductDetails.ProductId 未回填' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.ProductDetails
WHERE ProductId IS NULL
HAVING COUNT_BIG(*)>0;

SELECT N'BLOCKER' Result,N'CustomerPoConfig.CustomerId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig c
LEFT JOIN dbo.Customer x ON x.Uid=c.CustomerId
WHERE c.CustomerId IS NOT NULL AND x.Uid IS NULL
HAVING COUNT_BIG(*)>0;
SELECT N'BLOCKER' Result,N'CustomerPoConfig.SupplierId 孤立' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig c
LEFT JOIN dbo.Supplier x ON x.Uid=c.SupplierId
WHERE c.SupplierId IS NOT NULL AND x.Uid IS NULL
HAVING COUNT_BIG(*)>0;
SELECT N'WARNING' Result,N'CustomerPoConfig.CustomerId 未回填' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig
WHERE NULLIF(LTRIM(RTRIM(CustomerCode)),N'') IS NOT NULL AND CustomerId IS NULL
HAVING COUNT_BIG(*)>0;
SELECT N'WARNING' Result,N'CustomerPoConfig.SupplierId 未回填' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig
WHERE NULLIF(LTRIM(RTRIM(SupplierCode)),N'') IS NOT NULL AND SupplierId IS NULL
HAVING COUNT_BIG(*)>0;

DECLARE @TargetTables TABLE(TableName sysname NOT NULL PRIMARY KEY);
INSERT @TargetTables(TableName) VALUES
 (N'SupplierProduct'),(N'ProductMaster'),(N'ProductNeutral'),(N'ProductPackage'),(N'Bom'),(N'SysDicCurrencyExchange'),
 (N'DocumentSequence'),(N'SalesOrderHeader'),(N'SalesOrderLine'),(N'GrossDemand'),(N'NetDemand'),
 (N'NetDemandAllocation'),(N'NetDemandPlanCommandLog'),(N'DemandExecutionLink'),(N'PurchaseOrderHead'),
 (N'PurchaseOrderLine'),(N'SupplierProgressReport'),(N'WarehouseSite'),(N'InventoryBalance'),
 (N'InventoryLedgerEntry'),(N'InboundDocument'),(N'InboundDocumentLine'),(N'OutboundDocument'),
 (N'OutboundDocumentLine'),(N'PackingListDocument'),(N'PackingListContributionLine'),(N'BusinessOperationLog');

SELECT N'BLOCKER' Result,t.TableName,N'目标业务表不存在' Problem
FROM @TargetTables t
WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(t.TableName),N'U') IS NULL
ORDER BY t.TableName;

SELECT DB_NAME() TargetDatabaseName,@SourceDatabaseName SourceDatabaseName,SYSUTCDATETIME() CheckedAtUtc;
SELECT t.name TableName,p.rows [RowCount]
FROM sys.tables t
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1)
WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
ORDER BY t.name;

SELECT t.name TableName,c.name ColumnName,TYPE_NAME(c.user_type_id) DataType,c.max_length,c.precision,c.scale,c.is_nullable
FROM sys.tables t
JOIN sys.columns c ON c.object_id=t.object_id
WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
ORDER BY t.name,c.column_id;

SELECT OBJECT_NAME(i.object_id) TableName,i.name IndexName,i.is_unique,i.has_filter,i.filter_definition
FROM sys.indexes i
JOIN sys.tables t ON t.object_id=i.object_id
WHERE SCHEMA_NAME(t.schema_id)=N'dbo' AND i.index_id>0
ORDER BY OBJECT_NAME(i.object_id),i.name;

SELECT OBJECT_NAME(cc.parent_object_id) TableName,cc.name ConstraintName,cc.definition
FROM sys.check_constraints cc
ORDER BY OBJECT_NAME(cc.parent_object_id),cc.name;
