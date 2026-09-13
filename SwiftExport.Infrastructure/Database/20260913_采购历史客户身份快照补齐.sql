/*
用途：按采购头客户代码匹配 dbo.Customer，补齐客户 Uid 和名称空值。
在目标业务数据库中手工执行；不加入自动迁移，不修改 OriginKind 或确认状态。
名称使用当前客户主数据，并非还原历史名称；已有非空值不覆盖。
默认仅预览；核对结果后将 @Apply 改为 1，整段重新执行即可提交。
排除软删除记录；停用但未删除客户允许用于历史修复。
重复代码、身份冲突、未匹配及手工采购固定身份记录仅输出，不更新。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Apply bit = 0;
DECLARE @EmptyUid uniqueidentifier = '00000000-0000-0000-0000-000000000000';

IF @@TRANCOUNT <> 0
    THROW 51000, N'请在没有外层事务的查询窗口执行此脚本。', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 将匹配和更新置于同一事务；重复代码不得任意挑选客户。
    SELECT c.Uid, c.CustomerCode, c.CustomerName,
           COUNT(*) OVER (PARTITION BY LTRIM(RTRIM(c.CustomerCode))) AS CodeCount
    INTO #CustomerSnapshotRepairCustomers
    FROM dbo.Customer c WITH (HOLDLOCK)
    WHERE ISNULL(c.IsDeleted, 0) = 0
      AND NULLIF(LTRIM(RTRIM(c.CustomerCode)), N'') IS NOT NULL;

    SELECT h.Uid, h.OrderNo, h.CustomerCode,
           h.CustomerId AS OldCustomerId,
           h.CustomerNameSnapshot AS OldCustomerName,
           c.Uid AS MatchedCustomerId, c.CustomerName AS MatchedCustomerName,
           CASE
               WHEN h.OriginKind = 2 THEN N'跳过：手工采购固定身份受触发器保护'
               WHEN c.Uid IS NULL THEN N'跳过：未匹配未删除客户'
               WHEN c.CodeCount <> 1 THEN N'跳过：客户代码重复'
               WHEN h.CustomerId IS NOT NULL AND h.CustomerId <> @EmptyUid
                    AND h.CustomerId <> c.Uid THEN N'跳过：已有客户身份与代码匹配结果冲突'
               WHEN (h.CustomerId IS NULL OR h.CustomerId = @EmptyUid)
                    OR NULLIF(LTRIM(RTRIM(c.CustomerName)), N'') IS NOT NULL THEN N'可补齐'
               ELSE N'跳过：客户主数据名称也为空'
           END AS RepairStatus
    INTO #CustomerSnapshotRepairPlan
    FROM dbo.PurchaseOrderHead h WITH (UPDLOCK, HOLDLOCK)
    LEFT JOIN #CustomerSnapshotRepairCustomers c
      ON LTRIM(RTRIM(c.CustomerCode)) = LTRIM(RTRIM(h.CustomerCode))
    WHERE ISNULL(h.IsDeleted, 0) = 0
      AND (h.CustomerId IS NULL OR h.CustomerId = @EmptyUid
           OR NULLIF(LTRIM(RTRIM(h.CustomerNameSnapshot)), N'') IS NULL);

    SELECT Uid, OrderNo, CustomerCode, OldCustomerId, OldCustomerName,
           MatchedCustomerId, MatchedCustomerName, RepairStatus
    FROM #CustomerSnapshotRepairPlan
    ORDER BY RepairStatus, OrderNo;

    IF @Apply = 1
    BEGIN
        UPDATE h
        SET CustomerId = CASE WHEN h.CustomerId IS NULL OR h.CustomerId = @EmptyUid
                              THEN p.MatchedCustomerId ELSE h.CustomerId END,
            CustomerNameSnapshot = CASE
                WHEN NULLIF(LTRIM(RTRIM(h.CustomerNameSnapshot)), N'') IS NULL
                     AND NULLIF(LTRIM(RTRIM(p.MatchedCustomerName)), N'') IS NOT NULL
                THEN p.MatchedCustomerName ELSE h.CustomerNameSnapshot END,
            Version = h.Version + 1,
            UpdatedDate = GETDATE(),
            UpdatedBy = N'采购客户快照修复'
        OUTPUT inserted.Uid, inserted.OrderNo,
               deleted.CustomerId AS OldCustomerId, inserted.CustomerId AS NewCustomerId,
               deleted.CustomerNameSnapshot AS OldCustomerName,
               inserted.CustomerNameSnapshot AS NewCustomerName
        FROM dbo.PurchaseOrderHead h
        JOIN #CustomerSnapshotRepairPlan p ON p.Uid = h.Uid
        WHERE p.RepairStatus = N'可补齐';

        SELECT @@ROWCOUNT AS UpdatedCount;
    END;

    DROP TABLE #CustomerSnapshotRepairPlan;
    DROP TABLE #CustomerSnapshotRepairCustomers;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
