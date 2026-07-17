/*
    净需求工作台 Phase 1：仅增加兼容列、NetDemandAllocation 和宽松索引。
    不删除旧列，不改变 IndependentDemand/DependentDemand 物理表名。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 51600,N'Refusing to run net demand migration against a system database.',1;
IF OBJECT_ID(N'dbo.Product',N'U') IS NULL
 OR OBJECT_ID(N'dbo.Supplier',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DependentDemand',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DemandExecutionLink',N'U') IS NULL
 OR OBJECT_ID(N'dbo.PurchaseOrderHead',N'U') IS NULL
 OR OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NULL
    THROW 51601,N'Required SwiftExport tables are missing. Run preflight first.',1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.Product',N'DefaultSupplierId') IS NULL
        ALTER TABLE dbo.Product ADD DefaultSupplierId uniqueidentifier NULL;

    IF OBJECT_ID(N'dbo.NetDemandAllocation',N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.NetDemandAllocation
        (
            Uid uniqueidentifier NOT NULL,
            Version int NOT NULL CONSTRAINT DF_NetDemandAllocation_Version DEFAULT(1),
            IsDeleted bit NOT NULL CONSTRAINT DF_NetDemandAllocation_IsDeleted DEFAULT(0),
            CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,
            UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
            NetDemandId uniqueidentifier NOT NULL,
            PlanRevision int NOT NULL,
            Sequence int NOT NULL,
            LifecycleStatus int NOT NULL CONSTRAINT DF_NetDemandAllocation_LifecycleStatus DEFAULT(0),
            ExecutionType int NOT NULL,
            PlannedQty decimal(18,4) NOT NULL,
            DemandRequiredDate datetime2 NULL,
            PlannedDeliveryDate datetime2 NULL,
            PurchaseSupplierId uniqueidentifier NULL,
            SupplierCodeSnapshot nvarchar(50) NULL,
            SupplierNameSnapshot nvarchar(200) NULL,
            DeliveryTargetAllocationId uniqueidentifier NULL,
            DeliveryTargetSupplierId uniqueidentifier NULL,
            DeliverySupplierCodeSnapshot nvarchar(50) NULL,
            DeliverySupplierNameSnapshot nvarchar(200) NULL,
            AssignmentSource int NOT NULL,
            ReplacesAllocationId uniqueidentifier NULL,
            ChangeReason nvarchar(500) NULL,
            CancelReasonCode nvarchar(50) NULL,
            CancelledAt datetime2 NULL,CancelledBy nvarchar(100) NULL,CancelledReason nvarchar(500) NULL,
            CONSTRAINT PK_NetDemandAllocation PRIMARY KEY(Uid)
        );
    END;

    IF OBJECT_ID(N'dbo.NetDemandPlanCommandLog',N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.NetDemandPlanCommandLog
        (
            Uid uniqueidentifier NOT NULL,
            Version int NOT NULL CONSTRAINT DF_NetDemandPlanCommandLog_Version DEFAULT(1),
            IsDeleted bit NOT NULL CONSTRAINT DF_NetDemandPlanCommandLog_IsDeleted DEFAULT(0),
            CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,
            UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
            CommandId uniqueidentifier NOT NULL,
            OriginalAllocationId uniqueidentifier NOT NULL,
            CommandType nvarchar(50) NOT NULL,
            RequestFingerprint char(64) NOT NULL,
            Outcome int NOT NULL,
            NewAllocationId uniqueidentifier NULL,
            NewAllocationVersion int NOT NULL,
            ResultMessage nvarchar(500) NULL,
            AffectedAllocationIds nvarchar(max) NULL,
            CONSTRAINT PK_NetDemandPlanCommandLog PRIMARY KEY(Uid)
        );
    END;

    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'NetDemandAllocationId') IS NULL
        ALTER TABLE dbo.DemandExecutionLink ADD NetDemandAllocationId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'ExecutionRequestId') IS NULL
        ALTER TABLE dbo.DemandExecutionLink ADD ExecutionRequestId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'CommandItemId') IS NULL
        ALTER TABLE dbo.DemandExecutionLink ADD CommandItemId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'DemandRequiredDate') IS NULL
        ALTER TABLE dbo.DemandExecutionLink ADD DemandRequiredDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.DemandExecutionLink',N'PlannedDeliveryDate') IS NULL
        ALTER TABLE dbo.DemandExecutionLink ADD PlannedDeliveryDate datetime2 NULL;

    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'SupplierId') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD SupplierId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'DemandSourceType') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD DemandSourceType nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'DemandSourceUid') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD DemandSourceUid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'DemandSourceNo') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD DemandSourceNo nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ExecutionRequestId') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD ExecutionRequestId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'LifecycleStatus') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD LifecycleStatus int NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ConfirmationStatus') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD ConfirmationStatus int NULL;

    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'ProductId') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'PlannedDeliveryDate') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD PlannedDeliveryDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryTargetAllocationId') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD DeliveryTargetAllocationId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryTargetSupplierId') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD DeliveryTargetSupplierId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliverySupplierCodeSnapshot') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD DeliverySupplierCodeSnapshot nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliverySupplierNameSnapshot') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD DeliverySupplierNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'SourceUid') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD SourceUid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'SourceLineId') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD SourceLineId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'SourceLineNumber') IS NULL
        ALTER TABLE dbo.PurchaseOrderLine ADD SourceLineNumber int NULL;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Product') AND name=N'IX_Product_DefaultSupplierId')
        EXEC(N'CREATE INDEX IX_Product_DefaultSupplierId ON dbo.Product(DefaultSupplierId) WHERE DefaultSupplierId IS NOT NULL;');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandAllocation') AND name=N'IX_NetDemandAllocation_NetDemandLifecycle')
        CREATE INDEX IX_NetDemandAllocation_NetDemandLifecycle ON dbo.NetDemandAllocation(NetDemandId,LifecycleStatus,IsDeleted);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandAllocation') AND name=N'IX_NetDemandAllocation_PurchaseSupplier')
        CREATE INDEX IX_NetDemandAllocation_PurchaseSupplier ON dbo.NetDemandAllocation(PurchaseSupplierId,LifecycleStatus,IsDeleted);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandAllocation') AND name=N'IX_NetDemandAllocation_DeliveryTarget')
        CREATE INDEX IX_NetDemandAllocation_DeliveryTarget ON dbo.NetDemandAllocation(DeliveryTargetAllocationId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND name=N'IX_DemandExecutionLink_AllocationStatus')
        EXEC(N'CREATE INDEX IX_DemandExecutionLink_AllocationStatus ON dbo.DemandExecutionLink(NetDemandAllocationId,LinkStatus,IsDeleted);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderHead') AND name=N'IX_PurchaseOrderHead_DemandSource')
        EXEC(N'CREATE INDEX IX_PurchaseOrderHead_DemandSource ON dbo.PurchaseOrderHead(DemandSourceType,DemandSourceNo,IsDeleted);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandPlanCommandLog') AND name=N'IX_NetDemandPlanCommandLog_CommandAllocation')
        CREATE INDEX IX_NetDemandPlanCommandLog_CommandAllocation
            ON dbo.NetDemandPlanCommandLog(CommandId,OriginalAllocationId,IsDeleted);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
