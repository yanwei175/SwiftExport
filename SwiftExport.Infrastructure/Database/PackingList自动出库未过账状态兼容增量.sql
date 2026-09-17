-- 自动出库与手工草稿都需要先保存未过账状态；仅扩展状态约束，不改表或历史数据。
-- 必须在确认的目标数据库内执行。WITH CHECK 同时校验已有数据并保持约束受信任。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.OutboundDocument', N'U') IS NULL
    THROW 51000, N'缺少 OutboundDocument，未执行变更。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.OutboundDocument')
    AND name=N'CK_OutboundDocument_Status')
    THROW 51000, N'缺少预期状态约束，请先核对结构。', 1;
IF EXISTS (SELECT 1 FROM dbo.OutboundDocument
    WHERE LifecycleStatus NOT IN (0,8,9) OR PostingStatus NOT IN (0,1,8))
    THROW 51000, N'存在未知状态，不能自动修改约束。', 1;
ALTER TABLE dbo.OutboundDocument DROP CONSTRAINT CK_OutboundDocument_Status;
ALTER TABLE dbo.OutboundDocument WITH CHECK ADD CONSTRAINT CK_OutboundDocument_Status
    CHECK (LifecycleStatus IN (0,8,9) AND PostingStatus IN (0,1,8));
ALTER TABLE dbo.OutboundDocument CHECK CONSTRAINT CK_OutboundDocument_Status;
COMMIT TRANSACTION;
