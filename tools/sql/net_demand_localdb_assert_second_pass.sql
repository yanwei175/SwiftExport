/*
    第二轮 006/007 后断言，也可对 .bak 恢复库重复执行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'SwiftExportNetDemandTest[_]%'
    THROW 51840,N'Refusing to assert migration outside SwiftExportNetDemandTest_*.',1;
IF OBJECT_ID(N'dbo.__Gate_Product',N'U') IS NULL
    THROW 51841,N'First-pass snapshot is missing.',1;

IF EXISTS(SELECT * FROM dbo.Product EXCEPT SELECT * FROM dbo.__Gate_Product)
 OR EXISTS(SELECT * FROM dbo.__Gate_Product EXCEPT SELECT * FROM dbo.Product)
    THROW 51842,N'Second pass changed Product business or audit data.',1;
IF EXISTS(SELECT * FROM dbo.NetDemandAllocation EXCEPT SELECT * FROM dbo.__Gate_NetDemandAllocation)
 OR EXISTS(SELECT * FROM dbo.__Gate_NetDemandAllocation EXCEPT SELECT * FROM dbo.NetDemandAllocation)
    THROW 51843,N'Second pass changed NetDemandAllocation business or audit data.',1;
IF EXISTS(SELECT * FROM dbo.NetDemandPlanCommandLog EXCEPT SELECT * FROM dbo.__Gate_NetDemandPlanCommandLog)
 OR EXISTS(SELECT * FROM dbo.__Gate_NetDemandPlanCommandLog EXCEPT SELECT * FROM dbo.NetDemandPlanCommandLog)
    THROW 51844,N'Second pass changed NetDemandPlanCommandLog data.',1;
IF EXISTS(SELECT * FROM dbo.DemandExecutionLink EXCEPT SELECT * FROM dbo.__Gate_DemandExecutionLink)
 OR EXISTS(SELECT * FROM dbo.__Gate_DemandExecutionLink EXCEPT SELECT * FROM dbo.DemandExecutionLink)
    THROW 51845,N'Second pass changed DemandExecutionLink business or audit data.',1;
IF EXISTS(SELECT * FROM dbo.PurchaseOrderHead EXCEPT SELECT * FROM dbo.__Gate_PurchaseOrderHead)
 OR EXISTS(SELECT * FROM dbo.__Gate_PurchaseOrderHead EXCEPT SELECT * FROM dbo.PurchaseOrderHead)
    THROW 51846,N'Second pass changed PurchaseOrderHead business or audit data.',1;
IF EXISTS(SELECT * FROM dbo.PurchaseOrderLine EXCEPT SELECT * FROM dbo.__Gate_PurchaseOrderLine)
 OR EXISTS(SELECT * FROM dbo.__Gate_PurchaseOrderLine EXCEPT SELECT * FROM dbo.PurchaseOrderLine)
    THROW 51847,N'Second pass changed PurchaseOrderLine business or audit data.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'QTY'
      AND precision=18 AND scale=4 AND is_nullable=0
)
    THROW 51848,N'PurchaseOrderLine.QTY is not decimal(18,4) NOT NULL.',1;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'UX_PurchaseOrderLine_OrderNoSku'
)
    THROW 51849,N'Legacy PurchaseOrderLine (OrderNo,Sku) unique index still exists.',1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine')
      AND name=N'UX_PurchaseOrderLine_HeadLineNumber' AND is_unique=1 AND has_filter=1
)
    THROW 51850,N'PurchaseOrderLine head/line active unique index is missing.',1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.NetDemandPlanCommandLog')
      AND name=N'UX_NetDemandPlanCommandLog_CommandAllocation' AND is_unique=1 AND has_filter=1
)
    THROW 51851,N'NetDemandPlanCommandLog idempotency unique index is missing.',1;
IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ExecutionRequestId') IS NULL
 OR COL_LENGTH(N'dbo.PurchaseOrderHead',N'GenerationRequestId') IS NOT NULL
    THROW 51858,N'PurchaseOrderHead must use ExecutionRequestId and must not expose GenerationRequestId.',1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderHead')
      AND name=N'UX_PurchaseOrderHead_ExecutionRequestSourceSupplier'
      AND is_unique=1 AND has_filter=1
)
    THROW 51859,N'PurchaseOrderHead execution request/source/supplier unique index is missing.',1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.NetDemandPlanCommandLog')
      AND name=N'CK_NetDemandPlanCommandLog_Outcome' AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51852,N'NetDemandPlanCommandLog outcome constraint is missing or untrusted.',1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Product p
    WHERE p.Uid='20000000-0000-0000-0000-000000000001'
      AND p.DefaultSupplierId='30000000-0000-0000-0000-000000000001'
      AND p.DefaultSupplierCode=N'S-A'
)
 OR NOT EXISTS
(
    SELECT 1
    FROM dbo.Product p
    WHERE p.Uid='20000000-0000-0000-0000-000000000002'
      AND p.DefaultSupplierId='30000000-0000-0000-0000-000000000002'
      AND p.DefaultSupplierCode=N'S-B'
)
    THROW 51853,N'Provable Product default supplier backfill is incorrect.',1;

IF (SELECT COUNT_BIG(1) FROM dbo.NetDemandAllocation WHERE LifecycleStatus=0 AND IsDeleted=0)<>2
    THROW 51854,N'Expected exactly two active A/B allocations.',1;
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.NetDemandAllocation b
    JOIN dbo.NetDemandAllocation a ON a.Uid=b.DeliveryTargetAllocationId
    WHERE b.NetDemandId='40000000-0000-0000-0000-000000000002'
      AND a.NetDemandId='40000000-0000-0000-0000-000000000001'
      AND b.PurchaseSupplierId='30000000-0000-0000-0000-000000000002'
      AND b.DeliveryTargetSupplierId='30000000-0000-0000-0000-000000000001'
      AND b.DeliverySupplierCodeSnapshot=N'S-A'
)
    THROW 51855,N'Customer-supplied B allocation target or supplier is incorrect.',1;
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DemandExecutionLink l
    JOIN dbo.NetDemandAllocation a ON a.Uid=l.NetDemandAllocationId
    JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId
    WHERE l.Uid='70000000-0000-0000-0000-000000000001'
      AND a.NetDemandId=l.DependentDemandId
      AND pol.Uid='60000000-0000-0000-0000-000000000001'
)
    THROW 51856,N'Active execution Link was not mapped to its provable allocation and purchase line.',1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name IN
    (
        N'FK_NetDemandAllocation_NetDemand',
        N'FK_NetDemandAllocation_DeliveryTarget',
        N'FK_NetDemandAllocation_Replaces',
        N'FK_DemandExecutionLink_NetDemandAllocation'
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
 OR (SELECT COUNT(1) FROM sys.foreign_keys WHERE name IN
    (
        N'FK_NetDemandAllocation_NetDemand',
        N'FK_NetDemandAllocation_DeliveryTarget',
        N'FK_NetDemandAllocation_Replaces',
        N'FK_DemandExecutionLink_NetDemandAllocation'
    ))<>4
    THROW 51857,N'Net-demand foreign keys are missing, disabled, or untrusted.',1;

SELECT N'LOCALDB_GATE_ASSERTIONS_PASSED' AS GateResult,
       DB_NAME() AS DatabaseName,
       (SELECT COUNT_BIG(1) FROM dbo.NetDemandAllocation WHERE LifecycleStatus=0 AND IsDeleted=0) AS ActiveAllocationCount,
       (SELECT COUNT_BIG(1) FROM dbo.DemandExecutionLink WHERE LinkStatus=0 AND IsDeleted=0) AS ActiveLinkCount;
