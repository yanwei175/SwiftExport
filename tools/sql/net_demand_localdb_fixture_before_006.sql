/*
    净需求迁移一次性 LocalDB 夹具：建立最小 pre-006 结构与可证明 A/B 样本。
    只允许在 SwiftExportNetDemandTest_ 前缀数据库执行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() NOT LIKE N'SwiftExportNetDemandTest[_]%'
    THROW 51800,N'Refusing to create net-demand fixture outside SwiftExportNetDemandTest_*.',1;

IF OBJECT_ID(N'dbo.Product',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.Supplier',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.SupplierProduct',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.IndependentDemand',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.DependentDemand',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.DemandExecutionLink',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.PurchaseOrderHead',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NOT NULL
    THROW 51801,N'Fixture requires an empty disposable database.',1;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE dbo.Supplier
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_Supplier PRIMARY KEY,
        SupplierCode nvarchar(50) NOT NULL,
        SupplierName nvarchar(200) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_Supplier_IsDeleted DEFAULT(0)
    );

    CREATE TABLE dbo.Product
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_Product PRIMARY KEY,
        Sku nvarchar(255) NOT NULL,
        DefaultSupplierCode nvarchar(50) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_Product_IsDeleted DEFAULT(0)
    );

    CREATE TABLE dbo.SupplierProduct
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_SupplierProduct PRIMARY KEY,
        SupplierId uniqueidentifier NOT NULL,
        SkuId uniqueidentifier NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_SupplierProduct_IsDeleted DEFAULT(0)
    );

    CREATE TABLE dbo.IndependentDemand
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_IndependentDemand PRIMARY KEY,
        LifecycleStatus int NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_IndependentDemand_IsDeleted DEFAULT(0)
    );

    CREATE TABLE dbo.DependentDemand
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_DependentDemand PRIMARY KEY,
        Version int NOT NULL CONSTRAINT DF_LocalGate_DependentDemand_Version DEFAULT(1),
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_DependentDemand_IsDeleted DEFAULT(0),
        CreatedDate datetime2 NULL,
        CreatedBy nvarchar(100) NULL,
        IndependentDemandId uniqueidentifier NOT NULL,
        LifecycleStatus int NOT NULL,
        DecompositionRevision int NOT NULL,
        LineNumber int NOT NULL,
        ParentNetDemandId uniqueidentifier NULL,
        RootNetDemandId uniqueidentifier NOT NULL,
        DeliveryTargetNetDemandId uniqueidentifier NULL,
        DeliveryMode int NOT NULL,
        ProductId uniqueidentifier NULL,
        SupplyType nvarchar(20) NOT NULL,
        Sku nvarchar(255) NOT NULL,
        NetQty decimal(18,4) NOT NULL,
        RequiredDate datetime2 NULL,
        PreferredSupplierId uniqueidentifier NULL,
        PreferredSupplierCode nvarchar(50) NULL
    );

    CREATE TABLE dbo.PurchaseOrderHead
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_PurchaseOrderHead PRIMARY KEY,
        OrderNo nvarchar(100) NOT NULL,
        SupplierCode nvarchar(50) NOT NULL,
        SalesOrderNo nvarchar(100) NULL,
        BoNo nvarchar(100) NULL,
        ConfirmationStatus int NOT NULL CONSTRAINT DF_LocalGate_PurchaseOrderHead_ConfirmationStatus DEFAULT(0),
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_PurchaseOrderHead_IsDeleted DEFAULT(0)
    );

    CREATE TABLE dbo.PurchaseOrderLine
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_PurchaseOrderLine PRIMARY KEY,
        PurchaseOrderId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        OrderNo nvarchar(100) NOT NULL,
        Sku nvarchar(255) NOT NULL,
        QTY decimal(10,2) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_PurchaseOrderLine_IsDeleted DEFAULT(0)
    );

    CREATE UNIQUE INDEX UX_PurchaseOrderLine_OrderNoSku
        ON dbo.PurchaseOrderLine(OrderNo,Sku);

    CREATE TABLE dbo.DemandExecutionLink
    (
        Uid uniqueidentifier NOT NULL CONSTRAINT PK_LocalGate_DemandExecutionLink PRIMARY KEY,
        DependentDemandId uniqueidentifier NOT NULL,
        LinkStatus int NOT NULL,
        TargetType nvarchar(30) NOT NULL,
        TargetHeadId uniqueidentifier NOT NULL,
        TargetNo nvarchar(100) NOT NULL,
        TargetLineId uniqueidentifier NOT NULL,
        AllocatedQty decimal(18,4) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LocalGate_DemandExecutionLink_IsDeleted DEFAULT(0)
    );

    CREATE UNIQUE INDEX UX_DemandExecutionLink_ActiveTargetLine
        ON dbo.DemandExecutionLink(DependentDemandId,TargetType,TargetLineId)
        WHERE LinkStatus=0 AND IsDeleted=0;

    INSERT dbo.Supplier(Uid,SupplierCode,SupplierName,IsDeleted)
    VALUES
      ('30000000-0000-0000-0000-000000000001',N'S-A',N'供应商甲',0),
      ('30000000-0000-0000-0000-000000000002',N'S-B',N'供应商乙',0);

    INSERT dbo.Product(Uid,Sku,DefaultSupplierCode,IsDeleted)
    VALUES
      ('20000000-0000-0000-0000-000000000001',N'A-100',N'S-A',0),
      ('20000000-0000-0000-0000-000000000002',N'B-100',N'S-B',0);

    INSERT dbo.SupplierProduct(Uid,SupplierId,SkuId,IsDeleted)
    VALUES
      ('31000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',0),
      ('31000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002',0);

    INSERT dbo.IndependentDemand(Uid,LifecycleStatus,IsDeleted)
    VALUES ('10000000-0000-0000-0000-000000000001',0,0);

    INSERT dbo.DependentDemand
    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,IndependentDemandId,LifecycleStatus,
     DecompositionRevision,LineNumber,ParentNetDemandId,RootNetDemandId,
     DeliveryTargetNetDemandId,DeliveryMode,ProductId,SupplyType,Sku,NetQty,
     RequiredDate,PreferredSupplierId,PreferredSupplierCode)
    VALUES
    ('40000000-0000-0000-0000-000000000001',1,0,'2026-07-17T00:00:00',N'local-gate',
     '10000000-0000-0000-0000-000000000001',0,1,1,NULL,
     '40000000-0000-0000-0000-000000000001',NULL,0,
     '20000000-0000-0000-0000-000000000001',N'采购',N'A-100',10.0000,
     '2026-08-10T00:00:00',NULL,NULL),
    ('40000000-0000-0000-0000-000000000002',1,0,'2026-07-17T00:00:00',N'local-gate',
     '10000000-0000-0000-0000-000000000001',0,1,2,
     '40000000-0000-0000-0000-000000000001',
     '40000000-0000-0000-0000-000000000001',
     '40000000-0000-0000-0000-000000000001',1,
     '20000000-0000-0000-0000-000000000002',N'采购',N'B-100',20.0000,
     '2026-08-10T00:00:00',NULL,NULL);

    INSERT dbo.PurchaseOrderHead
    (Uid,OrderNo,SupplierCode,SalesOrderNo,BoNo,ConfirmationStatus,IsDeleted)
    VALUES
    ('50000000-0000-0000-0000-000000000001',N'PO-LOCAL-001',N'S-A',N'SO-LOCAL-001',N'BO-LOCAL-001',0,0);

    INSERT dbo.PurchaseOrderLine
    (Uid,PurchaseOrderId,LineNumber,OrderNo,Sku,QTY,IsDeleted)
    VALUES
    ('60000000-0000-0000-0000-000000000001',
     '50000000-0000-0000-0000-000000000001',1,N'PO-LOCAL-001',N'A-100',4.25,0);

    INSERT dbo.DemandExecutionLink
    (Uid,DependentDemandId,LinkStatus,TargetType,TargetHeadId,TargetNo,TargetLineId,AllocatedQty,IsDeleted)
    VALUES
    ('70000000-0000-0000-0000-000000000001',
     '40000000-0000-0000-0000-000000000001',0,N'PO',
     '50000000-0000-0000-0000-000000000001',N'PO-LOCAL-001',
     '60000000-0000-0000-0000-000000000001',4.2500,0);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
