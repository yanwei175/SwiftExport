/* 只读：确认遗留 PackingList 主键类型和可证明 Allocation 覆盖率。 */
SET NOCOUNT ON;
SELECT t.name AS TableName,c.column_id,c.name AS ColumnName,ty.name AS TypeName,c.max_length,c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.name IN(N'PackingListHead',N'PackingListLine') ORDER BY t.name,c.column_id;
IF OBJECT_ID(N'dbo.PackingListLine',N'U') IS NOT NULL
BEGIN
    DECLARE @sql nvarchar(max)=N'SELECT COUNT(*) AS ActiveLineCount';
    IF COL_LENGTH(N'dbo.PackingListLine',N'NetDemandAllocationId') IS NOT NULL
        SET @sql+=N',SUM(CASE WHEN NetDemandAllocationId IS NOT NULL THEN 1 ELSE 0 END) AS ProvenAllocationCount';
    SET @sql+=N' FROM dbo.PackingListLine WHERE '+CASE WHEN COL_LENGTH(N'dbo.PackingListLine',N'IsDeleted') IS NOT NULL THEN N'ISNULL(IsDeleted,0)=0' ELSE N'1=1' END+N';';
    EXEC sys.sp_executesql @sql;
END;
