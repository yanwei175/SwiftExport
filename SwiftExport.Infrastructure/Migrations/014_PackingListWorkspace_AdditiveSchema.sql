/* PackingList 工作台增量结构。保留遗留 int Id/PackingListHeadId，新链路使用 Uid/PackingListDocumentId。 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb') THROW 51800,N'Refusing to run packing list migration against a system database.',1;
IF OBJECT_ID(N'dbo.NetDemandAllocation',N'U') IS NULL OR OBJECT_ID(N'dbo.Inventory',N'U') IS NULL THROW 51801,N'Demand/inventory baseline is missing. Run preflight first.',1;

BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.PackingListHead',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingListHead
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PackingListHead_Legacy PRIMARY KEY,
        Uid uniqueidentifier NULL,Version int NULL,IsDeleted bit NULL,CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
        PackingListNo nvarchar(100) NOT NULL,CustomerId uniqueidentifier NULL,CustomerCodeSnapshot nvarchar(50) NULL,CustomerNameSnapshot nvarchar(200) NULL,
        PackingDate datetime2 NULL,ShipmentDate datetime2 NULL,ETD datetime2 NULL,ETA datetime2 NULL,InvoiceNo nvarchar(100) NULL,
        ContainerNo nvarchar(100) NULL,SealNo nvarchar(100) NULL,ShippingMark nvarchar(1000) NULL,DeliveryAddressSnapshot nvarchar(1000) NULL,
        LifecycleStatus int NULL,PostingStatus int NULL,PostedDate datetime2 NULL,PostedBy nvarchar(100) NULL,
        ReversedDate datetime2 NULL,ReversedBy nvarchar(100) NULL,ReverseReason nvarchar(1000) NULL,
        CreateCommandId uniqueidentifier NULL,CreateRequestFingerprint char(64) NULL,PostCommandId uniqueidentifier NULL,PostPreviewToken char(64) NULL,
        ReverseCommandId uniqueidentifier NULL,ReversePreviewToken char(64) NULL
    );
END
ELSE
BEGIN
    IF COL_LENGTH(N'dbo.PackingListHead',N'Uid') IS NULL ALTER TABLE dbo.PackingListHead ADD Uid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'Version') IS NULL ALTER TABLE dbo.PackingListHead ADD Version int NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'IsDeleted') IS NULL ALTER TABLE dbo.PackingListHead ADD IsDeleted bit NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CreatedDate') IS NULL ALTER TABLE dbo.PackingListHead ADD CreatedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CreatedBy') IS NULL ALTER TABLE dbo.PackingListHead ADD CreatedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'UpdatedDate') IS NULL ALTER TABLE dbo.PackingListHead ADD UpdatedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'UpdatedBy') IS NULL ALTER TABLE dbo.PackingListHead ADD UpdatedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CustomerId') IS NULL ALTER TABLE dbo.PackingListHead ADD CustomerId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CustomerCodeSnapshot') IS NULL ALTER TABLE dbo.PackingListHead ADD CustomerCodeSnapshot nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CustomerNameSnapshot') IS NULL ALTER TABLE dbo.PackingListHead ADD CustomerNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ShipmentDate') IS NULL ALTER TABLE dbo.PackingListHead ADD ShipmentDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'LifecycleStatus') IS NULL ALTER TABLE dbo.PackingListHead ADD LifecycleStatus int NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'PostingStatus') IS NULL ALTER TABLE dbo.PackingListHead ADD PostingStatus int NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'PostedDate') IS NULL ALTER TABLE dbo.PackingListHead ADD PostedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'PostedBy') IS NULL ALTER TABLE dbo.PackingListHead ADD PostedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ReversedDate') IS NULL ALTER TABLE dbo.PackingListHead ADD ReversedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ReversedBy') IS NULL ALTER TABLE dbo.PackingListHead ADD ReversedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ReverseReason') IS NULL ALTER TABLE dbo.PackingListHead ADD ReverseReason nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CreateCommandId') IS NULL ALTER TABLE dbo.PackingListHead ADD CreateCommandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'CreateRequestFingerprint') IS NULL ALTER TABLE dbo.PackingListHead ADD CreateRequestFingerprint char(64) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'PostCommandId') IS NULL ALTER TABLE dbo.PackingListHead ADD PostCommandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'PostPreviewToken') IS NULL ALTER TABLE dbo.PackingListHead ADD PostPreviewToken char(64) NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ReverseCommandId') IS NULL ALTER TABLE dbo.PackingListHead ADD ReverseCommandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListHead',N'ReversePreviewToken') IS NULL ALTER TABLE dbo.PackingListHead ADD ReversePreviewToken char(64) NULL;
END;

IF OBJECT_ID(N'dbo.PackingListLine',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingListLine
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PackingListLine_Legacy PRIMARY KEY,PackingListHeadId int NOT NULL,
        Uid uniqueidentifier NULL,Version int NULL,IsDeleted bit NULL,CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
        PackingListDocumentId uniqueidentifier NULL,LineNumber int NULL,ProductId uniqueidentifier NULL,Sku nvarchar(255) NULL,CustomerSku nvarchar(255) NULL,
        DescriptionSnapshot nvarchar(500) NULL,UnitSnapshot nvarchar(30) NULL,Quantity decimal(18,4) NULL,Qty decimal(18,4) NULL,
        PackageCount decimal(18,4) NULL,NetWeight decimal(18,4) NULL,GrossWeight decimal(18,4) NULL,Volume decimal(18,4) NULL,Marks nvarchar(500) NULL,Note nvarchar(1000) NULL,
        GrossDemandId uniqueidentifier NULL,NetDemandId uniqueidentifier NULL,NetDemandAllocationId uniqueidentifier NULL,DemandExecutionLinkId uniqueidentifier NULL,
        SalesOrderId uniqueidentifier NULL,SalesOrderLineId uniqueidentifier NULL,SalesOrderNoSnapshot nvarchar(100) NULL,SalesOrderLineNumberSnapshot int NULL,
        SourceSupplierId uniqueidentifier NULL,SourceSupplierNameSnapshot nvarchar(200) NULL,WarehouseSiteId uniqueidentifier NULL,WarehouseCodeSnapshot nvarchar(50) NULL,WarehouseNameSnapshot nvarchar(200) NULL,
        SnapshotRequiredDate datetime2 NULL,SupplyType tinyint NULL,CommandItemId uniqueidentifier NULL
    );
END
ELSE
BEGIN
    IF COL_LENGTH(N'dbo.PackingListLine',N'Uid') IS NULL ALTER TABLE dbo.PackingListLine ADD Uid uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'Version') IS NULL ALTER TABLE dbo.PackingListLine ADD Version int NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'IsDeleted') IS NULL ALTER TABLE dbo.PackingListLine ADD IsDeleted bit NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'CreatedDate') IS NULL ALTER TABLE dbo.PackingListLine ADD CreatedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'CreatedBy') IS NULL ALTER TABLE dbo.PackingListLine ADD CreatedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'UpdatedDate') IS NULL ALTER TABLE dbo.PackingListLine ADD UpdatedDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'UpdatedBy') IS NULL ALTER TABLE dbo.PackingListLine ADD UpdatedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'PackingListDocumentId') IS NULL ALTER TABLE dbo.PackingListLine ADD PackingListDocumentId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'LineNumber') IS NULL ALTER TABLE dbo.PackingListLine ADD LineNumber int NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'ProductId') IS NULL ALTER TABLE dbo.PackingListLine ADD ProductId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'CustomerSku') IS NULL ALTER TABLE dbo.PackingListLine ADD CustomerSku nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'DescriptionSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD DescriptionSnapshot nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'UnitSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD UnitSnapshot nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'Quantity') IS NULL ALTER TABLE dbo.PackingListLine ADD Quantity decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'PackageCount') IS NULL ALTER TABLE dbo.PackingListLine ADD PackageCount decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'NetWeight') IS NULL ALTER TABLE dbo.PackingListLine ADD NetWeight decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'GrossWeight') IS NULL ALTER TABLE dbo.PackingListLine ADD GrossWeight decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'Volume') IS NULL ALTER TABLE dbo.PackingListLine ADD Volume decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'Marks') IS NULL ALTER TABLE dbo.PackingListLine ADD Marks nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'Note') IS NULL ALTER TABLE dbo.PackingListLine ADD Note nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'GrossDemandId') IS NULL ALTER TABLE dbo.PackingListLine ADD GrossDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'NetDemandId') IS NULL ALTER TABLE dbo.PackingListLine ADD NetDemandId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'NetDemandAllocationId') IS NULL ALTER TABLE dbo.PackingListLine ADD NetDemandAllocationId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'DemandExecutionLinkId') IS NULL ALTER TABLE dbo.PackingListLine ADD DemandExecutionLinkId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SalesOrderId') IS NULL ALTER TABLE dbo.PackingListLine ADD SalesOrderId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SalesOrderLineId') IS NULL ALTER TABLE dbo.PackingListLine ADD SalesOrderLineId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SalesOrderNoSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD SalesOrderNoSnapshot nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SalesOrderLineNumberSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD SalesOrderLineNumberSnapshot int NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SourceSupplierId') IS NULL ALTER TABLE dbo.PackingListLine ADD SourceSupplierId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SourceSupplierNameSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD SourceSupplierNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'WarehouseSiteId') IS NULL ALTER TABLE dbo.PackingListLine ADD WarehouseSiteId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'WarehouseCodeSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD WarehouseCodeSnapshot nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'WarehouseNameSnapshot') IS NULL ALTER TABLE dbo.PackingListLine ADD WarehouseNameSnapshot nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SnapshotRequiredDate') IS NULL ALTER TABLE dbo.PackingListLine ADD SnapshotRequiredDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'SupplyType') IS NULL ALTER TABLE dbo.PackingListLine ADD SupplyType tinyint NULL;
    IF COL_LENGTH(N'dbo.PackingListLine',N'CommandItemId') IS NULL ALTER TABLE dbo.PackingListLine ADD CommandItemId uniqueidentifier NULL;
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'UX_PackingListHead_No_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListHead_No_New ON dbo.PackingListHead(PackingListNo) WHERE Uid IS NOT NULL AND IsDeleted=0 AND LifecycleStatus=0;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'UX_PackingListHead_CreateCommand_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListHead_CreateCommand_New ON dbo.PackingListHead(CreateCommandId) WHERE CreateCommandId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'UX_PackingListHead_PostCommand_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListHead_PostCommand_New ON dbo.PackingListHead(PostCommandId) WHERE PostCommandId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'UX_PackingListHead_ReverseCommand_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListHead_ReverseCommand_New ON dbo.PackingListHead(ReverseCommandId) WHERE ReverseCommandId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'UX_PackingListLine_Allocation_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListLine_Allocation_New ON dbo.PackingListLine(PackingListDocumentId,NetDemandAllocationId) WHERE PackingListDocumentId IS NOT NULL AND IsDeleted=0;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'UX_PackingListLine_CommandItem_New')
    EXEC(N'CREATE UNIQUE INDEX UX_PackingListLine_CommandItem_New ON dbo.PackingListLine(CommandItemId) WHERE CommandItemId IS NOT NULL;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead') AND name=N'IX_PackingListHead_Search_New')
    EXEC(N'CREATE INDEX IX_PackingListHead_Search_New ON dbo.PackingListHead(CustomerId,PostingStatus,ShipmentDate) INCLUDE(PackingListNo,InvoiceNo);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'IX_PackingListLine_Search_New')
    EXEC(N'CREATE INDEX IX_PackingListLine_Search_New ON dbo.PackingListLine(SalesOrderNoSnapshot,SourceSupplierId,WarehouseSiteId) INCLUDE(Sku,Quantity);');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
