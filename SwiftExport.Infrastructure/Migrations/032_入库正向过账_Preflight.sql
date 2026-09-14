-- 只读部署前检查：不创建对象、不改数据、不执行迁移。
-- 先完成 031_入库非过账接口_Preflight.sql 与库存内核 031 预检。
-- 任何缺字段、错误精度、缺唯一索引或历史来源不明都必须停止启用过账权限。
SET NOCOUNT ON;

SELECT expected.TableName,expected.ColumnName,TYPE_NAME(c.user_type_id) AS ActualType,
       c.precision,c.scale,c.is_nullable,
       CASE WHEN c.column_id IS NULL THEN N'阻断：缺少字段' ELSE N'存在：继续核对类型与业务含义' END AS Result
FROM (VALUES
    (N'InboundDocument',N'Uid'),(N'InboundDocument',N'Version'),(N'InboundDocument',N'CreationMode'),
    (N'InboundDocument',N'PostingStatus'),(N'InboundDocument',N'PostedDate'),(N'InboundDocument',N'PostedBy'),
    (N'InboundDocument',N'TriggerPackingListId'),(N'InboundDocumentLine',N'SourceLineId'),
    (N'InboundDocumentLine',N'OriginalLineId'),(N'InboundDocumentLine',N'Quantity'),
    (N'PurchaseOrderHead',N'ConfirmationStatus'),(N'PurchaseOrderHead',N'LifecycleStatus'),
    (N'PurchaseOrderLine',N'PurchaseOrderId'),(N'PurchaseOrderLine',N'QTY'),(N'PurchaseOrderLine',N'UnitSnapshot'),
    (N'InventoryBalance',N'OnHandQty'),(N'InventoryLedgerEntry',N'CommandItemId'),
    (N'InventoryLedgerEntry',N'SourceDocumentId'),(N'InventoryLedgerEntry',N'SourceLineId'),
    (N'BusinessOperationLog',N'ChangedFieldsJson'),(N'BusinessOperationLog',N'RelatedObjectsJson')
) expected(TableName,ColumnName)
LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+expected.TableName) AND c.name=expected.ColumnName
ORDER BY expected.TableName,expected.ColumnName;

-- 核对实际唯一键列与过滤条件；仅同名不代表满足要求。
SELECT OBJECT_NAME(i.object_id) AS TableName,i.name,i.is_unique,i.is_disabled,i.filter_definition,
       c.name AS KeyColumn,ic.key_ordinal
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.InventoryBalance'),OBJECT_ID(N'dbo.InventoryLedgerEntry'),
    OBJECT_ID(N'dbo.BusinessOperationLog'),OBJECT_ID(N'dbo.InboundDocumentLine'),OBJECT_ID(N'dbo.PurchaseOrderHead'),OBJECT_ID(N'dbo.PurchaseOrderLine'))
ORDER BY TableName,i.name,ic.key_ordinal;

SELECT name,start_value,current_value,increment FROM sys.sequences WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerSequence');
SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted
FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.InboundDocumentLine'),OBJECT_ID(N'dbo.InventoryLedgerEntry'),OBJECT_ID(N'dbo.BusinessOperationLog'));

-- 动态只读查询避免缺少新字段时整批编译失败；缺字段不能解释为无历史风险。
IF COL_LENGTH(N'dbo.InboundDocumentLine',N'SourceLineId') IS NOT NULL
    AND COL_LENGTH(N'dbo.InboundDocument',N'PostingStatus') IS NOT NULL
    AND OBJECT_ID(N'dbo.PurchaseOrderLine') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT ih.Uid,ih.DocumentNo,il.Uid AS LineId,il.SourceLineId
        FROM dbo.InboundDocumentLine il JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
        LEFT JOIN dbo.PurchaseOrderLine pl ON pl.Uid=il.SourceLineId
        WHERE ih.IsDeleted=0 AND il.IsDeleted=0 AND ih.PostingStatus IN(1,8)
          AND (ih.DocumentType IN(11,12) OR (ih.DocumentType=10 AND pl.Uid IS NULL));';
END
ELSE SELECT N'阻断：缺少新入库来源字段或采购表，未执行历史检查' AS Result;

-- 必须人工确认：InventoryBalance 唯一键为 WarehouseSiteId+ProductId；
-- Ledger 的 CommandItemId、Sequence 唯一；日志保留现有命令唯一索引和 JSON 对象约束；
-- 入库数量继续为 decimal(18,4) 正数，不为本批放宽负数量约束。
-- 代码交接阻断：ProcurementOrderRepository.HasDownstreamFactsAsync 仍引用 SourceType/SourceUid，
-- 如该取消确认入口启用，必须由采购任务迁移到 DocumentType+SourceLineId 后再联合验收。
