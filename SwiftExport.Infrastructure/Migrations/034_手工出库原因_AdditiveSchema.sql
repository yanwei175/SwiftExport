/* 人工审核增量：在已部署 033 后添加出库原因。不连接数据库，不修改已执行迁移。
   历史已过账单据不猜测回填；列允许空值，新的过账必填门禁由 Application 执行。
   执行前需用户当次授权、备份并审核 034 Preflight。 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb') THROW 53400,N'禁止在系统数据库执行。',1;
IF @@TRANCOUNT<>0 THROW 53401,N'请在无活动事务的会话执行。',1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.OutboundDocument',N'U') IS NULL
        OR COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NULL
        OR COL_LENGTH(N'dbo.OutboundDocument',N'Remark') IS NULL
        THROW 53402,N'缺少 033 出库基础结构，请先核对。',1;
    IF COL_LENGTH(N'dbo.OutboundDocument',N'IssueReason') IS NULL
        ALTER TABLE dbo.OutboundDocument ADD IssueReason nvarchar(500) NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocument')
        AND name=N'IssueReason' AND TYPE_NAME(user_type_id)=N'nvarchar' AND max_length=1000
        AND is_nullable=1 AND is_computed=0 AND default_object_id=0)
        THROW 53403,N'IssueReason 必须为无默认值的可空 nvarchar(500)；禁止自动转换或截断历史值。',1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
