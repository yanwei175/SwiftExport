SET NOCOUNT ON;

/* 1. 列出数据库中仍直接引用 NetDemandAllocation 表的外键。 */
SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
       OBJECT_NAME(fk.parent_object_id) AS ParentTable,
       fk.name AS ForeignKeyName
FROM sys.foreign_keys fk
WHERE fk.referenced_object_id=OBJECT_ID(N'dbo.NetDemandAllocation')
ORDER BY ParentSchema,ParentTable,ForeignKeyName;

/* 2. 列出仍在 SQL 模块文本中引用 Allocation 的对象。 */
SELECT OBJECT_SCHEMA_NAME(m.object_id) AS ObjectSchema,
       OBJECT_NAME(m.object_id) AS ObjectName,
       o.type_desc AS ObjectType
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id=m.object_id
WHERE m.definition LIKE N'%NetDemandAllocation%'
ORDER BY ObjectSchema,ObjectName;

/* 3. 检查采购链路是否已允许新记录不带历史 Allocation。 */
SELECT c.name AS ColumnName,c.is_nullable,t.name AS DataType
FROM sys.columns c
JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.DemandExecutionLink')
  AND c.name IN(N'NetDemandId',N'NetDemandAllocationId');

/* 4. 列出采购相关表上仍以 Allocation 列为键或包含列的索引。 */
SELECT OBJECT_NAME(i.object_id) AS TableName,i.name AS IndexName,c.name AS ColumnName,ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.DemandExecutionLink'),OBJECT_ID(N'dbo.PurchaseOrderLine'))
  AND c.name IN(N'NetDemandAllocationId',N'DeliveryTargetAllocationId')
ORDER BY TableName,IndexName,ic.key_ordinal;

/* 5. 历史值统计仅用于确认保留规模，不会修改数据。 */
IF OBJECT_ID(N'dbo.DemandExecutionLink',N'U') IS NOT NULL
    SELECT COUNT_BIG(*) AS LinkCount,
           SUM(CASE WHEN NetDemandAllocationId IS NULL THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS LinkWithoutAllocationCount
    FROM dbo.DemandExecutionLink;

IF OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryTargetAllocationId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS PurchaseLineCount,
               SUM(CASE WHEN DeliveryTargetAllocationId IS NULL THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS LineWithoutAllocationCount
        FROM dbo.PurchaseOrderLine;';
