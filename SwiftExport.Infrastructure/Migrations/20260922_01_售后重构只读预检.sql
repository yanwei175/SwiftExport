-- 只读预检：只创建连接内临时表，不修改业务数据。
-- 停止旧客户端写入后执行；任何来源或执行事实都须交由所属领域处理。
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.AfterSalesHeader',N'U') IS NULL OR OBJECT_ID(N'dbo.AfterSalesLine',N'U') IS NULL
    THROW 51000,N'售后头行表不存在，请先核对目标数据库。',1;
SELECT DB_NAME() AS 数据库, COUNT_BIG(*) AS 售后头数 FROM dbo.AfterSalesHeader;
SELECT COUNT_BIG(*) AS 售后行数 FROM dbo.AfterSalesLine;
SELECT h.Uid,h.AfterSalesNo,h.DocumentStatus,h.ConfirmationStatus,
       (SELECT COUNT(*) FROM dbo.AfterSalesLine l WHERE l.HeaderId=h.Uid) AS 明细数
FROM dbo.AfterSalesHeader h;
CREATE TABLE #AfterSalesTargets(Uid uniqueidentifier PRIMARY KEY);
INSERT #AfterSalesTargets SELECT Uid FROM dbo.AfterSalesHeader UNION SELECT Uid FROM dbo.AfterSalesLine;
IF OBJECT_ID(N'dbo.GrossDemand',N'U') IS NOT NULL
BEGIN
    EXEC(N'SELECT * FROM dbo.GrossDemand WHERE SourceType=N''CR'' OR SourceUid IN(SELECT Uid FROM #AfterSalesTargets) OR SourceLineId IN(SELECT Uid FROM #AfterSalesTargets);
    INSERT #AfterSalesTargets SELECT g.Uid FROM dbo.GrossDemand g WHERE g.SourceType=N''CR'' AND NOT EXISTS(SELECT 1 FROM #AfterSalesTargets t WHERE t.Uid=g.Uid);');
END;
-- 使用当前数据库实际存在的逻辑引用列检查净需求、采购、PackingList、执行及库存记录。
DECLARE @table sysname,@column sysname,@sql nvarchar(max);
DECLARE refs CURSOR LOCAL FAST_FORWARD FOR
SELECT t.name,c.name FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND c.system_type_id=36
  AND c.name IN(N'SourceUid',N'SourceLineId',N'DemandSourceUid',N'GrossDemandId')
  AND t.name NOT IN(N'AfterSalesHeader',N'AfterSalesLine');
OPEN refs; FETCH NEXT FROM refs INTO @table,@column;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @sql=N'SELECT N'''+REPLACE(@table,N'''',N'''''')+N''' AS 关联表,N'''+@column+N''' AS 关联列,COUNT_BIG(*) AS 关联数 FROM dbo.'
        +QUOTENAME(@table)+N' WHERE '+QUOTENAME(@column)+N' IN(SELECT Uid FROM #AfterSalesTargets);';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM refs INTO @table,@column;
END;
CLOSE refs; DEALLOCATE refs;
DROP TABLE #AfterSalesTargets;
-- 业务操作日志是追溯记录，后续清理脚本不会删除。
IF OBJECT_ID(N'dbo.BusinessOperationLog',N'U') IS NOT NULL
    EXEC(N'SELECT COUNT_BIG(*) AS 保留审计记录数 FROM dbo.BusinessOperationLog WHERE Module=N''AfterSales'';');
