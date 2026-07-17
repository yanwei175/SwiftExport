SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    IF OBJECT_ID(N'dbo.PurchaseOrderHead',N'U') IS NULL OR OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NULL
        THROW 51010,N'采购字段兼容迁移阻断：采购头行物理表不存在。',1;

    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'BoNo') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD BoNo nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'SalesOrderNo') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD SalesOrderNo nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'CustomerPoNo') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD CustomerPoNo nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'CustomerCode') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD CustomerCode nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'SoPerson') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD SoPerson nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'PoPerson') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD PoPerson nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'InletrHoseType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD InletrHoseType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'AeratorType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD AeratorType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'PalletType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD PalletType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'BoxType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD BoxType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'LabelType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD LabelType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'IMType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD IMType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'CartonType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD CartonType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'LogoType') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD LogoType nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ReviewedBy') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD ReviewedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'PoFolder') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD PoFolder nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ZheKouBeiZhu') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD ZheKouBeiZhu nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ReplyDate') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD ReplyDate datetime2(7) NULL;

    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'CustomerSku') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD CustomerSku nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'LinkSku') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD LinkSku nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'SupplierSku') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD SupplierSku nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DescriptionZh') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD DescriptionZh nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'FinishZh') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD FinishZh nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'FeaturesZh') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD FeaturesZh nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'MaterialType') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD MaterialType nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'SupplyType') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD SupplyType nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'RecQty') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD RecQty decimal(18,4) NOT NULL CONSTRAINT DF_PurchaseOrderLine_RecQty_010 DEFAULT(0);
    IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'LastRecDate') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD LastRecDate datetime2(7) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
