/*
  在新目标数据库中执行：从同实例旧库复制 7 张系统白名单表。
  SysUser.PasswordHash 按原哈希值迁移，不生成、不打印任何明文密码。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowWhitelistMigration bit=1;
DECLARE @SourceDatabaseName sysname=N'dv';
DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next';

IF @AllowWhitelistMigration<>1 THROW 52500,N'系统白名单迁移门禁未开启。',1;
IF NULLIF(@SourceDatabaseName,N'') IS NULL OR DB_ID(@SourceDatabaseName) IS NULL
    THROW 52501,N'源数据库不存在。',1;
IF NULLIF(@ExpectedTargetDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedTargetDatabaseName
    THROW 52502,N'当前数据库不是指定目标数据库。',1;
IF @SourceDatabaseName=@ExpectedTargetDatabaseName
    THROW 52503,N'源数据库与目标数据库不能同名。',1;
IF @@TRANCOUNT<>0 THROW 52504,N'当前连接存在未结束事务。',1;

DECLARE @Tables TABLE(RowNo int NOT NULL PRIMARY KEY,TableName sysname NOT NULL UNIQUE);
INSERT @Tables(RowNo,TableName) VALUES
 (10,N'SysDicCurrency'),(20,N'SysDictType'),(30,N'SysDictData'),(40,N'SysRoles'),
 (50,N'SysUser'),(60,N'SysUser_Roles'),(70,N'SysViewRoute');

DECLARE @sourcePrefix nvarchar(258)=QUOTENAME(@SourceDatabaseName)+N'.dbo.';
DECLARE @sql nvarchar(max)=N'';

SELECT @sql+=N'
IF OBJECT_ID(N'''+REPLACE(@sourcePrefix+QUOTENAME(t.TableName),'''','''''')+N''',N''U'') IS NULL
    THROW 52510,N''源库缺少白名单表 '+REPLACE(t.TableName,'''','''''')+N'。'',1;
IF OBJECT_ID(N''dbo.'+REPLACE(t.TableName,'''','''''')+N''',N''U'') IS NOT NULL
    THROW 52511,N''目标白名单表 '+REPLACE(t.TableName,'''','''''')+N' 已存在，禁止重复迁移。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' WHERE Uid IS NULL)
    THROW 52512,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 存在空 Uid。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' GROUP BY Uid HAVING COUNT_BIG(*)>1)
    THROW 52513,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 存在重复 Uid。'',1;
IF EXISTS(SELECT 1 FROM '+@sourcePrefix+QUOTENAME(t.TableName)+N' WHERE Version IS NULL OR Version<=0 OR IsDeleted IS NULL)
    THROW 52514,N''源表 '+REPLACE(t.TableName,'''','''''')+N' 的 Version/IsDeleted 不符合基线。'',1;'
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
