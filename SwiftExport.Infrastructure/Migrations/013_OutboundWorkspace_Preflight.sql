/*
  出库工作台 Phase 0 只读审计。
  用途：确认遗留 Outbound/PackingList/库存流水的真实结构和可证明追溯范围。
  边界：本脚本只读取系统目录和业务表；不包含 DDL、DML、回填或数据修复。
  规则：缺少明确 Uid 追溯字段的遗留行只能归为“不可证明”，禁止按单号、SKU 或 BizType 猜测。
*/
SET NOCOUNT ON;

/* 1. 目标物理表、列、键和索引。 */
;WITH ExpectedTables AS
(
    SELECT N'OutboundHead' AS TableName UNION ALL
    SELECT N'OutboundLine' UNION ALL
    SELECT N'PackingListHead' UNION ALL
    SELECT N'PackingListLine' UNION ALL
    SELECT N'InventoryTransaction' UNION ALL
    SELECT N'Inventory' UNION ALL
    SELECT N'PurchaseReceiveHead' UNION ALL
    SELECT N'PurchaseReceiveLine'
)
SELECT e.TableName,
       CASE WHEN t.object_id IS NULL THEN N'缺失' ELSE N'存在' END AS TableStatus,
       t.object_id AS ObjectId
FROM ExpectedTables e
LEFT JOIN sys.tables t ON t.schema_id = SCHEMA_ID(N'dbo') AND t.name = e.TableName
ORDER BY e.TableName;

SELECT t.name AS TableName,
       SUM(p.rows) AS PhysicalRowCount,
       CASE WHEN EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id=t.object_id AND c.name=N'IsDeleted')
            THEN N'含软删除列；业务核验时应以活动记录为准'
            ELSE N'未发现软删除列；此统计为全部物理记录' END AS CountMeaning
FROM sys.tables t
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.schema_id=SCHEMA_ID(N'dbo')
  AND t.name IN (N'OutboundHead',N'OutboundLine',N'PackingListHead',N'PackingListLine',N'InventoryTransaction',N'Inventory',N'PurchaseReceiveHead',N'PurchaseReceiveLine')
GROUP BY t.object_id,t.name
ORDER BY t.name;

SELECT t.name AS TableName,
       c.column_id AS ColumnOrder,
       c.name AS ColumnName,
       ty.name AS TypeName,
       c.max_length AS MaxLength,
       c.precision AS [Precision],
       c.scale AS Scale,
       c.is_nullable AS IsNullable
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name IN (N'OutboundHead',N'OutboundLine',N'PackingListHead',N'PackingListLine',N'InventoryTransaction',N'Inventory',N'PurchaseReceiveHead',N'PurchaseReceiveLine')
ORDER BY t.name,c.column_id;

SELECT fk.name AS ForeignKeyName,
       OBJECT_NAME(fk.parent_object_id) AS ChildTable,
       pc.name AS ChildColumn,
       OBJECT_NAME(fk.referenced_object_id) AS ParentTable,
       rc.name AS ParentColumn,
       fk.is_disabled AS IsDisabled,
       fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE SCHEMA_NAME(OBJECT_SCHEMA_ID(fk.parent_object_id)) = N'dbo'
  AND (OBJECT_NAME(fk.parent_object_id) IN (N'OutboundHead',N'OutboundLine',N'PackingListHead',N'PackingListLine')
       OR OBJECT_NAME(fk.referenced_object_id) IN (N'OutboundHead',N'OutboundLine',N'PackingListHead',N'PackingListLine'))
ORDER BY ChildTable,ForeignKeyName;

SELECT OBJECT_NAME(i.object_id) AS TableName,
       i.name AS IndexName,
       i.type_desc AS IndexType,
       i.is_unique AS IsUnique,
       i.has_filter AS HasFilter,
       i.filter_definition AS FilterDefinition,
       STRING_AGG(CASE WHEN ic.is_included_column = 0 THEN c.name END,N', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns,
       STRING_AGG(CASE WHEN ic.is_included_column = 1 THEN c.name END,N', ') WITHIN GROUP (ORDER BY c.column_id) AS IncludedColumns
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id IN (OBJECT_ID(N'dbo.OutboundHead'),OBJECT_ID(N'dbo.OutboundLine'),OBJECT_ID(N'dbo.PackingListHead'),OBJECT_ID(N'dbo.PackingListLine'),OBJECT_ID(N'dbo.InventoryTransaction'))
  AND i.index_id > 0
GROUP BY i.object_id,i.name,i.type_desc,i.is_unique,i.has_filter,i.filter_definition
ORDER BY TableName,IndexName;

/* 2. Phase 1 字段缺口：仅比较，不建议或执行任何 ALTER TABLE。 */
;WITH RequiredFields AS
(
    SELECT N'OutboundHead' AS TableName,N'身份与并发' AS FieldGroup,N'Uid' AS TargetField,N'Uid' AS CandidateColumns UNION ALL
    SELECT N'OutboundHead',N'身份与并发',N'Version',N'Version' UNION ALL
    SELECT N'OutboundHead',N'单据快照',N'DocumentNo',N'OutboundNo' UNION ALL
    SELECT N'OutboundHead',N'单据快照',N'ShipmentDate',N'OutboundDate|Date' UNION ALL
    SELECT N'OutboundHead',N'单据快照',N'DocumentType',N'DocumentType|BizType' UNION ALL
    SELECT N'OutboundHead',N'分层状态',N'LifecycleStatus',N'LifecycleStatus' UNION ALL
    SELECT N'OutboundHead',N'分层状态',N'PostingStatus',N'PostingStatus|IsPosted' UNION ALL
    SELECT N'OutboundHead',N'仓库与来源',N'WarehouseSiteId',N'WarehouseSiteId|WarehouseId' UNION ALL
    SELECT N'OutboundHead',N'仓库与来源',N'SourcePackingListId',N'SourcePackingListId|PackingListId' UNION ALL
    SELECT N'OutboundHead',N'过账与幂等',N'PostedDate/PostedBy',N'PostedDate|PostedBy' UNION ALL
    SELECT N'OutboundHead',N'过账与幂等',N'Reversal事实',N'ReversedDate|ReversedBy|ReverseReason' UNION ALL
    SELECT N'OutboundHead',N'过账与幂等',N'CommandId',N'CommandId' UNION ALL
    SELECT N'OutboundLine',N'身份与父子关系',N'Uid',N'Uid' UNION ALL
    SELECT N'OutboundLine',N'身份与父子关系',N'OutboundDocumentId',N'OutboundHeadId|OutboundId|OutboundNo' UNION ALL
    SELECT N'OutboundLine',N'身份与父子关系',N'LineNumber',N'LineNumber' UNION ALL
    SELECT N'OutboundLine',N'物料与数量',N'ProductId',N'ProductId' UNION ALL
    SELECT N'OutboundLine',N'物料与数量',N'Quantity',N'Quantity|Qty' UNION ALL
    SELECT N'OutboundLine',N'物料与数量',N'单位快照',N'Unit|UnitSnapshot' UNION ALL
    SELECT N'OutboundLine',N'业务来源',N'来源 Uid',N'SourceUid|PackingListContributionId|NetDemandAllocationId' UNION ALL
    SELECT N'OutboundLine',N'业务来源',N'销售来源快照',N'GrossDemandId|SalesOrderNoSnapshot|SalesOrderNo' UNION ALL
    SELECT N'OutboundLine',N'异常与反向',N'负库存放行事实',N'IsNegativeStockOverride|OverrideReason' UNION ALL
    SELECT N'OutboundLine',N'异常与反向',N'OriginalLineId',N'OriginalLineId' UNION ALL
    SELECT N'OutboundLine',N'幂等',N'CommandItemId',N'CommandItemId'
),
CandidateFields AS
(
    SELECT r.TableName,r.FieldGroup,r.TargetField,r.CandidateColumns,
           c.name AS ExistingColumn
    FROM RequiredFields r
    LEFT JOIN sys.tables t ON t.schema_id = SCHEMA_ID(N'dbo') AND t.name = r.TableName
    LEFT JOIN sys.columns c ON c.object_id = t.object_id
        AND CHARINDEX(N'|' + c.name + N'|',N'|' + r.CandidateColumns + N'|') > 0
)
SELECT TableName,FieldGroup,TargetField,CandidateColumns,
       STRING_AGG(ExistingColumn,N', ') AS ExistingColumns,
       CASE WHEN COUNT(ExistingColumn) = 0 THEN N'缺口：需 Phase 1 设计后最小扩展'
            ELSE N'候选列存在：必须结合语义人工确认，不可直接认定等价' END AS AuditConclusion
FROM CandidateFields
GROUP BY TableName,FieldGroup,TargetField,CandidateColumns
ORDER BY TableName,FieldGroup,TargetField;

/* 3. 头行关系候选。每个候选独立统计，不能因匹配数量而推定为真实父子关系。 */
IF COL_LENGTH(N'dbo.OutboundHead',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundHead',N'IsDeleted') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutboundLine',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'OutboundHeadId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''OutboundLine.OutboundHeadId -> OutboundHead.Uid'' AS RelationCandidate,
               SUM(CASE WHEN l.OutboundHeadId IS NULL THEN 1 ELSE 0 END) AS NullChildKeyCount,
               SUM(CASE WHEN l.OutboundHeadId IS NOT NULL AND h.Uid IS NULL THEN 1 ELSE 0 END) AS OrphanChildCount,
               SUM(CASE WHEN h.Uid IS NOT NULL AND l.Uid IS NULL THEN 1 ELSE 0 END) AS HeadWithoutLineCount
        FROM dbo.OutboundLine l
        FULL OUTER JOIN dbo.OutboundHead h ON h.Uid=l.OutboundHeadId AND ISNULL(h.IsDeleted,0)=0
        WHERE ISNULL(l.IsDeleted,0)=0 OR ISNULL(h.IsDeleted,0)=0;';

IF COL_LENGTH(N'dbo.OutboundHead',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundHead',N'IsDeleted') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutboundLine',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'OutboundId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''OutboundLine.OutboundId -> OutboundHead.Uid'' AS RelationCandidate,
               SUM(CASE WHEN l.OutboundId IS NULL THEN 1 ELSE 0 END) AS NullChildKeyCount,
               SUM(CASE WHEN l.OutboundId IS NOT NULL AND h.Uid IS NULL THEN 1 ELSE 0 END) AS OrphanChildCount,
               SUM(CASE WHEN h.Uid IS NOT NULL AND l.Uid IS NULL THEN 1 ELSE 0 END) AS HeadWithoutLineCount
        FROM dbo.OutboundLine l
        FULL OUTER JOIN dbo.OutboundHead h ON h.Uid=l.OutboundId AND ISNULL(h.IsDeleted,0)=0
        WHERE ISNULL(l.IsDeleted,0)=0 OR ISNULL(h.IsDeleted,0)=0;';

IF COL_LENGTH(N'dbo.OutboundHead',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundHead',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundHead',N'OutboundNo') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutboundLine',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'OutboundNo') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''OutboundLine.OutboundNo -> OutboundHead.OutboundNo'' AS RelationCandidate,
               SUM(CASE WHEN NULLIF(LTRIM(RTRIM(l.OutboundNo)),N'''') IS NULL THEN 1 ELSE 0 END) AS NullChildKeyCount,
               SUM(CASE WHEN NULLIF(LTRIM(RTRIM(l.OutboundNo)),N'''') IS NOT NULL AND h.Uid IS NULL THEN 1 ELSE 0 END) AS OrphanChildCount,
               SUM(CASE WHEN h.Uid IS NOT NULL AND l.Uid IS NULL THEN 1 ELSE 0 END) AS HeadWithoutLineCount
        FROM dbo.OutboundLine l
        FULL OUTER JOIN dbo.OutboundHead h ON h.OutboundNo=l.OutboundNo AND ISNULL(h.IsDeleted,0)=0
        WHERE ISNULL(l.IsDeleted,0)=0 OR ISNULL(h.IsDeleted,0)=0;';

IF COL_LENGTH(N'dbo.OutboundHead',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundHead',N'OutboundNo') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT OutboundNo,COUNT(*) AS ActiveDocumentCount
        FROM dbo.OutboundHead
        WHERE ISNULL(IsDeleted,0)=0 AND NULLIF(LTRIM(RTRIM(OutboundNo)),N'''') IS NOT NULL
        GROUP BY OutboundNo
        HAVING COUNT(*) > 1
        ORDER BY ActiveDocumentCount DESC,OutboundNo;';

/* 4. 行级来源可证明性。只有明确的贡献/Allocation Uid 才是可证明候选。 */
IF OBJECT_ID(N'dbo.OutboundLine',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundLine',N'IsDeleted') IS NOT NULL
BEGIN
    DECLARE @ProvenExpression nvarchar(max) = N'0=1';
    DECLARE @SignalExpression nvarchar(max) = N'0=1';

    IF COL_LENGTH(N'dbo.OutboundLine',N'PackingListContributionId') IS NOT NULL
    BEGIN
        SET @ProvenExpression = N'l.PackingListContributionId IS NOT NULL';
        SET @SignalExpression = N'l.PackingListContributionId IS NOT NULL';
    END;
    IF COL_LENGTH(N'dbo.OutboundLine',N'NetDemandAllocationId') IS NOT NULL
    BEGIN
        SET @ProvenExpression = @ProvenExpression + N' OR l.NetDemandAllocationId IS NOT NULL';
        SET @SignalExpression = @SignalExpression + N' OR l.NetDemandAllocationId IS NOT NULL';
    END;
    IF COL_LENGTH(N'dbo.OutboundLine',N'DemandExecutionLinkId') IS NOT NULL
        SET @SignalExpression = @SignalExpression + N' OR l.DemandExecutionLinkId IS NOT NULL';
    IF COL_LENGTH(N'dbo.OutboundLine',N'GrossDemandId') IS NOT NULL
        SET @SignalExpression = @SignalExpression + N' OR l.GrossDemandId IS NOT NULL';
    IF COL_LENGTH(N'dbo.OutboundLine',N'SourceUid') IS NOT NULL
        SET @SignalExpression = @SignalExpression + N' OR l.SourceUid IS NOT NULL';

    DECLARE @SourceAuditSql nvarchar(max) = N'
        SELECT COUNT(*) AS ActiveLineCount,
               SUM(CASE WHEN ' + @ProvenExpression + N' THEN 1 ELSE 0 END) AS ProvenSourceCandidateCount,
               SUM(CASE WHEN NOT (' + @ProvenExpression + N') THEN 1 ELSE 0 END) AS UnprovenSourceCount,
               SUM(CASE WHEN (' + @SignalExpression + N') AND NOT (' + @ProvenExpression + N') THEN 1 ELSE 0 END) AS RequiresManualEvidenceCount
        FROM dbo.OutboundLine l
        WHERE ISNULL(l.IsDeleted,0)=0;';
    EXEC sys.sp_executesql @SourceAuditSql;
END;

/* 5. PackingList 头行关系候选及可供后续核验的来源字段。 */
IF COL_LENGTH(N'dbo.PackingListHead',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListHead',N'IsDeleted') IS NOT NULL
   AND COL_LENGTH(N'dbo.PackingListLine',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListLine',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListLine',N'PackingListHeadId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''PackingListLine.PackingListHeadId -> PackingListHead.Uid'' AS RelationCandidate,
               SUM(CASE WHEN l.PackingListHeadId IS NOT NULL AND h.Uid IS NULL THEN 1 ELSE 0 END) AS OrphanLineCount,
               SUM(CASE WHEN h.Uid IS NOT NULL AND l.Uid IS NULL THEN 1 ELSE 0 END) AS HeadWithoutLineCount
        FROM dbo.PackingListLine l
        FULL OUTER JOIN dbo.PackingListHead h ON h.Uid=l.PackingListHeadId AND ISNULL(h.IsDeleted,0)=0
        WHERE ISNULL(l.IsDeleted,0)=0 OR ISNULL(h.IsDeleted,0)=0;';

IF COL_LENGTH(N'dbo.PackingListHead',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListHead',N'IsDeleted') IS NOT NULL
   AND COL_LENGTH(N'dbo.PackingListLine',N'Uid') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListLine',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.PackingListLine',N'PackingListId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT N''PackingListLine.PackingListId -> PackingListHead.Uid'' AS RelationCandidate,
               SUM(CASE WHEN l.PackingListId IS NOT NULL AND h.Uid IS NULL THEN 1 ELSE 0 END) AS OrphanLineCount,
               SUM(CASE WHEN h.Uid IS NOT NULL AND l.Uid IS NULL THEN 1 ELSE 0 END) AS HeadWithoutLineCount
        FROM dbo.PackingListLine l
        FULL OUTER JOIN dbo.PackingListHead h ON h.Uid=l.PackingListId AND ISNULL(h.IsDeleted,0)=0
        WHERE ISNULL(l.IsDeleted,0)=0 OR ISNULL(h.IsDeleted,0)=0;';

/* 6. 库存流水和非零库存风险。BizType/BizNo 仅作为人工核查信号，不能证明出库来源。 */
IF OBJECT_ID(N'dbo.InventoryTransaction',N'U') IS NOT NULL
BEGIN
    SELECT c.name AS InventoryTransactionTraceField,
           CASE WHEN c.name IN (N'SourceDocumentId',N'SourceLineId',N'CommandId',N'CommandItemId',N'ReversalOfLedgerId')
                THEN N'新口径可用于明确 Uid/幂等/反向追溯'
                ELSE N'遗留展示或人工核查信号，不单独证明来源' END AS AuditMeaning
    FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(N'dbo.InventoryTransaction')
      AND c.name IN (N'BizType',N'BizNo',N'BizLineId',N'RefNo',N'SourceDocumentId',N'SourceLineId',N'CommandId',N'CommandItemId',N'ReversalOfLedgerId',N'TransactionType')
    ORDER BY c.name;

    IF COL_LENGTH(N'dbo.InventoryTransaction',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.InventoryTransaction',N'BizType') IS NOT NULL
        EXEC sys.sp_executesql N'
            SELECT BizType,COUNT(*) AS ActiveLedgerCount
            FROM dbo.InventoryTransaction
            WHERE ISNULL(IsDeleted,0)=0
            GROUP BY BizType
            ORDER BY ActiveLedgerCount DESC,BizType;';
END;

IF COL_LENGTH(N'dbo.Inventory',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'WarehouseId') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'ProductId') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'OnhandQty') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT WarehouseId,ProductId,OnhandQty,COUNT(*) OVER(PARTITION BY WarehouseId,ProductId) AS DuplicateBalanceCount
        FROM dbo.Inventory
        WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(OnhandQty,0)<>0
        ORDER BY WarehouseId,ProductId;';
ELSE IF COL_LENGTH(N'dbo.Inventory',N'IsDeleted') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'WhseCode') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'Sku') IS NOT NULL AND COL_LENGTH(N'dbo.Inventory',N'OnhandQty') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT WhseCode,Sku,OnhandQty,COUNT(*) OVER(PARTITION BY WhseCode,Sku) AS DuplicateBalanceCount
        FROM dbo.Inventory
        WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(OnhandQty,0)<>0
        ORDER BY WhseCode,Sku;';

/*
  结果处理：
  1. ProvenSourceCandidateCount 仅是“可进一步核验”的候选；仍须用实际 Uid 关联 PackingList/Allocation/销售来源复核。
  2. UnprovenSourceCount 必须列入不可证明来源清单，不回填、不纳入新已发货汇总、不允许新命令操作。
  3. 本脚本没有、也不得补充任何迁移、回填、库存余额更新或数据修复语句。
*/
