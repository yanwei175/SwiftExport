/*
    首次 006/007 后快照。第二轮迁移必须与这些业务/审计行完全一致。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'SwiftExportNetDemandTest[_]%'
    THROW 51820,N'Refusing to capture migration snapshot outside SwiftExportNetDemandTest_*.',1;
IF OBJECT_ID(N'dbo.NetDemandAllocation',N'U') IS NULL
 OR OBJECT_ID(N'dbo.NetDemandPlanCommandLog',N'U') IS NULL
    THROW 51821,N'First migration pass is incomplete.',1;
IF OBJECT_ID(N'dbo.__Gate_Product',N'U') IS NOT NULL
    THROW 51822,N'Migration snapshot already exists.',1;

SELECT * INTO dbo.__Gate_Product FROM dbo.Product;
SELECT * INTO dbo.__Gate_NetDemandAllocation FROM dbo.NetDemandAllocation;
SELECT * INTO dbo.__Gate_NetDemandPlanCommandLog FROM dbo.NetDemandPlanCommandLog;
SELECT * INTO dbo.__Gate_DemandExecutionLink FROM dbo.DemandExecutionLink;
SELECT * INTO dbo.__Gate_PurchaseOrderHead FROM dbo.PurchaseOrderHead;
SELECT * INTO dbo.__Gate_PurchaseOrderLine FROM dbo.PurchaseOrderLine;
