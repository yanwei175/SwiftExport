/*
    Demand Planning Phase 1：仅增加兼容字段、DemandExecutionLink 目标字段和普通索引。
    不删除旧列，不更改 IndependentDemand/DependentDemand 物理表名。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51300, N'Refusing to run demand planning migration against a system database.', 1;

IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
 OR OBJECT_ID(N'dbo.Bom', N'U') IS NULL
 OR OBJECT_ID(N'dbo.IndependentDemand', N'U') IS NULL
 OR OBJECT_ID(N'dbo.DependentDemand', N'U') IS NULL
    THROW 51301, N'Required SwiftExport demand planning tables are missing. Run preflight first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    /* Bom：只补齐版本化、父件和稳定行号。 */
    IF COL_LENGTH(N'dbo.Bom', N'BomVersion') IS NULL
        ALTER TABLE dbo.Bom ADD BomVersion int NULL;
    IF COL_LENGTH(N'dbo.Bom', N'ParentSkuId') IS NULL
        ALTER TABLE dbo.Bom ADD ParentSkuId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.Bom', N'ParentSku') IS NULL
        ALTER TABLE dbo.Bom ADD ParentSku nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.Bom', N'SubSkuId') IS NULL
        ALTER TABLE dbo.Bom ADD SubSkuId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.Bom', N'LineNumber') IS NULL
        ALTER TABLE dbo.Bom ADD LineNumber int NULL;

    /* IndependentDemand：新语义字段，旧字段继续保留。 */
    IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleStatus') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD LifecycleStatus int NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'DecompositionStatus') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD DecompositionStatus int NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'DecompositionRevision') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD DecompositionRevision int NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'ProductId') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'SourceLineNumber') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD SourceLineNumber int NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'LinkSkuSnapshot') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD LinkSkuSnapshot nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'ImagePathSnapshot') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD ImagePathSnapshot nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'DecomposedBy') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD DecomposedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleChangedAt') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD LifecycleChangedAt datetime2 NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleChangedBy') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD LifecycleChangedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleChangedReason') IS NULL
        ALTER TABLE dbo.IndependentDemand ADD LifecycleChangedReason nvarchar(500) NULL;

    /* DependentDemand：新净需求关系和历史版本字段，旧执行字段继续保留。 */
    IF COL_LENGTH(N'dbo.DependentDemand', N'LifecycleStatus') IS NULL
        ALTER TABLE dbo.DependentDemand ADD LifecycleStatus int NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'DecompositionRevision') IS NULL
        ALTER TABLE dbo.DependentDemand ADD DecompositionRevision int NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'LineNumber') IS NULL
        ALTER TABLE dbo.DependentDemand ADD LineNumber int NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'ParentNetDemandId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD ParentNetDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'RootNetDemandId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD RootNetDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'DeliveryTargetNetDemandId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD DeliveryTargetNetDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'DeliveryMode') IS NULL
        ALTER TABLE dbo.DependentDemand ADD DeliveryMode int NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'ProductId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'BomLineId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD BomLineId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'BomVersion') IS NULL
        ALTER TABLE dbo.DependentDemand ADD BomVersion int NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'BomPath') IS NULL
        ALTER TABLE dbo.DependentDemand ADD BomPath nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'MaterialTypeSnapshot') IS NULL
        ALTER TABLE dbo.DependentDemand ADD MaterialTypeSnapshot nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'PreferredSupplierId') IS NULL
        ALTER TABLE dbo.DependentDemand ADD PreferredSupplierId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'PreferredSupplierCode') IS NULL
        ALTER TABLE dbo.DependentDemand ADD PreferredSupplierCode nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'RolledBackAt') IS NULL
        ALTER TABLE dbo.DependentDemand ADD RolledBackAt datetime2 NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'RolledBackBy') IS NULL
        ALTER TABLE dbo.DependentDemand ADD RolledBackBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.DependentDemand', N'RolledBackReason') IS NULL
        ALTER TABLE dbo.DependentDemand ADD RolledBackReason nvarchar(500) NULL;

    /* DemandExecutionLink：无表时创建；旧结构时仅增量补列。 */
    IF OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DemandExecutionLink
        (
            Uid uniqueidentifier NOT NULL,
            Version int NOT NULL CONSTRAINT DF_DemandExecutionLink_Version DEFAULT (1),
            IsDeleted bit NOT NULL CONSTRAINT DF_DemandExecutionLink_IsDeleted DEFAULT (0),
            CreatedDate datetime2 NULL,
            CreatedBy nvarchar(100) NULL,
            UpdatedDate datetime2 NULL,
            UpdatedBy nvarchar(100) NULL,
            DependentDemandId uniqueidentifier NOT NULL,
            TargetType nvarchar(30) NULL,
            TargetHeadId uniqueidentifier NULL,
            TargetNo nvarchar(50) NULL,
            TargetLineId uniqueidentifier NULL,
            ExecutedQty decimal(18,4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ExecutedQty DEFAULT (0),
            ReadyQty decimal(18,4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ReadyQty DEFAULT (0),
            ReceivedQty decimal(18,4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ReceivedQty DEFAULT (0),
            LinkStatus int NULL,
            AllocatedQty decimal(18,4) NULL,
            CompletedQty decimal(18,4) NULL,
            CancelledAt datetime2 NULL,
            CancelledBy nvarchar(100) NULL,
            CancelledReason nvarchar(500) NULL,
            ReversedAt datetime2 NULL,
            ReversedBy nvarchar(100) NULL,
            ReversedReason nvarchar(500) NULL,
            Remark nvarchar(500) NULL,
            CONSTRAINT PK_DemandExecutionLink PRIMARY KEY (Uid)
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'Version') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD Version int NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'IsDeleted') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD IsDeleted bit NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CreatedDate') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CreatedDate datetime2 NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CreatedBy') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CreatedBy nvarchar(100) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'UpdatedDate') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD UpdatedDate datetime2 NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'UpdatedBy') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD UpdatedBy nvarchar(100) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'DependentDemandId') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD DependentDemandId uniqueidentifier NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'TargetType') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD TargetType nvarchar(30) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'TargetHeadId') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD TargetHeadId uniqueidentifier NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'TargetNo') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD TargetNo nvarchar(50) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'TargetLineId') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD TargetLineId uniqueidentifier NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ExecutedQty') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ExecutedQty decimal(18,4) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ReadyQty') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ReadyQty decimal(18,4) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ReceivedQty') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ReceivedQty decimal(18,4) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'LinkStatus') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD LinkStatus int NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'AllocatedQty') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD AllocatedQty decimal(18,4) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CompletedQty') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CompletedQty decimal(18,4) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CancelledAt') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CancelledAt datetime2 NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CancelledBy') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CancelledBy nvarchar(100) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'CancelledReason') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD CancelledReason nvarchar(500) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ReversedAt') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ReversedAt datetime2 NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ReversedBy') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ReversedBy nvarchar(100) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'ReversedReason') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD ReversedReason nvarchar(500) NULL;
        IF COL_LENGTH(N'dbo.DemandExecutionLink', N'Remark') IS NULL
            ALTER TABLE dbo.DemandExecutionLink ADD Remark nvarchar(500) NULL;
    END;

    /* 普通查询索引不依赖历史数据唯一性。 */
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Bom') AND name = N'IX_Bom_DemandPlanningLookup')
        CREATE INDEX IX_Bom_DemandPlanningLookup
            ON dbo.Bom(BomNo, BomVersion, LineNumber);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand') AND name = N'IX_IndependentDemand_DemandPlanningSearch')
        CREATE INDEX IX_IndependentDemand_DemandPlanningSearch
            ON dbo.IndependentDemand(LifecycleStatus, DecompositionStatus, HopefulDate, Uid);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DependentDemand') AND name = N'IX_DependentDemand_GrossDemandLifecycle')
        CREATE INDEX IX_DependentDemand_GrossDemandLifecycle
            ON dbo.DependentDemand(IndependentDemandId, LifecycleStatus, DecompositionRevision);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DemandExecutionLink') AND name = N'IX_DemandExecutionLink_NetDemandStatus')
        CREATE INDEX IX_DemandExecutionLink_NetDemandStatus
            ON dbo.DemandExecutionLink(DependentDemandId, LinkStatus, IsDeleted);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
