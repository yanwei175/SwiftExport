-- 只读人工预检：不连接数据库、不修复历史，不自动执行后续增量脚本。
-- 先人工核对真实表名；不将旧 OutboundHead/OutboundLine 猜测成新实体表。
SELECT expected.TableName,expected.ColumnName,TYPE_NAME(c.user_type_id) AS ActualType,
       c.max_length,c.precision,c.scale,c.is_nullable,
       CASE
           WHEN expected.TableName=N'OutboundDocument' AND expected.ColumnName=N'SourcePackingListId' AND c.column_id IS NULL
               AND COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NOT NULL THEN N'正常：旧字段已移除，核对新触发字段类型'
           WHEN expected.TableName=N'OutboundDocument' AND expected.ColumnName=N'SourcePackingListId' AND c.column_id IS NOT NULL
               AND COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NOT NULL THEN N'阻断：新旧触发字段同时存在，禁止自动合并'
           WHEN expected.TableName=N'OutboundDocument' AND expected.ColumnName=N'Remark' AND c.column_id IS NULL THEN N'待增量：033 将添加可空 nvarchar(500)'
           WHEN c.column_id IS NULL THEN N'缺字段：部署前需核对' ELSE N'存在：核对类型及约束' END AS Result
FROM (VALUES
    (N'OutboundDocument',N'Uid'),(N'OutboundDocument',N'Version'),(N'OutboundDocument',N'IsDeleted'),
    (N'OutboundDocument',N'DocumentNo'),(N'OutboundDocument',N'DocumentType'),(N'OutboundDocument',N'PostingStatus'),
    (N'OutboundDocument',N'LifecycleStatus'),(N'OutboundDocument',N'WarehouseSiteId'),
    (N'OutboundDocument',N'WarehouseCodeSnapshot'),(N'OutboundDocument',N'WarehouseNameSnapshot'),
    (N'OutboundDocument',N'ShipmentDate'),(N'OutboundDocument',N'Remark'),
    (N'OutboundDocument',N'TriggerPackingListId'),(N'OutboundDocument',N'SourcePackingListId'),
    (N'OutboundDocument',N'CommandId'),(N'OutboundDocument',N'PostedDate'),(N'OutboundDocument',N'PostedBy'),
    (N'OutboundDocumentLine',N'Uid'),(N'OutboundDocumentLine',N'Version'),(N'OutboundDocumentLine',N'IsDeleted'),
    (N'OutboundDocumentLine',N'OutboundDocumentId'),(N'OutboundDocumentLine',N'LineNumber'),
    (N'OutboundDocumentLine',N'ProductId'),(N'OutboundDocumentLine',N'SkuSnapshot'),
    (N'OutboundDocumentLine',N'DescriptionSnapshot'),(N'OutboundDocumentLine',N'UnitSnapshot'),
    (N'OutboundDocumentLine',N'Quantity'),(N'OutboundDocumentLine',N'CommandItemId'),(N'OutboundDocumentLine',N'OriginalLineId'),
    (N'InventoryBalance',N'OnHandQty'),(N'InventoryBalance',N'WarehouseSiteId'),(N'InventoryBalance',N'ProductId'),
    (N'InventoryLedgerEntry',N'CommandItemId'),(N'InventoryLedgerEntry',N'TransactionType'),
    (N'BusinessOperationLog',N'ChangedFieldsJson'),(N'BusinessOperationLog',N'RelatedObjectsJson')
) expected(TableName,ColumnName)
LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+expected.TableName) AND c.name=expected.ColumnName
ORDER BY expected.TableName,expected.ColumnName;

-- 历史被移除的业务字段只保留数据库历史值，人工新单不再写入；非空约束会阻挡新草稿。
SELECT OBJECT_NAME(c.object_id) AS TableName,c.name,TYPE_NAME(c.user_type_id) AS TypeName,c.max_length,c.is_nullable,dc.definition AS DefaultValue
FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE c.object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'))
ORDER BY TableName,c.column_id;
SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted
FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'),OBJECT_ID(N'dbo.InventoryLedgerEntry'));
SELECT OBJECT_NAME(i.object_id) AS TableName,i.name,i.is_unique,i.is_disabled,i.filter_definition,c.name AS ColumnName,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'),OBJECT_ID(N'dbo.InventoryBalance'),OBJECT_ID(N'dbo.InventoryLedgerEntry'),OBJECT_ID(N'dbo.BusinessOperationLog'))
ORDER BY TableName,i.name,ic.key_ordinal;
SELECT OBJECT_NAME(parent_id) AS TableName,name,OBJECT_DEFINITION(object_id) AS TriggerDefinition
FROM sys.triggers WHERE parent_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'));
-- 物理外键只按系统目录报告，不根据单号、SKU 或相似字段猜测关联。
SELECT fk.name AS ForeignKeyName,OBJECT_NAME(fkc.parent_object_id) AS ChildTable,
       child.name AS ChildColumn,OBJECT_NAME(fkc.referenced_object_id) AS ParentTable,
       parent.name AS ParentColumn,fk.is_disabled,fk.is_not_trusted
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns child ON child.object_id=fkc.parent_object_id AND child.column_id=fkc.parent_column_id
JOIN sys.columns parent ON parent.object_id=fkc.referenced_object_id AND parent.column_id=fkc.referenced_column_id
WHERE fkc.parent_object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'))
   OR fkc.referenced_object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.OutboundDocumentLine'));

-- 缺少事实字段时不将历史检查未运行解释为“无风险”。
IF COL_LENGTH(N'dbo.OutboundDocument',N'DocumentNo') IS NOT NULL
    AND COL_LENGTH(N'dbo.OutboundDocumentLine',N'CommandItemId') IS NOT NULL
    AND COL_LENGTH(N'dbo.InventoryLedgerEntry',N'SourceLineId') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT DocumentNo,COUNT_BIG(*) AS DuplicateCount FROM dbo.OutboundDocument GROUP BY DocumentNo HAVING COUNT_BIG(*)>1;
        SELECT CommandItemId,COUNT_BIG(*) AS DuplicateCount FROM dbo.OutboundDocumentLine GROUP BY CommandItemId HAVING COUNT_BIG(*)>1;
        SELECT l.Uid AS OrphanLineId,l.OutboundDocumentId FROM dbo.OutboundDocumentLine l
            LEFT JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE h.Uid IS NULL;
        SELECT h.Uid,h.DocumentNo,h.PostingStatus,l.Uid AS LineId,l.ProductId,l.Quantity,
               ledger.LedgerCount,ledger.NetDelta
        FROM dbo.OutboundDocument h JOIN dbo.OutboundDocumentLine l ON l.OutboundDocumentId=h.Uid
        OUTER APPLY(SELECT COUNT_BIG(*) AS LedgerCount,SUM(e.QuantityDelta) AS NetDelta
            FROM dbo.InventoryLedgerEntry e WHERE e.SourceDocumentType=N''OutboundDocument'' AND e.SourceDocumentId=h.Uid AND e.SourceLineId=l.Uid) ledger
        WHERE h.IsDeleted=0 AND l.IsDeleted=0 AND ((h.PostingStatus IN(1,8) AND ledger.LedgerCount=0) OR (h.PostingStatus=0 AND ledger.LedgerCount>0));
        SELECT WarehouseSiteId,ProductId,OnHandQty FROM dbo.InventoryBalance WHERE IsDeleted=0 AND OnHandQty<0;';
END
ELSE SELECT N'阻断：真实事实字段不足，历史检查未运行；先完成表结构核对。' AS Result;

-- 人工验收：余额 WarehouseSiteId+ProductId 唯一且无软删；流水 CommandItemId 唯一；
-- 日志命令唯一/JSON 约束、库存序列沿用 031；不得把手工类型 22 汇入销售已交付。
-- 两个触发字段同时存在、旧复杂 CHECK、异常历史及额外必填字段均须人工处理后再部署。
