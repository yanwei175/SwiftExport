/* 只读审计：执行结果用于人工确认回填范围，脚本不写共享数据库。 */
SELECT c.name AS ColumnName,t.name AS TypeName,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Warehouse') ORDER BY c.column_id;
SELECT c.name AS ColumnName,t.name AS TypeName,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Inventory') ORDER BY c.column_id;
SELECT c.name AS ColumnName,t.name AS TypeName,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.InventoryTransaction') ORDER BY c.column_id;
SELECT i.WhseCode,i.Sku,i.OnhandQty,COUNT(*) OVER(PARTITION BY i.WhseCode,i.Sku) AS DuplicateCount FROM dbo.Inventory i WHERE ISNULL(i.IsDeleted,0)=0 AND ISNULL(i.OnhandQty,0)<>0 ORDER BY i.WhseCode,i.Sku;
SELECT t.WhseCode,t.Sku,SUM(ISNULL(t.[Change],ISNULL(t.InQty,0)-ISNULL(t.OutQty,0))) AS LedgerNetQty FROM dbo.InventoryTransaction t WHERE ISNULL(t.IsDeleted,0)=0 GROUP BY t.WhseCode,t.Sku ORDER BY t.WhseCode,t.Sku;
