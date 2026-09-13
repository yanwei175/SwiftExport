/*
  供应商虚拟仓管理增量保护（SQL Server / dbo）。
  仅针对当前 WarehouseSite / InventoryBalance / InventoryLedgerEntry 体系补索引与唯一约束。
  本脚本可重复执行，但不会由应用自动运行；执行前必须由用户审核并选择隔离数据库。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
BEGIN TRAN;

IF OBJECT_ID(N'dbo.WarehouseSite',N'U') IS NULL
    OR OBJECT_ID(N'dbo.InventoryBalance',N'U') IS NULL
    OR OBJECT_ID(N'dbo.InventoryLedgerEntry',N'U') IS NULL
    OR OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NULL
    OR OBJECT_ID(N'dbo.PurchaseConfirmationNode',N'U') IS NULL
    OR OBJECT_ID(N'dbo.InboundDocument',N'U') IS NULL
    OR OBJECT_ID(N'dbo.OutboundDocument',N'U') IS NULL
    OR OBJECT_ID(N'dbo.PackingListContributionLine',N'U') IS NULL
    OR OBJECT_ID(N'dbo.BusinessOperationLog',N'U') IS NULL
    THROW 53000,N'当前数据库缺少虚拟仓删除保护所需的目标表；禁止按无引用继续部署。',1;

IF COL_LENGTH(N'dbo.WarehouseSite',N'Code') IS NULL
    OR COL_LENGTH(N'dbo.WarehouseSite',N'IsDeleted') IS NULL
    OR COL_LENGTH(N'dbo.WarehouseSite',N'IsActive') IS NULL
    OR COL_LENGTH(N'dbo.WarehouseSite',N'SupplierId') IS NULL
    OR COL_LENGTH(N'dbo.WarehouseSite',N'SiteType') IS NULL
    OR COL_LENGTH(N'dbo.InventoryBalance',N'WarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.InventoryLedgerEntry',N'WarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryWarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.PurchaseConfirmationNode',N'ConsumptionWarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.InboundDocument',N'WarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.OutboundDocument',N'WarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.PackingListContributionLine',N'WarehouseSiteId') IS NULL
    OR COL_LENGTH(N'dbo.BusinessOperationLog',N'Module') IS NULL
    OR COL_LENGTH(N'dbo.BusinessOperationLog',N'CommandId') IS NULL
    OR COL_LENGTH(N'dbo.BusinessOperationLog',N'IsDeleted') IS NULL
    THROW 53001,N'当前业务表缺少虚拟仓管理保护所需的稳定标识列；禁止部署。',1;

IF EXISTS(
    SELECT Code FROM dbo.WarehouseSite
    WHERE ISNULL(IsDeleted,0)=0
    GROUP BY Code HAVING COUNT(*)>1)
    THROW 53002,N'存在未删除虚拟仓代码重复，请先输出异常清单并人工处理。',1;

IF EXISTS(
    SELECT SupplierId,SiteType FROM dbo.WarehouseSite
    WHERE ISNULL(IsDeleted,0)=0 AND IsActive=1 AND SupplierId IS NOT NULL
    GROUP BY SupplierId,SiteType HAVING COUNT(*)>1)
    THROW 53003,N'存在同供应商多活动虚拟仓，请先输出异常清单并人工处理。',1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WarehouseSite') AND name=N'UX_WarehouseSite_Code')
    CREATE UNIQUE INDEX UX_WarehouseSite_Code ON dbo.WarehouseSite(Code) WHERE IsDeleted=0;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WarehouseSite') AND name=N'UX_WarehouseSite_ActiveSupplier')
    CREATE UNIQUE INDEX UX_WarehouseSite_ActiveSupplier ON dbo.WarehouseSite(SupplierId,SiteType)
    WHERE IsDeleted=0 AND IsActive=1 AND SupplierId IS NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'IX_InventoryLedger_WarehouseSite_DeleteGuard')
    CREATE INDEX IX_InventoryLedger_WarehouseSite_DeleteGuard ON dbo.InventoryLedgerEntry(WarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'IX_PurchaseOrderLine_DeliveryWarehouse_DeleteGuard')
    CREATE INDEX IX_PurchaseOrderLine_DeliveryWarehouse_DeleteGuard ON dbo.PurchaseOrderLine(DeliveryWarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PurchaseConfirmationNode') AND name=N'IX_PurchaseConfirmationNode_Warehouse_DeleteGuard')
    CREATE INDEX IX_PurchaseConfirmationNode_Warehouse_DeleteGuard ON dbo.PurchaseConfirmationNode(ConsumptionWarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InboundDocument') AND name=N'IX_InboundDocument_Warehouse_DeleteGuard')
    CREATE INDEX IX_InboundDocument_Warehouse_DeleteGuard ON dbo.InboundDocument(WarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundDocument') AND name=N'IX_OutboundDocument_Warehouse_DeleteGuard')
    CREATE INDEX IX_OutboundDocument_Warehouse_DeleteGuard ON dbo.OutboundDocument(WarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListContributionLine') AND name=N'IX_PackingListContribution_Warehouse_DeleteGuard')
    CREATE INDEX IX_PackingListContribution_Warehouse_DeleteGuard ON dbo.PackingListContributionLine(WarehouseSiteId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BusinessOperationLog') AND name=N'IX_BusinessOperationLog_ModuleCommand')
    CREATE INDEX IX_BusinessOperationLog_ModuleCommand ON dbo.BusinessOperationLog(Module,CommandId)
    INCLUDE(OperationType,OccurredDate) WHERE IsDeleted=0;

COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
