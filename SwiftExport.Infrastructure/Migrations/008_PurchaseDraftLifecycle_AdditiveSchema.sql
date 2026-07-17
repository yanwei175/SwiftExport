/*
    采购草稿取消/减量增量结构。
    本脚本仅提供给人工审核执行；Codex 不连接共享 dv 或生产库。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.DemandExecutionLink',N'U') IS NULL
        THROW 51840,N'dbo.DemandExecutionLink is required.',1;

    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'ReleasedQty') IS NULL
        ALTER TABLE dbo.DemandExecutionLink
            ADD ReleasedQty decimal(18,4) NOT NULL
                CONSTRAINT DF_DemandExecutionLink_ReleasedQty DEFAULT(0) WITH VALUES;

    IF OBJECT_ID(N'dbo.CK_DemandExecutionLink_ReleasedQty',N'C') IS NULL
        ALTER TABLE dbo.DemandExecutionLink WITH CHECK
            ADD CONSTRAINT CK_DemandExecutionLink_ReleasedQty
            CHECK(ReleasedQty>=0 AND ReleasedQty<=AllocatedQty);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink')
          AND name=N'IX_DemandExecutionLink_TargetHeadStatus'
    )
        CREATE INDEX IX_DemandExecutionLink_TargetHeadStatus
            ON dbo.DemandExecutionLink(TargetHeadId,TargetType,LinkStatus,IsDeleted)
            INCLUDE(TargetLineId,NetDemandAllocationId,AllocatedQty,ReleasedQty,PlannedDeliveryDate);

    IF OBJECT_ID(N'dbo.PurchaseReceiveLine',N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PurchaseReceiveLine',N'PoLineId') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1 FROM sys.indexes
           WHERE object_id=OBJECT_ID(N'dbo.PurchaseReceiveLine')
             AND name=N'IX_PurchaseReceiveLine_PoLineId'
       )
        CREATE INDEX IX_PurchaseReceiveLine_PoLineId
            ON dbo.PurchaseReceiveLine(PoLineId,IsDeleted);

    IF OBJECT_ID(N'dbo.InventoryTransaction',N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.InventoryTransaction',N'BizLineId') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1 FROM sys.indexes
           WHERE object_id=OBJECT_ID(N'dbo.InventoryTransaction')
             AND name=N'IX_InventoryTransaction_BizLineId'
       )
        CREATE INDEX IX_InventoryTransaction_BizLineId
            ON dbo.InventoryTransaction(BizLineId,IsDeleted);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
