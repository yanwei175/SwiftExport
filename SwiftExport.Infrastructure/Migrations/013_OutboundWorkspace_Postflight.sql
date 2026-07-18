SET NOCOUNT ON;
SELECT t.name AS TableName,c.name AS ColumnName,ty.name AS TypeName,c.is_nullable AS IsNullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.name IN(N'OutboundHead',N'OutboundLine') ORDER BY t.name,c.column_id;
SELECT SourcePackingListId,DocumentType,WarehouseSiteId,CommandId,COUNT(*) AS DuplicateCount
FROM dbo.OutboundHead WHERE CommandId IS NOT NULL GROUP BY SourcePackingListId,DocumentType,WarehouseSiteId,CommandId HAVING COUNT(*)>1;
SELECT CommandItemId,COUNT(*) AS DuplicateCount FROM dbo.OutboundLine WHERE CommandItemId IS NOT NULL GROUP BY CommandItemId HAVING COUNT(*)>1;
