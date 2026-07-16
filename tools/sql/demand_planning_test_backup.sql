/*
    需求计划测试库迁移前备份。
    仅允许在约定的 dv 测试库执行；每次生成带 UTC 时间戳的独立备份表。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'dv'
    THROW 51500, N'Refusing to create demand planning test backups outside database dv.', 1;

DECLARE @Suffix sysname = REPLACE(REPLACE(REPLACE(CONVERT(nvarchar(27), SYSUTCDATETIME(), 126), N'-', N''), N':', N''), N'.', N'');
DECLARE @Sql nvarchar(max) = N'';

SELECT @Sql += N'SELECT * INTO dbo.' + QUOTENAME(N'DPBackup_' + @Suffix + N'_' + source_table.TableName)
    + N' FROM dbo.' + QUOTENAME(source_table.TableName) + N';' + CHAR(13) + CHAR(10)
FROM (VALUES
    (N'Product'),
    (N'Bom'),
    (N'IndependentDemand'),
    (N'DependentDemand'),
    (N'SalesOrderHeader'),
    (N'SalesOrderLine')
) source_table(TableName)
WHERE OBJECT_ID(N'dbo.' + source_table.TableName, N'U') IS NOT NULL;

IF OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NOT NULL
    SET @Sql += N'SELECT * INTO dbo.' + QUOTENAME(N'DPBackup_' + @Suffix + N'_DemandExecutionLink')
        + N' FROM dbo.DemandExecutionLink;' + CHAR(13) + CHAR(10);

EXEC sys.sp_executesql @Sql;

SELECT DB_NAME() AS DatabaseName,
       @Suffix AS BackupSuffix,
       N'DPBackup_' + @Suffix + N'_%' AS BackupTablePattern,
       SYSUTCDATETIME() AS CompletedAtUtc;
