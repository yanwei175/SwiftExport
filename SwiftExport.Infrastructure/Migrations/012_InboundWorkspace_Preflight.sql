/* 只读 preflight：执行前确认既有收货表实际结构与遗留来源可证明性；不写入数据库。 */
SET NOCOUNT ON;
SELECT t.name AS TableName,c.column_id,c.name AS ColumnName,ty.name AS TypeName,c.max_length,c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name IN (N'PurchaseReceiveHead',N'PurchaseReceiveLine',N'InventoryTransaction',N'Warehouse') ORDER BY t.name,c.column_id;
SELECT COUNT(*) AS LegacyHeadCount FROM dbo.PurchaseReceiveHead WHERE ISNULL(IsDeleted,0)=0 AND DocumentType IS NULL;
SELECT COUNT(*) AS LegacyLineWithoutProvenSource FROM dbo.PurchaseReceiveLine l JOIN dbo.PurchaseReceiveHead h ON h.Uid=l.PurchaseReceiveHeadId WHERE ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0 AND h.DocumentType IS NULL AND (l.PoLineId IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine p WHERE p.Uid=l.PoLineId));
SELECT i.name AS IndexName,OBJECT_NAME(i.object_id) AS TableName FROM sys.indexes i WHERE i.object_id IN (OBJECT_ID(N'dbo.Inventory'),OBJECT_ID(N'dbo.InventoryTransaction'),OBJECT_ID(N'dbo.Warehouse')) ORDER BY TableName,IndexName;
