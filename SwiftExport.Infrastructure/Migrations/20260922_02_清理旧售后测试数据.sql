-- 仅供用户确认可清理的旧售后测试库；手工执行，不得由应用启动自动运行。
-- 不删除来源需求、物料需求、采购、交付、库存或审计。存在任何来源/执行引用即阻断。
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 THROW 51000,N'请在无外部事务的连接独立执行。',1;
IF OBJECT_ID(N'dbo.AfterSalesHeader',N'U') IS NULL OR OBJECT_ID(N'dbo.AfterSalesLine',N'U') IS NULL
    THROW 51000,N'售后头行表不存在。',1;
-- 新模型不得被历史清理脚本再次清空。
IF COL_LENGTH(N'dbo.AfterSalesHeader',N'IssueDescription') IS NOT NULL
    THROW 51000,N'已是新单投诉模型，禁止执行旧数据清理。',1;
BEGIN TRY
    BEGIN TRANSACTION;
    CREATE TABLE #AfterSalesTargets(Uid uniqueidentifier PRIMARY KEY);
    INSERT #AfterSalesTargets SELECT Uid FROM dbo.AfterSalesHeader WITH(TABLOCKX,HOLDLOCK)
        UNION SELECT Uid FROM dbo.AfterSalesLine WITH(TABLOCKX,HOLDLOCK);
    IF OBJECT_ID(N'dbo.GrossDemand',N'U') IS NULL
        THROW 51000,N'缺少来源需求表，无法证明安全清理，已阻断。',1;
    EXEC(N'IF EXISTS(SELECT 1 FROM dbo.GrossDemand WITH(UPDLOCK,HOLDLOCK) WHERE SourceType=N''CR'')
        THROW 51000,N''存在CR来源事实（包括历史记录），请由来源领域合法处置后重新预检，不得级联删除。'',1;');
    DECLARE @table sysname,@column sysname,@sql nvarchar(max);
    DECLARE refs CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name,c.name FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
    WHERE t.schema_id=SCHEMA_ID(N'dbo') AND c.system_type_id=36
      AND c.name IN(N'SourceUid',N'SourceLineId',N'DemandSourceUid',N'GrossDemandId')
      AND t.name NOT IN(N'AfterSalesHeader',N'AfterSalesLine');
    OPEN refs; FETCH NEXT FROM refs INTO @table,@column;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' WITH(UPDLOCK,HOLDLOCK) WHERE '
            +QUOTENAME(@column)+N' IN(SELECT Uid FROM #AfterSalesTargets)) THROW 51000,N''存在来源或执行关联：'
            +REPLACE(@table,N'''',N'''''')+N'，清理已阻断。'',1;';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM refs INTO @table,@column;
    END;
    CLOSE refs; DEALLOCATE refs;
    DELETE FROM dbo.AfterSalesLine;
    DELETE FROM dbo.AfterSalesHeader;
    DROP TABLE #AfterSalesTargets;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
