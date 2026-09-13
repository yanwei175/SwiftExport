/*
  库存不可变流水增量结构。人工审核 031 preflight 无阻断后，仅在隔离库/目标库执行。
  脚本不连接数据库、不修复遗留事实，并可重复执行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.WarehouseSite',N'U') IS NULL OR OBJECT_ID(N'dbo.InventoryBalance',N'U') IS NULL OR OBJECT_ID(N'dbo.InventoryLedgerEntry',N'U') IS NULL
        THROW 53101,N'库存迁移阻断：目标库存表不存在。',1;

    IF COL_LENGTH(N'dbo.InventoryBalance',N'ProductNameSnapshot') IS NULL ALTER TABLE dbo.InventoryBalance ADD ProductNameSnapshot nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'SourceDocumentNoSnapshot') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD SourceDocumentNoSnapshot nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'ReplacementOfLedgerId') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD ReplacementOfLedgerId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'CorrectionGroupId') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD CorrectionGroupId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'RequestFingerprint') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD RequestFingerprint char(64) NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'WarehouseNameSnapshot') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD WarehouseNameSnapshot nvarchar(300) NULL;
    IF COL_LENGTH(N'dbo.InventoryLedgerEntry',N'ProductNameSnapshot') IS NULL ALTER TABLE dbo.InventoryLedgerEntry ADD ProductNameSnapshot nvarchar(500) NULL;

    -- 兼容早期脚本已经建立 nvarchar(200) 的数据库；只扩容元数据，不改写任何流水事实。
    DECLARE @WarehouseNameType sysname,@WarehouseNameMaxLength smallint,@WarehouseNameNullable bit;
    SELECT @WarehouseNameType=t.name,@WarehouseNameMaxLength=c.max_length,@WarehouseNameNullable=c.is_nullable
    FROM sys.columns c
    INNER JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND c.name=N'WarehouseNameSnapshot';
    IF @WarehouseNameType<>N'nvarchar' OR @WarehouseNameMaxLength NOT IN (400,600) OR @WarehouseNameNullable<>1
        THROW 53115,N'库存迁移阻断：WarehouseNameSnapshot 必须是可空 nvarchar(200) 或 nvarchar(300)。',1;
    IF @WarehouseNameMaxLength=400
        EXEC(N'ALTER TABLE dbo.InventoryLedgerEntry ALTER COLUMN WarehouseNameSnapshot nvarchar(300) NULL;');

    IF EXISTS(SELECT 1 FROM dbo.InventoryBalance GROUP BY WarehouseSiteId,ProductId HAVING COUNT_BIG(*)>1)
        THROW 53102,N'库存迁移阻断：余额唯一键存在重复。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryBalance WHERE IsDeleted<>0)
        THROW 53116,N'库存迁移阻断：余额投影存在软删除记录。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE IsDeleted<>0)
        THROW 53103,N'库存迁移阻断：不可变流水存在软删除记录。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry GROUP BY Sequence HAVING COUNT_BIG(*)>1)
        THROW 53112,N'库存迁移阻断：流水序号存在重复。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE ReversalOfLedgerId IS NOT NULL GROUP BY ReversalOfLedgerId HAVING COUNT_BIG(*)>1)
        THROW 53104,N'库存迁移阻断：同一原流水存在多个反向。',1;
    DECLARE @InvalidRelationship bit=0;
    EXEC sys.sp_executesql
        N'SELECT @Invalid=CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE (TransactionType=900 AND (ReversalOfLedgerId IS NULL OR ReplacementOfLedgerId IS NOT NULL)) OR (TransactionType<>900 AND ReversalOfLedgerId IS NOT NULL)) THEN 1 ELSE 0 END);',
        N'@Invalid bit OUTPUT',@Invalid=@InvalidRelationship OUTPUT;
    IF @InvalidRelationship=1 THROW 53106,N'库存迁移阻断：反向流水关系不完整。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry l LEFT JOIN dbo.InventoryLedgerEntry original ON original.Uid=l.ReversalOfLedgerId WHERE l.ReversalOfLedgerId IS NOT NULL AND original.Uid IS NULL)
        THROW 53107,N'库存迁移阻断：反向流水引用的原流水不存在。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryBalance b LEFT JOIN dbo.WarehouseSite w ON w.Uid=b.WarehouseSiteId LEFT JOIN dbo.Product p ON p.Uid=b.ProductId WHERE w.Uid IS NULL OR p.Uid IS NULL)
        OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry l LEFT JOIN dbo.WarehouseSite w ON w.Uid=l.WarehouseSiteId LEFT JOIN dbo.Product p ON p.Uid=l.ProductId WHERE w.Uid IS NULL OR p.Uid IS NULL)
        THROW 53108,N'库存迁移阻断：仓库或产品身份引用失效。',1;
    IF EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry l LEFT JOIN dbo.InventoryBalance b ON b.WarehouseSiteId=l.WarehouseSiteId AND b.ProductId=l.ProductId WHERE b.Uid IS NULL)
        THROW 53113,N'库存迁移阻断：存在流水但缺少余额投影。',1;
    IF EXISTS(
        SELECT 1 FROM (
            SELECT BalanceAfter,SUM(QuantityDelta) OVER(PARTITION BY WarehouseSiteId,ProductId ORDER BY Sequence,Uid ROWS UNBOUNDED PRECEDING) AS ExpectedBalanceAfter
            FROM dbo.InventoryLedgerEntry
        ) running WHERE BalanceAfter<>ExpectedBalanceAfter)
        THROW 53114,N'库存迁移阻断：逐流水 BalanceAfter 不连续。',1;
    IF EXISTS(
        SELECT b.Uid FROM dbo.InventoryBalance b
        LEFT JOIN dbo.InventoryLedgerEntry l ON l.WarehouseSiteId=b.WarehouseSiteId AND l.ProductId=b.ProductId
        GROUP BY b.Uid,b.OnHandQty,b.LastLedgerSequence
        HAVING b.OnHandQty<>COALESCE(SUM(l.QuantityDelta),0) OR b.LastLedgerSequence<>COALESCE(MAX(l.Sequence),0))
        THROW 53109,N'库存迁移阻断：余额投影与流水净额不一致。',1;

    IF OBJECT_ID(N'dbo.CK_InventoryLedger_Relationships',N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.InventoryLedgerEntry WITH CHECK ADD CONSTRAINT CK_InventoryLedger_Relationships CHECK((TransactionType=900 AND ReversalOfLedgerId IS NOT NULL AND ReplacementOfLedgerId IS NULL) OR (TransactionType<>900 AND ReversalOfLedgerId IS NULL));');
    IF OBJECT_ID(N'dbo.CK_InventoryLedger_CorrectionGroup',N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.InventoryLedgerEntry WITH CHECK ADD CONSTRAINT CK_InventoryLedger_CorrectionGroup CHECK(CorrectionGroupId IS NULL OR ReversalOfLedgerId IS NOT NULL OR ReplacementOfLedgerId IS NOT NULL);');
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryBalance') AND name=N'UX_InventoryBalance_Warehouse_Product')
        DROP INDEX UX_InventoryBalance_Warehouse_Product ON dbo.InventoryBalance;
    CREATE UNIQUE INDEX UX_InventoryBalance_Warehouse_Product ON dbo.InventoryBalance(WarehouseSiteId,ProductId);

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'UX_InventoryLedger_CommandItem')
        CREATE UNIQUE INDEX UX_InventoryLedger_CommandItem ON dbo.InventoryLedgerEntry(CommandItemId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'UX_InventoryLedger_Sequence')
        CREATE UNIQUE INDEX UX_InventoryLedger_Sequence ON dbo.InventoryLedgerEntry(Sequence);

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'UX_InventoryLedger_ReversalOf')
        EXEC(N'CREATE UNIQUE INDEX UX_InventoryLedger_ReversalOf ON dbo.InventoryLedgerEntry(ReversalOfLedgerId) WHERE ReversalOfLedgerId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'UX_InventoryLedger_ReplacementOf')
        EXEC(N'CREATE UNIQUE INDEX UX_InventoryLedger_ReplacementOf ON dbo.InventoryLedgerEntry(ReplacementOfLedgerId) WHERE ReplacementOfLedgerId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'IX_InventoryLedger_CorrectionGroup')
        EXEC(N'CREATE INDEX IX_InventoryLedger_CorrectionGroup ON dbo.InventoryLedgerEntry(CorrectionGroupId,Sequence) WHERE CorrectionGroupId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'IX_InventoryLedger_WarehouseProductSequence')
        CREATE INDEX IX_InventoryLedger_WarehouseProductSequence ON dbo.InventoryLedgerEntry(WarehouseSiteId,ProductId,Sequence DESC);

    EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_InventoryLedgerEntry_Immutable ON dbo.InventoryLedgerEntry INSTEAD OF UPDATE,DELETE AS THROW 53105,N''库存流水是不可变事实，禁止更新或删除；纠错必须追加反向流水。'',1;');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
