/*
  供应商虚拟仓与库存内核。仅供人工审核后在隔离库/目标库执行；不会创建同义物理表。
  执行前先运行同目录的 011_InventoryVirtualWarehouse_Preflight.sql。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Warehouse',N'U') IS NULL OR OBJECT_ID(N'dbo.Inventory',N'U') IS NULL OR OBJECT_ID(N'dbo.InventoryTransaction',N'U') IS NULL
        THROW 51100,N'库存迁移阻断：Warehouse、Inventory、InventoryTransaction 必须已存在。',1;

    -- 遗留仓无法证明供应商归属时按 Internal 保留，绝不猜测为供应商虚拟仓。
    IF COL_LENGTH(N'dbo.Warehouse',N'SiteType') IS NULL ALTER TABLE dbo.Warehouse ADD SiteType tinyint NOT NULL CONSTRAINT DF_Warehouse_SiteType_011 DEFAULT(1) WITH VALUES;
    IF COL_LENGTH(N'dbo.Warehouse',N'SupplierId') IS NULL ALTER TABLE dbo.Warehouse ADD SupplierId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.Warehouse',N'SupplierCodeSnapshot') IS NULL ALTER TABLE dbo.Warehouse ADD SupplierCodeSnapshot nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.Warehouse',N'SupplierNameSnapshot') IS NULL ALTER TABLE dbo.Warehouse ADD SupplierNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.Warehouse',N'IsActive') IS NULL ALTER TABLE dbo.Warehouse ADD IsActive bit NOT NULL CONSTRAINT DF_Warehouse_IsActive_011 DEFAULT(1) WITH VALUES;
    IF OBJECT_ID(N'dbo.CK_Warehouse_SiteTypeSupplier_011',N'C') IS NULL EXEC(N'ALTER TABLE dbo.Warehouse WITH CHECK ADD CONSTRAINT CK_Warehouse_SiteTypeSupplier_011 CHECK((SiteType=0 AND SupplierId IS NOT NULL) OR (SiteType=1 AND SupplierId IS NULL));');

    IF COL_LENGTH(N'dbo.Inventory',N'WarehouseId') IS NULL ALTER TABLE dbo.Inventory ADD WarehouseId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.Inventory',N'ProductId') IS NULL ALTER TABLE dbo.Inventory ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.Inventory',N'UnitSnapshot') IS NULL ALTER TABLE dbo.Inventory ADD UnitSnapshot nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.Inventory',N'LastLedgerSequence') IS NULL ALTER TABLE dbo.Inventory ADD LastLedgerSequence bigint NOT NULL CONSTRAINT DF_Inventory_LastLedgerSequence_011 DEFAULT(0) WITH VALUES;
    IF COL_LENGTH(N'dbo.Inventory',N'Version') IS NULL ALTER TABLE dbo.Inventory ADD Version int NOT NULL CONSTRAINT DF_Inventory_Version_011 DEFAULT(1) WITH VALUES;

    IF COL_LENGTH(N'dbo.InventoryTransaction',N'Sequence') IS NULL ALTER TABLE dbo.InventoryTransaction ADD Sequence bigint NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'WarehouseId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD WarehouseId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'ProductId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'TransactionType') IS NULL ALTER TABLE dbo.InventoryTransaction ADD TransactionType smallint NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'QuantityDelta') IS NULL ALTER TABLE dbo.InventoryTransaction ADD QuantityDelta decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'BalanceAfter') IS NULL ALTER TABLE dbo.InventoryTransaction ADD BalanceAfter decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'SourceDocumentType') IS NULL ALTER TABLE dbo.InventoryTransaction ADD SourceDocumentType nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'SourceDocumentId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD SourceDocumentId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'SourceLineId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD SourceLineId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'ReversalOfLedgerId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD ReversalOfLedgerId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'CommandId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD CommandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'CommandItemId') IS NULL ALTER TABLE dbo.InventoryTransaction ADD CommandItemId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'Reason') IS NULL ALTER TABLE dbo.InventoryTransaction ADD Reason nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'OccurredDate') IS NULL ALTER TABLE dbo.InventoryTransaction ADD OccurredDate datetime2(7) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'WarehouseCodeSnapshot') IS NULL ALTER TABLE dbo.InventoryTransaction ADD WarehouseCodeSnapshot nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'SkuSnapshot') IS NULL ALTER TABLE dbo.InventoryTransaction ADD SkuSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.InventoryTransaction',N'UnitSnapshot') IS NULL ALTER TABLE dbo.InventoryTransaction ADD UnitSnapshot nvarchar(50) NULL;

    IF OBJECT_ID(N'dbo.InventoryLedgerSequence',N'SO') IS NULL EXEC(N'CREATE SEQUENCE dbo.InventoryLedgerSequence AS bigint START WITH 1 INCREMENT BY 1;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Warehouse') AND name=N'UX_Warehouse_ActiveSupplierVirtual') EXEC(N'CREATE UNIQUE INDEX UX_Warehouse_ActiveSupplierVirtual ON dbo.Warehouse(SiteType,SupplierId) WHERE IsActive=1 AND IsDeleted=0 AND SupplierId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Warehouse') AND name=N'UX_Warehouse_ActiveCode') EXEC(N'CREATE UNIQUE INDEX UX_Warehouse_ActiveCode ON dbo.Warehouse(WhseCode) WHERE IsActive=1 AND IsDeleted=0;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Inventory') AND name=N'UX_Inventory_Warehouse_Product') EXEC(N'CREATE UNIQUE INDEX UX_Inventory_Warehouse_Product ON dbo.Inventory(WarehouseId,ProductId) WHERE IsDeleted=0 AND WarehouseId IS NOT NULL AND ProductId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryTransaction') AND name=N'UX_InventoryTransaction_CommandItem') EXEC(N'CREATE UNIQUE INDEX UX_InventoryTransaction_CommandItem ON dbo.InventoryTransaction(CommandItemId) WHERE IsDeleted=0 AND CommandItemId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryTransaction') AND name=N'IX_InventoryTransaction_Warehouse_Product_Sequence') EXEC(N'CREATE INDEX IX_InventoryTransaction_Warehouse_Product_Sequence ON dbo.InventoryTransaction(WarehouseId,ProductId,Sequence DESC) INCLUDE(TransactionType,QuantityDelta,BalanceAfter,OccurredDate,RefNo);');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
