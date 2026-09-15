-- PackingList 草稿接口增量结构：先执行同序号 Preflight。
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRAN;
    IF OBJECT_ID(N'dbo.PackingListHead',N'U') IS NULL OR OBJECT_ID(N'dbo.PackingListLine',N'U') IS NULL
        THROW 51000,N'PackingListHead/Line 新表尚未部署。',1;
    IF OBJECT_ID(N'dbo.PackingListDocument',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PackingListContributionLine',N'U') IS NOT NULL
        THROW 51003,N'旧 PackingList 表仍存在，拒绝形成双写或双读结构。',1;
    IF EXISTS(SELECT 1 FROM dbo.PackingListLine WITH(UPDLOCK,HOLDLOCK) WHERE Qty<=0)
        THROW 51001,N'存在非正交付数量，拒绝收紧约束。',1;
    IF EXISTS(SELECT CreateCommandId FROM dbo.PackingListHead WITH(UPDLOCK,HOLDLOCK)
              GROUP BY CreateCommandId HAVING COUNT_BIG(*)>1)
        THROW 51002,N'CreateCommandId 存在重复，拒绝建立唯一索引。',1;

    IF EXISTS(SELECT 1 FROM sys.check_constraints
              WHERE parent_object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'CK_PackingListLine_Qty')
        ALTER TABLE dbo.PackingListLine DROP CONSTRAINT CK_PackingListLine_Qty;
    ALTER TABLE dbo.PackingListLine WITH CHECK
        ADD CONSTRAINT CK_PackingListLine_Qty CHECK(Qty>0);
    ALTER TABLE dbo.PackingListLine CHECK CONSTRAINT CK_PackingListLine_Qty;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes
                  WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'UX_PackingListHead_CreateCommand')
        CREATE UNIQUE INDEX UX_PackingListHead_CreateCommand ON dbo.PackingListHead(CreateCommandId);

    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
