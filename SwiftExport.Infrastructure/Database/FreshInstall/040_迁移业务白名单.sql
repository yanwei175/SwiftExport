/*
  在新目标数据库中执行：从同实例旧库复制 8 张业务白名单表。
  源数据库只出现在 SELECT 中，不执行任何源库 DDL/DML。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowWhitelistMigration bit=1;
DECLARE @SourceDatabaseName sysname=N'dv';
DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next';

IF @AllowWhitelistMigration<>1 THROW 52400,N'业务白名单迁移门禁未开启。',1;
IF NULLIF(@SourceDatabaseName,N'') IS NULL OR DB_ID(@SourceDatabaseName) IS NULL
    THROW 52401,N'源数据库不存在。',1;
IF NULLIF(@ExpectedTargetDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedTargetDatabaseName
    THROW 52402,N'当前数据库不是指定目标数据库。',1;
IF @SourceDatabaseName=@ExpectedTargetDatabaseName
    THROW 52403,N'源数据库与目标数据库不能同名。',1;
IF @@TRANCOUNT<>0 THROW 52404,N'当前连接存在未结束事务。',1;

DECLARE @Tables TABLE(RowNo int NOT NULL PRIMARY KEY,TableName sysname NOT NULL UNIQUE);
INSERT @Tables(RowNo,TableName) VALUES
 (10,N'Customer'),(20,N'Supplier'),(30,N'ProductCategory'),(40,N'ProductColor'),
 (50,N'Product'),(60,N'CustomerProduct'),(70,N'ProductDetails'),(80,N'CustomerPoConfig');

DECLARE @sourcePrefix nvarchar(258)=QUOTENAME(@SourceDatabaseName)+N'.dbo.';
DECLARE @sql nvarchar(max)=N'';

SELECT @sql+=N'
IF OBJECT_ID(N'''+REPLACE(@sourcePrefix+QUOTENAME(t.TableName),'''','''''')+N''',N''U'') IS NULL
    THROW 52410,N''源库缺少白名单表 '+REPLACE(t.TableName,'''','''''')+N'。'',1;
IF OBJECT_ID(N''dbo.'+REPLACE(t.TableName,'''','''''')+N''',N''U'') IS NOT NULL
    THROW 52411,N''目标白名单表 '+REPLACE(t.TableName,'''','''''')+N' 已存在，禁止重复迁移。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' WHERE Uid IS NULL)
    THROW 52412,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 存在空 Uid。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' GROUP BY Uid HAVING COUNT_BIG(*)>1)
    THROW 52413,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 存在重复 Uid。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' WHERE Version IS NULL OR Version<=0 OR IsDeleted IS NULL)
    THROW 52414,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 的 Version/IsDeleted 不符合基线。'',1;'
FROM @Tables t;
EXEC sys.sp_executesql @sql;

BEGIN TRY
    BEGIN TRAN;

    SET @sql=N'';
    SELECT @sql+=N'
SELECT * INTO dbo.'+QUOTENAME(t.TableName)+N' FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N';
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ALTER COLUMN Uid uniqueidentifier NOT NULL;
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ALTER COLUMN Version int NOT NULL;
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ALTER COLUMN IsDeleted bit NOT NULL;
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ADD CONSTRAINT '+QUOTENAME(N'PK_'+t.TableName)+N' PRIMARY KEY CLUSTERED(Uid);
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ADD CONSTRAINT '+QUOTENAME(N'DF_'+t.TableName+N'_Version')+N' DEFAULT(1) FOR Version;
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ADD CONSTRAINT '+QUOTENAME(N'DF_'+t.TableName+N'_IsDeleted')+N' DEFAULT(0) FOR IsDeleted;
ALTER TABLE dbo.'+QUOTENAME(t.TableName)+N' ADD CONSTRAINT '+QUOTENAME(N'CK_'+t.TableName+N'_Version')+N' CHECK(Version>0);'
    FROM @Tables t
    ORDER BY t.RowNo;
    EXEC sys.sp_executesql @sql;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;

SET @sql=N'';
SELECT @sql+=N'
SELECT N'''+REPLACE(t.TableName,'''','''''')+N''' TableName,COUNT_BIG(*) [RowCount],CHECKSUM_AGG(BINARY_CHECKSUM(Uid)) UidChecksum
FROM dbo.'+QUOTENAME(t.TableName)+N';'
FROM @Tables t ORDER BY t.RowNo;
EXEC sys.sp_executesql @sql;
