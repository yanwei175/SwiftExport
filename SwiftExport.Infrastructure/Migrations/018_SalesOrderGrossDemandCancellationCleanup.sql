/*
  历史销售订单取消确认毛需求治理
  - 默认 @Execute=0：只读报告，事务最终回滚。
  - @Execute=1：仅处理可以完整证明安全的记录；存在任何阻断项则整批拒绝。
  - 本脚本不由应用启动自动执行，运行前必须另行取得数据库执行确认并完成备份。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Execute bit = 0;
DECLARE @MigrationReason nvarchar(1000) = N'历史销售订单取消确认毛需求规范化';
DECLARE @Now datetime2(7) = SYSDATETIME();
DECLARE @UserName nvarchar(100) = COALESCE(NULLIF(SUSER_SNAME(), N''), N'DatabaseMigration');

IF OBJECT_ID(N'dbo.GrossDemand', N'U') IS NULL
    THROW 51000, N'缺少 dbo.GrossDemand，不能执行治理脚本。', 1;
IF OBJECT_ID(N'dbo.NetDemand', N'U') IS NULL
    THROW 51000, N'缺少 dbo.NetDemand，不能执行治理脚本。', 1;
IF OBJECT_ID(N'dbo.NetDemandAllocation', N'U') IS NULL
    THROW 51000, N'缺少 dbo.NetDemandAllocation，不能执行治理脚本。', 1;
IF OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NULL
    THROW 51000, N'缺少 dbo.DemandExecutionLink，不能执行治理脚本。', 1;

BEGIN TRANSACTION;

CREATE TABLE #Evaluation
(
    GrossDemandId uniqueidentifier NOT NULL PRIMARY KEY,
    SourceUid uniqueidentifier NOT NULL,
    SourceLineId uniqueidentifier NOT NULL,
    DecompositionRevision int NOT NULL,
    NetDemandCount int NOT NULL,
    Disposition varchar(20) NOT NULL,
    BlockReason nvarchar(1000) NULL
);

INSERT INTO #Evaluation
(
    GrossDemandId, SourceUid, SourceLineId, DecompositionRevision,
    NetDemandCount, Disposition, BlockReason
)
SELECT
    d.[Uid],
    d.[SourceUid],
    d.[SourceLineId],
    d.[DecompositionRevision],
    facts.[NetDemandCount],
    CASE
        WHEN facts.[HasFormalFact] = 1 THEN 'BLOCKED'
        WHEN d.[DecompositionRevision] = 0 AND facts.[NetDemandCount] = 0
            THEN 'HARD_DELETE'
        WHEN d.[DecompositionRevision] > 0
             AND facts.[NetDemandCount] > 0
             AND facts.[InvalidNetDemandCount] = 0
             AND facts.[DistinctRevisionCount] = d.[DecompositionRevision]
             AND facts.[NetDemandWithoutAllocationCount] = 0
             AND facts.[InvalidAllocationCount] = 0
             AND facts.[ActiveLinkCount] = 0
            THEN 'RESTORE_VOID'
        ELSE 'BLOCKED'
    END,
    CASE
        WHEN facts.[HasFormalFact] = 1
            THEN N'存在入库、出库、装箱或库存流水事实。'
        WHEN d.[DecompositionRevision] = 0 AND facts.[NetDemandCount] > 0
            THEN N'拆解修订号为 0，但存在净需求历史。'
        WHEN d.[DecompositionRevision] > 0 AND facts.[NetDemandCount] = 0
            THEN N'存在拆解修订号，但缺少净需求历史。'
        WHEN facts.[InvalidNetDemandCount] > 0
            THEN N'净需求不是有效的 RolledBack/Voided 历史。'
        WHEN facts.[DistinctRevisionCount] <> d.[DecompositionRevision]
            THEN N'毛需求修订号与净需求历史修订不连续。'
        WHEN facts.[NetDemandWithoutAllocationCount] > 0
            THEN N'存在缺少计划分配历史的净需求。'
        WHEN facts.[InvalidAllocationCount] > 0
            THEN N'存在未取消、未替代或被软删除的计划分配历史。'
        WHEN facts.[ActiveLinkCount] > 0
            THEN N'存在活动执行追溯关系。'
        ELSE NULL
    END
FROM [dbo].[GrossDemand] d WITH (UPDLOCK,HOLDLOCK)
CROSS APPLY
(
    SELECT
        (SELECT COUNT(1)
         FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
         WHERE nd.[GrossDemandId]=d.[Uid]) AS [NetDemandCount],
        (SELECT COUNT(DISTINCT nd.[DecompositionRevision])
         FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
         WHERE nd.[GrossDemandId]=d.[Uid]) AS [DistinctRevisionCount],
        (SELECT COUNT(1)
         FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
         WHERE nd.[GrossDemandId]=d.[Uid]
           AND (nd.[IsDeleted]<>0 OR nd.[LifecycleStatus] NOT IN (8,9)
                OR nd.[DecompositionRevision]<1
                OR nd.[DecompositionRevision]>d.[DecompositionRevision])) AS [InvalidNetDemandCount],
        (SELECT COUNT(1)
         FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
         WHERE nd.[GrossDemandId]=d.[Uid]
           AND NOT EXISTS (
               SELECT 1 FROM [dbo].[NetDemandAllocation] a WITH (UPDLOCK,HOLDLOCK)
               WHERE a.[NetDemandId]=nd.[Uid])) AS [NetDemandWithoutAllocationCount],
        (SELECT COUNT(1)
         FROM [dbo].[NetDemandAllocation] a WITH (UPDLOCK,HOLDLOCK)
         INNER JOIN [dbo].[NetDemand] nd ON nd.[Uid]=a.[NetDemandId]
         WHERE nd.[GrossDemandId]=d.[Uid]
           AND (a.[IsDeleted]<>0 OR a.[LifecycleStatus] NOT IN (8,9))) AS [InvalidAllocationCount],
        (SELECT COUNT(1)
         FROM [dbo].[DemandExecutionLink] link WITH (UPDLOCK,HOLDLOCK)
         INNER JOIN [dbo].[NetDemand] nd ON nd.[Uid]=link.[NetDemandId]
         WHERE nd.[GrossDemandId]=d.[Uid]
           AND link.[LinkStatus]=0 AND link.[IsDeleted]=0) AS [ActiveLinkCount],
        CONVERT(bit, CASE WHEN
            EXISTS (
                SELECT 1
                FROM [dbo].[InboundDocumentLine] il
                LEFT JOIN [dbo].[DemandExecutionLink] link ON link.[Uid]=il.[DemandExecutionLinkId]
                LEFT JOIN [dbo].[NetDemandAllocation] a ON a.[Uid]=COALESCE(il.[NetDemandAllocationId],link.[NetDemandAllocationId])
                LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=COALESCE(link.[NetDemandId],a.[NetDemandId])
                WHERE COALESCE(il.[GrossDemandId],nd.[GrossDemandId])=d.[Uid])
            OR EXISTS (
                SELECT 1
                FROM [dbo].[OutboundDocumentLine] ol
                LEFT JOIN [dbo].[NetDemandAllocation] a ON a.[Uid]=ol.[NetDemandAllocationId]
                LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=a.[NetDemandId]
                WHERE COALESCE(ol.[GrossDemandId],nd.[GrossDemandId])=d.[Uid])
            OR EXISTS (SELECT 1 FROM [dbo].[PackingListContributionLine] pl WHERE pl.[GrossDemandId]=d.[Uid])
            OR EXISTS (
                SELECT 1
                FROM [dbo].[InventoryLedgerEntry] ledger
                INNER JOIN [dbo].[InboundDocumentLine] il ON il.[Uid]=ledger.[SourceLineId]
                LEFT JOIN [dbo].[DemandExecutionLink] link ON link.[Uid]=il.[DemandExecutionLinkId]
                LEFT JOIN [dbo].[NetDemandAllocation] a ON a.[Uid]=COALESCE(il.[NetDemandAllocationId],link.[NetDemandAllocationId])
                LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=COALESCE(link.[NetDemandId],a.[NetDemandId])
                WHERE COALESCE(il.[GrossDemandId],nd.[GrossDemandId])=d.[Uid])
            OR EXISTS (
                SELECT 1
                FROM [dbo].[InventoryLedgerEntry] ledger
                INNER JOIN [dbo].[OutboundDocumentLine] ol ON ol.[Uid]=ledger.[SourceLineId]
                LEFT JOIN [dbo].[NetDemandAllocation] a ON a.[Uid]=ol.[NetDemandAllocationId]
                LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=a.[NetDemandId]
                WHERE COALESCE(ol.[GrossDemandId],nd.[GrossDemandId])=d.[Uid])
            THEN 1 ELSE 0 END) AS [HasFormalFact]
) facts
WHERE d.[SourceType]=N'SO' AND d.[IsDeleted]=1;

SELECT
    [GrossDemandId], [SourceUid], [SourceLineId], [DecompositionRevision],
    [NetDemandCount], [Disposition], [BlockReason]
FROM #Evaluation
ORDER BY [Disposition], [SourceUid], [SourceLineId];

SELECT [Disposition], COUNT(1) AS [RecordCount]
FROM #Evaluation
GROUP BY [Disposition]
ORDER BY [Disposition];

IF @Execute = 0
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'只读报告完成：@Execute=0，未写入任何业务数据。';
    RETURN;
END;

IF EXISTS (SELECT 1 FROM #Evaluation WHERE [Disposition]='BLOCKED')
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51001, N'存在无法证明安全的历史记录，整批治理已阻断。', 1;
END;

DECLARE @ExpectedHardDelete int =
    (SELECT COUNT(1) FROM #Evaluation WHERE [Disposition]='HARD_DELETE');
DECLARE @ExpectedRestoreVoid int =
    (SELECT COUNT(1) FROM #Evaluation WHERE [Disposition]='RESTORE_VOID');

DELETE d
OUTPUT DELETED.[Uid] AS [HardDeletedGrossDemandId]
FROM [dbo].[GrossDemand] d
INNER JOIN #Evaluation e ON e.[GrossDemandId]=d.[Uid] AND e.[Disposition]='HARD_DELETE'
WHERE d.[IsDeleted]=1 AND d.[DecompositionRevision]=0
  AND NOT EXISTS (SELECT 1 FROM [dbo].[NetDemand] nd WHERE nd.[GrossDemandId]=d.[Uid]);

IF @@ROWCOUNT <> @ExpectedHardDelete
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51002, N'硬删除候选在执行期间发生变化，整批治理已回滚。', 1;
END;

UPDATE d
SET d.[IsDeleted]=0,
    d.[LifecycleStatus]=9,
    d.[DecompositionStatus]=0,
    d.[LifecycleChangedAt]=@Now,
    d.[LifecycleChangedBy]=@UserName,
    d.[LifecycleChangedReason]=@MigrationReason,
    d.[UpdatedDate]=@Now,
    d.[UpdatedBy]=@UserName,
    d.[Version]=d.[Version]+1
OUTPUT INSERTED.[Uid] AS [RestoredVoidedGrossDemandId]
FROM [dbo].[GrossDemand] d
INNER JOIN #Evaluation e ON e.[GrossDemandId]=d.[Uid] AND e.[Disposition]='RESTORE_VOID'
WHERE d.[IsDeleted]=1 AND d.[DecompositionRevision]>0;

IF @@ROWCOUNT <> @ExpectedRestoreVoid
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51003, N'作废保留候选在执行期间发生变化，整批治理已回滚。', 1;
END;

COMMIT TRANSACTION;

SELECT
    @ExpectedHardDelete AS [HardDeletedGrossDemandCount],
    @ExpectedRestoreVoid AS [RestoredVoidedGrossDemandCount];
