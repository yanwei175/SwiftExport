-- 部署新版服务前由操作员手工执行；保留全部历史值及现有索引。
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.SupplierProgressReport', N'U') IS NULL
        THROW 50001, N'缺少 dbo.SupplierProgressReport，请确认目标数据库。', 1;

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.SupplierProgressReport')
                 AND name = N'ReadyQty' AND is_nullable = 0)
    BEGIN
        IF OBJECT_ID(N'dbo.CK_SupplierProgress_Qty', N'C') IS NOT NULL
            ALTER TABLE dbo.SupplierProgressReport DROP CONSTRAINT CK_SupplierProgress_Qty;
        ALTER TABLE dbo.SupplierProgressReport ALTER COLUMN ReadyQty decimal(18,4) NULL;
    END;

    IF OBJECT_ID(N'dbo.CK_SupplierProgress_Qty', N'C') IS NULL
        ALTER TABLE dbo.SupplierProgressReport WITH CHECK
            ADD CONSTRAINT CK_SupplierProgress_Qty CHECK (ReadyQty IS NULL OR ReadyQty >= 0);
    ALTER TABLE dbo.SupplierProgressReport WITH CHECK CHECK CONSTRAINT CK_SupplierProgress_Qty;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
