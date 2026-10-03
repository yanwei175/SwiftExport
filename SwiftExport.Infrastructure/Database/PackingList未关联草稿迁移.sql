-- 仅调整来源列可空性，保留全部业务数据；由管理员协调两端客户端更新后执行。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.PackingListLine', N'U') IS NULL THROW 50001, N'PackingListLine 尚未部署。', 1;
IF EXISTS(SELECT 1 FROM dbo.PackingListLine WHERE GrossDemandId='00000000-0000-0000-0000-000000000000')
    THROW 50002, N'检测到空 Guid 来源，请先人工核对，不猜测历史关系。', 1;
IF EXISTS(SELECT 1 FROM dbo.PackingListLine l JOIN dbo.PackingListHead h ON h.Uid=l.PackingListHeadId
          WHERE h.PostingStatus<>0 AND l.GrossDemandId IS NULL AND l.IsDeleted=0)
    THROW 50003, N'非草稿存在未关联行，请先人工核对。', 1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'GrossDemandId' AND is_nullable=0)
BEGIN
    DROP INDEX IF EXISTS IX_PackingListLine_GrossDemand ON dbo.PackingListLine;
    ALTER TABLE dbo.PackingListLine ALTER COLUMN GrossDemandId uniqueidentifier NULL;
    CREATE INDEX IX_PackingListLine_GrossDemand ON dbo.PackingListLine(GrossDemandId,PackingListHeadId) WHERE IsDeleted=0;
END;
COMMIT TRANSACTION;
