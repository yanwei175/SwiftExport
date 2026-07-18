/* 出库工作台增量结构。只创建新链路所需列和索引，不回填不可证明的遗留数据。 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb') THROW 51700,N'Refusing to run outbound migration against a system database.',1;
IF OBJECT_ID(N'dbo.InventoryTransaction',N'U') IS NULL OR OBJECT_ID(N'dbo.Warehouse',N'U') IS NULL THROW 51701,N'Inventory baseline is missing. Run preflight first.',1;

BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.OutboundHead',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboundHead
    (
        Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_OutboundHead_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_OutboundHead_IsDeleted DEFAULT(0),
        CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
        OutboundNo nvarchar(50) NOT NULL,DocumentType int NOT NULL,LifecycleStatus tinyint NOT NULL,PostingStatus int NOT NULL,
        WarehouseSiteId uniqueidentifier NOT NULL,WarehouseCode nvarchar(50) NULL,WarehouseNameSnapshot nvarchar(200) NULL,OutboundDate datetime2 NOT NULL,
        SourcePackingListId uniqueidentifier NULL,PackingListNoSnapshot nvarchar(100) NULL,PostedDate datetime2 NULL,PostedBy nvarchar(100) NULL,
        ReversedDate datetime2 NULL,ReversedBy nvarchar(100) NULL,ReverseReason nvarchar(1000) NULL,CommandId uniqueidentifier NOT NULL,
        CONSTRAINT PK_OutboundHead_New PRIMARY KEY(Uid)
    );
END
ELSE
BEGIN
    IF COL_LENGTH(N'dbo.OutboundHead',N'Uid') IS NULL ALTER TABLE dbo.OutboundHead ADD Uid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'Version') IS NULL ALTER TABLE dbo.OutboundHead ADD Version int NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'IsDeleted') IS NULL ALTER TABLE dbo.OutboundHead ADD IsDeleted bit NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'DocumentType') IS NULL ALTER TABLE dbo.OutboundHead ADD DocumentType int NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'LifecycleStatus') IS NULL ALTER TABLE dbo.OutboundHead ADD LifecycleStatus tinyint NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'PostingStatus') IS NULL ALTER TABLE dbo.OutboundHead ADD PostingStatus int NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'WarehouseSiteId') IS NULL ALTER TABLE dbo.OutboundHead ADD WarehouseSiteId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'WarehouseCode') IS NULL ALTER TABLE dbo.OutboundHead ADD WarehouseCode nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'WarehouseNameSnapshot') IS NULL ALTER TABLE dbo.OutboundHead ADD WarehouseNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'SourcePackingListId') IS NULL ALTER TABLE dbo.OutboundHead ADD SourcePackingListId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'PackingListNoSnapshot') IS NULL ALTER TABLE dbo.OutboundHead ADD PackingListNoSnapshot nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'PostedDate') IS NULL ALTER TABLE dbo.OutboundHead ADD PostedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'PostedBy') IS NULL ALTER TABLE dbo.OutboundHead ADD PostedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'ReversedDate') IS NULL ALTER TABLE dbo.OutboundHead ADD ReversedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'ReversedBy') IS NULL ALTER TABLE dbo.OutboundHead ADD ReversedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'ReverseReason') IS NULL ALTER TABLE dbo.OutboundHead ADD ReverseReason nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.OutboundHead',N'CommandId') IS NULL ALTER TABLE dbo.OutboundHead ADD CommandId uniqueidentifier NULL;
END;

IF OBJECT_ID(N'dbo.OutboundLine',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboundLine
    (
        Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_OutboundLine_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_OutboundLine_IsDeleted DEFAULT(0),
        CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
        OutboundDocumentId uniqueidentifier NOT NULL,LineNumber int NOT NULL,ProductId uniqueidentifier NOT NULL,Sku nvarchar(255) NOT NULL,
        DescriptionSnapshot nvarchar(500) NULL,UnitSnapshot nvarchar(30) NULL,Quantity decimal(18,4) NOT NULL,Qty decimal(18,4) NULL,
        PackingListContributionId uniqueidentifier NOT NULL,NetDemandAllocationId uniqueidentifier NOT NULL,GrossDemandId uniqueidentifier NULL,
        SalesOrderNoSnapshot nvarchar(100) NULL,CommandItemId uniqueidentifier NOT NULL,OriginalLineId uniqueidentifier NULL,
        CONSTRAINT PK_OutboundLine_New PRIMARY KEY(Uid)
    );
END
ELSE
BEGIN
    IF COL_LENGTH(N'dbo.OutboundLine',N'Uid') IS NULL ALTER TABLE dbo.OutboundLine ADD Uid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'Version') IS NULL ALTER TABLE dbo.OutboundLine ADD Version int NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'IsDeleted') IS NULL ALTER TABLE dbo.OutboundLine ADD IsDeleted bit NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'OutboundDocumentId') IS NULL ALTER TABLE dbo.OutboundLine ADD OutboundDocumentId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'LineNumber') IS NULL ALTER TABLE dbo.OutboundLine ADD LineNumber int NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'ProductId') IS NULL ALTER TABLE dbo.OutboundLine ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'DescriptionSnapshot') IS NULL ALTER TABLE dbo.OutboundLine ADD DescriptionSnapshot nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'UnitSnapshot') IS NULL ALTER TABLE dbo.OutboundLine ADD UnitSnapshot nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'Quantity') IS NULL ALTER TABLE dbo.OutboundLine ADD Quantity decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'PackingListContributionId') IS NULL ALTER TABLE dbo.OutboundLine ADD PackingListContributionId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'NetDemandAllocationId') IS NULL ALTER TABLE dbo.OutboundLine ADD NetDemandAllocationId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'GrossDemandId') IS NULL ALTER TABLE dbo.OutboundLine ADD GrossDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'SalesOrderNoSnapshot') IS NULL ALTER TABLE dbo.OutboundLine ADD SalesOrderNoSnapshot nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'CommandItemId') IS NULL ALTER TABLE dbo.OutboundLine ADD CommandItemId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundLine',N'OriginalLineId') IS NULL ALTER TABLE dbo.OutboundLine ADD OriginalLineId uniqueidentifier NULL;
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundHead') AND name=N'UX_OutboundHead_CommandId_New')
    EXEC(N'CREATE UNIQUE INDEX UX_OutboundHead_CommandId_New ON dbo.OutboundHead(CommandId,WarehouseSiteId,DocumentType) WHERE CommandId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundHead') AND name=N'IX_OutboundHead_PackingList')
    EXEC(N'CREATE INDEX IX_OutboundHead_PackingList ON dbo.OutboundHead(SourcePackingListId,PostingStatus,IsDeleted);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundLine') AND name=N'UX_OutboundLine_CommandItemId_New')
    EXEC(N'CREATE UNIQUE INDEX UX_OutboundLine_CommandItemId_New ON dbo.OutboundLine(CommandItemId) WHERE CommandItemId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundLine') AND name=N'IX_OutboundLine_Allocation')
    EXEC(N'CREATE INDEX IX_OutboundLine_Allocation ON dbo.OutboundLine(NetDemandAllocationId,IsDeleted) INCLUDE(Quantity,OutboundDocumentId);');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
