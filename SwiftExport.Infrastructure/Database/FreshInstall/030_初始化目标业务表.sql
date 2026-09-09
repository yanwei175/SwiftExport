/*
  SwiftExport 全新目标数据库业务表初始化（SQL Server / dbo）。
  本脚本只能在刚创建的空目标数据库中执行，不读取、不修改旧数据库，也不包含 DROP/DELETE/TRUNCATE。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowInitializeNewDatabase bit=1;
DECLARE @ExpectedDatabaseName sysname=N'dv_next';

IF @AllowInitializeNewDatabase<>1 THROW 52100,N'新数据库初始化门禁未开启。',1;
IF NULLIF(@ExpectedDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedDatabaseName
    THROW 52101,N'当前数据库名与 @ExpectedDatabaseName 不一致。',1;
IF @@TRANCOUNT<>0
    THROW 52102,N'当前连接存在未结束事务；请先处理原事务。',1;
IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0)
    THROW 52103,N'目标数据库不是空库，禁止初始化。',1;

BEGIN TRY
BEGIN TRAN;

CREATE TABLE dbo.SupplierProduct
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_SupplierProduct_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_SupplierProduct_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 SupplierId uniqueidentifier NOT NULL,SupplierCode nvarchar(100) NOT NULL,SkuId uniqueidentifier NOT NULL,ProductSku nvarchar(200) NOT NULL,SupplierSku nvarchar(200) NOT NULL,SupplierSkuDescriptionZh nvarchar(300) NULL,SupplierSkuFeaturesZh nvarchar(300) NULL,UnitPrice decimal(18,6) NULL,TaxPrice decimal(18,6) NULL,
 CONSTRAINT PK_SupplierProduct PRIMARY KEY(Uid),CONSTRAINT CK_SupplierProduct_Version CHECK(Version>0)
);
CREATE UNIQUE INDEX UX_SupplierProduct_Supplier_Product ON dbo.SupplierProduct(SupplierId,SkuId) WHERE IsDeleted=0;
CREATE INDEX IX_SupplierProduct_Product ON dbo.SupplierProduct(SkuId) WHERE IsDeleted=0;

CREATE TABLE dbo.ProductMaster
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_ProductMaster_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_ProductMaster_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 MasterSku nvarchar(200) NOT NULL,DefaultDescriptionEn nvarchar(300) NULL,DefaultDescriptionZh nvarchar(300) NULL,DefaultFeaturesEn nvarchar(500) NULL,DefaultFeaturesZh nvarchar(500) NULL,DefaultPcsPerPackage int NOT NULL CONSTRAINT DF_ProductMaster_Pcs DEFAULT(1),DefaultNetWeight decimal(18,4) NULL,DefaultGrossWeight decimal(18,4) NULL,DefaultCbm decimal(18,6) NULL,ProductType nvarchar(100) NULL,ProductTag nvarchar(100) NULL,CategoryId uniqueidentifier NOT NULL,DefaultSupplierId uniqueidentifier NOT NULL,
 CONSTRAINT PK_ProductMaster PRIMARY KEY(Uid),CONSTRAINT CK_ProductMaster_Version CHECK(Version>0),CONSTRAINT CK_ProductMaster_Pcs CHECK(DefaultPcsPerPackage>0),CONSTRAINT CK_ProductMaster_Measures CHECK((DefaultNetWeight IS NULL OR DefaultNetWeight>=0) AND (DefaultGrossWeight IS NULL OR DefaultGrossWeight>=0) AND (DefaultCbm IS NULL OR DefaultCbm>=0))
);
CREATE UNIQUE INDEX UX_ProductMaster_MasterSku ON dbo.ProductMaster(MasterSku) WHERE IsDeleted=0;
CREATE INDEX IX_ProductMaster_Category ON dbo.ProductMaster(CategoryId) WHERE IsDeleted=0;
CREATE INDEX IX_ProductMaster_DefaultSupplier ON dbo.ProductMaster(DefaultSupplierId) WHERE IsDeleted=0;

CREATE TABLE dbo.ProductNeutral
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_ProductNeutral_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_ProductNeutral_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 ProductCode nvarchar(200) NOT NULL,ProductMasterId uniqueidentifier NOT NULL,ColorId uniqueidentifier NOT NULL,DefaultSalesPriceUsd decimal(18,6) NULL,DefaultExchangeRate decimal(18,8) NULL,ProfitMargin decimal(18,6) NULL,DefaultPurchasePriceCny decimal(18,6) NULL,ImagePath nvarchar(1000) NULL,
 CONSTRAINT PK_ProductNeutral PRIMARY KEY(Uid),CONSTRAINT CK_ProductNeutral_Version CHECK(Version>0),CONSTRAINT CK_ProductNeutral_Prices CHECK((DefaultSalesPriceUsd IS NULL OR DefaultSalesPriceUsd>=0) AND (DefaultPurchasePriceCny IS NULL OR DefaultPurchasePriceCny>=0)),CONSTRAINT CK_ProductNeutral_ExchangeRate CHECK(DefaultExchangeRate IS NULL OR DefaultExchangeRate>0)
);
CREATE UNIQUE INDEX UX_ProductNeutral_ProductCode ON dbo.ProductNeutral(ProductCode) WHERE IsDeleted=0;
CREATE INDEX IX_ProductNeutral_ProductMaster ON dbo.ProductNeutral(ProductMasterId) WHERE IsDeleted=0;
CREATE INDEX IX_ProductNeutral_Color ON dbo.ProductNeutral(ColorId) WHERE IsDeleted=0;

CREATE TABLE dbo.ProductPackage
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_ProductPackage_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_ProductPackage_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 ProductId uniqueidentifier NULL,PackageType nvarchar(100) NOT NULL,PcsPerPackage int NOT NULL CONSTRAINT DF_ProductPackage_Pcs DEFAULT(1),Length_CM decimal(18,4) NULL,Width_CM decimal(18,4) NULL,Height_CM decimal(18,4) NULL,Nw_KG decimal(18,4) NULL,Gw_KG decimal(18,4) NULL,Cbm decimal(18,4) NULL,IsDefault bit NOT NULL CONSTRAINT DF_ProductPackage_Default DEFAULT(0),PackagingMaterialSku nvarchar(200) NULL,Note nvarchar(2000) NULL,
 CONSTRAINT PK_ProductPackage PRIMARY KEY(Uid),CONSTRAINT CK_ProductPackage_Version CHECK(Version>0),CONSTRAINT CK_ProductPackage_Pcs CHECK(PcsPerPackage>0)
);
CREATE UNIQUE INDEX UX_ProductPackage_Default ON dbo.ProductPackage(ProductId) WHERE IsDeleted=0 AND IsDefault=1;

CREATE TABLE dbo.Bom
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_Bom_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_Bom_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 BomNo nvarchar(100) NOT NULL,BomVersion int NOT NULL,ParentProductId uniqueidentifier NULL,ParentSkuSnapshot nvarchar(200) NOT NULL,LineNumber int NOT NULL,ComponentProductId uniqueidentifier NULL,ComponentSkuSnapshot nvarchar(200) NOT NULL,Numerator int NOT NULL CONSTRAINT DF_Bom_Numerator DEFAULT(1),Denominator int NOT NULL CONSTRAINT DF_Bom_Denominator DEFAULT(1),Note nvarchar(2000) NULL,
 CONSTRAINT PK_Bom PRIMARY KEY(Uid),CONSTRAINT CK_Bom_Version CHECK(Version>0),CONSTRAINT CK_Bom_Line CHECK(LineNumber>0),CONSTRAINT CK_Bom_Ratio CHECK(Numerator>0 AND Denominator>0)
);
CREATE UNIQUE INDEX UX_Bom_Parent_Version_Line ON dbo.Bom(ParentProductId,BomVersion,LineNumber) WHERE IsDeleted=0;
CREATE INDEX IX_Bom_Component ON dbo.Bom(ComponentProductId) WHERE IsDeleted=0;

CREATE TABLE dbo.SysDicCurrencyExchange
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_CurrencyExchange_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_CurrencyExchange_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 IsEnabled bit NOT NULL CONSTRAINT DF_CurrencyExchange_Enabled DEFAULT(1),FromCurrencyCode nvarchar(100) NULL,ToCurrencyCode nvarchar(100) NULL,Precision int NOT NULL CONSTRAINT DF_CurrencyExchange_Precision DEFAULT(6),Rate decimal(18,6) NULL,Remark nvarchar(2000) NULL,
 CONSTRAINT PK_SysDicCurrencyExchange PRIMARY KEY(Uid),CONSTRAINT CK_CurrencyExchange_Version CHECK(Version>0),CONSTRAINT CK_CurrencyExchange_Rate CHECK(Rate IS NULL OR Rate>0)
);
CREATE UNIQUE INDEX UX_CurrencyExchange_Pair ON dbo.SysDicCurrencyExchange(FromCurrencyCode,ToCurrencyCode) WHERE IsDeleted=0 AND IsEnabled=1;

CREATE TABLE dbo.DocumentSequence
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_DocumentSequence_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_DocumentSequence_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 DocType nvarchar(100) NOT NULL,YearMonth nvarchar(100) NOT NULL,CurrentNo int NOT NULL,
 CONSTRAINT PK_DocumentSequence PRIMARY KEY(Uid),CONSTRAINT CK_DocumentSequence_Version CHECK(Version>0),CONSTRAINT CK_DocumentSequence_CurrentNo CHECK(CurrentNo>=0)
);
CREATE UNIQUE INDEX UX_DocumentSequence_Type_Month ON dbo.DocumentSequence(DocType,YearMonth) WHERE IsDeleted=0;

CREATE TABLE dbo.SalesOrderHeader
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_SalesOrderHeader_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_SalesOrderHeader_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 DocumentStatus tinyint NOT NULL CONSTRAINT DF_SalesOrderHeader_DocumentStatus DEFAULT(0),ConfirmationStatus tinyint NOT NULL CONSTRAINT DF_SalesOrderHeader_Confirmation DEFAULT(0),OrderNo nvarchar(100) NOT NULL,InvoiceNo nvarchar(100) NULL,CustomerId uniqueidentifier NULL,CustomerCode nvarchar(100) NULL,CustomerPoNo nvarchar(100) NULL,ShippedBy nvarchar(100) NULL,Currency nvarchar(100) NULL,ExchangeRate decimal(18,6) NULL,TotalAmountOriginalCurrency decimal(18,4) NULL,TotalAmountLocalCurrency decimal(18,4) NULL,ShipToName nvarchar(300) NULL,ShipToAddress nvarchar(1000) NULL,ShipToPhone nvarchar(100) NULL,Incoterm nvarchar(100) NULL,IncotermLocation nvarchar(300) NULL,IncotermSortRemark nvarchar(2000) NULL,DepositRate decimal(18,6) NULL,LeadTimeDays int NOT NULL CONSTRAINT DF_SalesOrderHeader_Lead DEFAULT(45),PaymentMethod nvarchar(100) NULL,SoFolder nvarchar(1000) NULL,Remark nvarchar(2000) NULL,OrderDate datetime2(7) NULL,DeliveryDate datetime2(7) NULL,ReplyDate datetime2(7) NULL,ETA datetime2(7) NULL,ETD datetime2(7) NULL,CloseReason nvarchar(2000) NULL,ClosedDate datetime2(7) NULL,ClosedBy nvarchar(100) NULL,
 CONSTRAINT PK_SalesOrderHeader PRIMARY KEY(Uid),CONSTRAINT CK_SalesOrderHeader_Version CHECK(Version>0),CONSTRAINT CK_SalesOrderHeader_Status CHECK(DocumentStatus IN(0,9) AND ConfirmationStatus IN(0,1)),CONSTRAINT CK_SalesOrderHeader_Deposit CHECK(DepositRate IS NULL OR (DepositRate>=0 AND DepositRate<=1))
);
CREATE UNIQUE INDEX UX_SalesOrderHeader_OrderNo ON dbo.SalesOrderHeader(OrderNo) WHERE IsDeleted=0;

CREATE TABLE dbo.SalesOrderLine
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_SalesOrderLine_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_SalesOrderLine_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 LineNumber int NOT NULL,SalesOrderId uniqueidentifier NOT NULL,OrderNo nvarchar(100) NULL,IsNonStandard bit NOT NULL CONSTRAINT DF_SalesOrderLine_NonStandard DEFAULT(0),ProductId uniqueidentifier NULL,CustomerProductId uniqueidentifier NULL,Sku nvarchar(200) NOT NULL,LinkSku nvarchar(200) NULL,CustomerSku nvarchar(200) NULL,DescriptionEn nvarchar(300) NULL,ColorNameEn nvarchar(300) NULL,FeaturesEn nvarchar(300) NULL,CnyPrice decimal(18,6) NULL,UsdPrice decimal(18,6) NULL,Qty decimal(18,4) NOT NULL,CnyAmount decimal(18,4) NULL,UsdAmount decimal(18,4) NULL,PcsOfCarton decimal(18,4) NULL,NwOfCarton decimal(18,4) NULL,GwOfCarton decimal(18,4) NULL,CbmOfCarton decimal(18,4) NULL,Ctns decimal(18,4) NULL,NwSubTotal decimal(18,4) NULL,GwSubTotal decimal(18,4) NULL,CbmSubTotal decimal(18,4) NULL,SubNote nvarchar(2000) NULL,ExpectedDeliveryDate datetime2(7) NULL,MainImage nvarchar(1000) NULL,
 CONSTRAINT PK_SalesOrderLine PRIMARY KEY(Uid),CONSTRAINT FK_SalesOrderLine_Head FOREIGN KEY(SalesOrderId) REFERENCES dbo.SalesOrderHeader(Uid),CONSTRAINT CK_SalesOrderLine_Version CHECK(Version>0),CONSTRAINT CK_SalesOrderLine_Line CHECK(LineNumber>0),CONSTRAINT CK_SalesOrderLine_Qty CHECK(Qty>0)
);
CREATE UNIQUE INDEX UX_SalesOrderLine_Head_Line ON dbo.SalesOrderLine(SalesOrderId,LineNumber) WHERE IsDeleted=0;

CREATE TABLE dbo.GrossDemand
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_GrossDemand_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_GrossDemand_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 LifecycleStatus tinyint NOT NULL CONSTRAINT DF_GrossDemand_Lifecycle DEFAULT(0),DecompositionStatus tinyint NOT NULL CONSTRAINT DF_GrossDemand_Decomposition DEFAULT(0),DecompositionRevision int NOT NULL CONSTRAINT DF_GrossDemand_Revision DEFAULT(0),SourceType nvarchar(100) NOT NULL,SourceUid uniqueidentifier NOT NULL,SourceNo nvarchar(100) NOT NULL,SourceLineId uniqueidentifier NOT NULL,SourceLineNumber int NULL,SourceVersion int NULL,DemandBasisFingerprint nvarchar(64) NULL,CustomerId uniqueidentifier NULL,ProductId uniqueidentifier NULL,IsNonStandard bit NOT NULL,SkuSnapshot nvarchar(200) NOT NULL,LinkSkuSnapshot nvarchar(200) NULL,CustomerSkuSnapshot nvarchar(200) NULL,DescriptionSnapshot nvarchar(300) NULL,ColorSnapshot nvarchar(300) NULL,SpecificationSnapshot nvarchar(300) NULL,ImagePathSnapshot nvarchar(1000) NULL,UnitSnapshot nvarchar(100) NULL,DemandQty decimal(18,4) NOT NULL,RequiredDate datetime2(7) NULL,Remark nvarchar(2000) NULL,DecomposedAt datetime2(7) NULL,DecomposedBy nvarchar(100) NULL,LifecycleChangedAt datetime2(7) NULL,LifecycleChangedBy nvarchar(100) NULL,LifecycleChangedReason nvarchar(2000) NULL,
 CONSTRAINT PK_GrossDemand PRIMARY KEY(Uid),CONSTRAINT CK_GrossDemand_Version CHECK(Version>0),CONSTRAINT CK_GrossDemand_Status CHECK(LifecycleStatus IN(0,8,9) AND DecompositionStatus IN(0,1)),CONSTRAINT CK_GrossDemand_Qty CHECK(DemandQty>0)
);
CREATE UNIQUE INDEX UX_GrossDemand_SourceLine ON dbo.GrossDemand(SourceType,SourceUid,SourceLineId) WHERE IsDeleted=0 AND LifecycleStatus=0;
CREATE INDEX IX_GrossDemand_Source ON dbo.GrossDemand(SourceUid,SourceNo) WHERE IsDeleted=0;

CREATE TABLE dbo.NetDemand
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_NetDemand_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_NetDemand_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 GrossDemandId uniqueidentifier NOT NULL,SourceDemandVersionSnapshot int NULL,SourceVersionSnapshot int NULL,SourceDemandBasisFingerprintSnapshot nvarchar(64) NULL,DecompositionRevision int NOT NULL,LineNumber int NOT NULL,LifecycleStatus tinyint NOT NULL CONSTRAINT DF_NetDemand_Lifecycle DEFAULT(0),ParentNetDemandId uniqueidentifier NULL,RootNetDemandId uniqueidentifier NOT NULL,DeliveryTargetNetDemandId uniqueidentifier NULL,DeliveryMode tinyint NOT NULL CONSTRAINT DF_NetDemand_DeliveryMode DEFAULT(0),ProductId uniqueidentifier NULL,BomLineId uniqueidentifier NULL,BomVersion int NULL,BomPath nvarchar(1000) NULL,MaterialTypeSnapshot nvarchar(100) NULL,SupplyTypeSnapshot nvarchar(100) NOT NULL,SkuSnapshot nvarchar(200) NOT NULL,ParentSkuSnapshot nvarchar(200) NULL,CustomerSkuSnapshot nvarchar(200) NULL,DescriptionSnapshot nvarchar(300) NULL,ColorSnapshot nvarchar(300) NULL,SpecificationSnapshot nvarchar(300) NULL,GrossQty decimal(18,4) NOT NULL,NetQty decimal(18,4) NOT NULL,RequiredDate datetime2(7) NULL,Remark nvarchar(2000) NULL,RolledBackAt datetime2(7) NULL,RolledBackBy nvarchar(100) NULL,RolledBackReason nvarchar(2000) NULL,
 CONSTRAINT PK_NetDemand PRIMARY KEY(Uid),CONSTRAINT CK_NetDemand_Version CHECK(Version>0),CONSTRAINT CK_NetDemand_Status CHECK(LifecycleStatus IN(0,8,9)),CONSTRAINT CK_NetDemand_DeliveryMode CHECK(DeliveryMode IN(0,1)),CONSTRAINT CK_NetDemand_Qty CHECK(GrossQty>=0 AND NetQty>=0)
);
CREATE UNIQUE INDEX UX_NetDemand_Revision_Line ON dbo.NetDemand(GrossDemandId,DecompositionRevision,LineNumber) WHERE IsDeleted=0;
CREATE INDEX IX_NetDemand_Root ON dbo.NetDemand(RootNetDemandId) WHERE IsDeleted=0;
CREATE INDEX IX_NetDemand_GrossDemand_Active ON dbo.NetDemand(GrossDemandId,LifecycleStatus,DecompositionRevision) INCLUDE(SourceDemandVersionSnapshot,SourceVersionSnapshot,SourceDemandBasisFingerprintSnapshot) WHERE IsDeleted=0;

CREATE TABLE dbo.NetDemandAllocation
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_NetDemandAllocation_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_NetDemandAllocation_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 NetDemandId uniqueidentifier NOT NULL,PlanRevision int NOT NULL,Sequence int NOT NULL,LifecycleStatus tinyint NOT NULL CONSTRAINT DF_NetDemandAllocation_Lifecycle DEFAULT(0),ExecutionType tinyint NOT NULL,PlannedQty decimal(18,4) NOT NULL,DemandRequiredDate datetime2(7) NULL,PlannedDeliveryDate datetime2(7) NULL,PurchaseSupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,SupplierNameSnapshot nvarchar(300) NULL,DeliveryTargetAllocationId uniqueidentifier NULL,DeliveryTargetSupplierId uniqueidentifier NULL,DeliverySupplierCodeSnapshot nvarchar(100) NULL,DeliverySupplierNameSnapshot nvarchar(300) NULL,AssignmentSource tinyint NOT NULL,ReplacesAllocationId uniqueidentifier NULL,ChangeReason nvarchar(2000) NULL,CancelReasonCode nvarchar(100) NULL,CancelledAt datetime2(7) NULL,CancelledBy nvarchar(100) NULL,CancelledReason nvarchar(2000) NULL,
 CONSTRAINT PK_NetDemandAllocation PRIMARY KEY(Uid),CONSTRAINT CK_NetDemandAllocation_Version CHECK(Version>0),CONSTRAINT CK_NetDemandAllocation_Status CHECK(LifecycleStatus IN(0,8,9)),CONSTRAINT CK_NetDemandAllocation_Execution CHECK(ExecutionType IN(0,1,2)),CONSTRAINT CK_NetDemandAllocation_Assignment CHECK(AssignmentSource IN(0,1,2,3,4,5)),CONSTRAINT CK_NetDemandAllocation_Qty CHECK(PlannedQty>0)
);
CREATE UNIQUE INDEX UX_NetDemandAllocation_Revision_Sequence ON dbo.NetDemandAllocation(NetDemandId,PlanRevision,Sequence) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_NetDemandAllocation_Active ON dbo.NetDemandAllocation(NetDemandId) WHERE IsDeleted=0 AND LifecycleStatus=0;

CREATE TABLE dbo.NetDemandPlanCommandLog
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_NetDemandPlanCommand_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_NetDemandPlanCommand_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 CommandId uniqueidentifier NOT NULL,OriginalAllocationId uniqueidentifier NOT NULL,CommandType nvarchar(100) NOT NULL,RequestFingerprint nvarchar(300) NOT NULL,Outcome tinyint NOT NULL,NewAllocationId uniqueidentifier NULL,NewAllocationVersion int NOT NULL,ResultMessage nvarchar(2000) NULL,AffectedAllocationIds nvarchar(max) NULL,
 CONSTRAINT PK_NetDemandPlanCommandLog PRIMARY KEY(Uid),CONSTRAINT CK_NetDemandPlanCommand_Version CHECK(Version>0),CONSTRAINT CK_NetDemandPlanCommand_Json CHECK(AffectedAllocationIds IS NULL OR ISJSON(AffectedAllocationIds)=1)
);
CREATE UNIQUE INDEX UX_NetDemandPlanCommand_Command ON dbo.NetDemandPlanCommandLog(CommandId,OriginalAllocationId,CommandType) WHERE IsDeleted=0;

CREATE TABLE dbo.PurchaseOrderHead
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_PurchaseOrderHead_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_PurchaseOrderHead_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 ConfirmationStatus tinyint NOT NULL CONSTRAINT DF_PurchaseOrderHead_Confirmation DEFAULT(0),LifecycleStatus tinyint NOT NULL CONSTRAINT DF_PurchaseOrderHead_Lifecycle DEFAULT(0),OrderNo nvarchar(100) NOT NULL,BoNo nvarchar(100) NULL,CustomerPoNo nvarchar(100) NULL,CustomerCode nvarchar(100) NULL,Currency nvarchar(100) NOT NULL,SupplierId uniqueidentifier NOT NULL,ProductId uniqueidentifier NULL,SupplierCode nvarchar(100) NOT NULL,SupplierName nvarchar(300) NULL,DemandSourceType nvarchar(100) NOT NULL,DemandSourceUid uniqueidentifier NOT NULL,DemandSourceNo nvarchar(100) NOT NULL,ExecutionRequestId uniqueidentifier NOT NULL,SoPerson nvarchar(100) NULL,PoPerson nvarchar(100) NULL,InletrHoseType nvarchar(300) NULL,AeratorType nvarchar(300) NULL,PalletType nvarchar(300) NULL,BoxType nvarchar(300) NULL,LabelType nvarchar(300) NULL,IMType nvarchar(300) NULL,CartonType nvarchar(300) NULL,LogoType nvarchar(300) NULL,ReviewedBy nvarchar(100) NULL,PoFolder nvarchar(1000) NULL,ZheKouBeiZhu nvarchar(2000) NULL,Remark nvarchar(2000) NULL,OrderDate datetime2(7) NOT NULL,ExpectedDate datetime2(7) NULL,ReplyDate datetime2(7) NULL,ConfirmedDate datetime2(7) NULL,ConfirmedBy nvarchar(100) NULL,
 CONSTRAINT PK_PurchaseOrderHead PRIMARY KEY(Uid),CONSTRAINT CK_PurchaseOrderHead_Version CHECK(Version>0),CONSTRAINT CK_PurchaseOrderHead_Status CHECK(ConfirmationStatus IN(0,1) AND LifecycleStatus IN(0,8,9))
);
CREATE UNIQUE INDEX UX_PurchaseOrderHead_OrderNo ON dbo.PurchaseOrderHead(OrderNo) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_PurchaseOrderHead_Request_Supplier ON dbo.PurchaseOrderHead(ExecutionRequestId,SupplierId) WHERE IsDeleted=0 AND LifecycleStatus=0;

CREATE TABLE dbo.PurchaseOrderLine
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_PurchaseOrderLine_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_PurchaseOrderLine_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 PurchaseOrderId uniqueidentifier NOT NULL,LineNumber int NOT NULL,OrderNo nvarchar(100) NOT NULL,ProductId uniqueidentifier NULL,Sku nvarchar(200) NOT NULL,CustomerSku nvarchar(200) NULL,LinkSku nvarchar(200) NULL,SupplierSku nvarchar(200) NULL,DescriptionZh nvarchar(300) NULL,FinishZh nvarchar(300) NULL,FeaturesZh nvarchar(300) NULL,UnitSnapshot nvarchar(100) NULL,QTY decimal(18,4) NOT NULL,UnitPrice decimal(18,6) NULL,TaxPrice decimal(18,6) NULL,UnitAmount decimal(18,4) NULL,TaxAmount decimal(18,4) NULL,PlannedDeliveryDate datetime2(7) NULL,DeliveryTargetAllocationId uniqueidentifier NULL,DeliveryTargetSupplierId uniqueidentifier NULL,DeliverySupplierCodeSnapshot nvarchar(100) NULL,DeliverySupplierNameSnapshot nvarchar(300) NULL,SubNote nvarchar(2000) NULL,SupplyType nvarchar(100) NULL,MaterialType nvarchar(100) NULL,SourceType nvarchar(100) NOT NULL,SourceNo nvarchar(100) NOT NULL,SourceUid uniqueidentifier NULL,SourceLineId uniqueidentifier NULL,SourceLineNumber int NULL,
 CONSTRAINT PK_PurchaseOrderLine PRIMARY KEY(Uid),CONSTRAINT FK_PurchaseOrderLine_Head FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrderHead(Uid),CONSTRAINT CK_PurchaseOrderLine_Version CHECK(Version>0),CONSTRAINT CK_PurchaseOrderLine_Line CHECK(LineNumber>0),CONSTRAINT CK_PurchaseOrderLine_Qty CHECK(QTY>0)
);
CREATE UNIQUE INDEX UX_PurchaseOrderLine_Head_Line ON dbo.PurchaseOrderLine(PurchaseOrderId,LineNumber) WHERE IsDeleted=0;

CREATE TABLE dbo.DemandExecutionLink
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_DemandExecutionLink_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_DemandExecutionLink_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 NetDemandId uniqueidentifier NOT NULL,NetDemandAllocationId uniqueidentifier NOT NULL,ExecutionRequestId uniqueidentifier NOT NULL,CommandItemId uniqueidentifier NOT NULL,TargetType nvarchar(100) NOT NULL,TargetHeadId uniqueidentifier NOT NULL,TargetNo nvarchar(100) NOT NULL,TargetLineId uniqueidentifier NOT NULL,LinkStatus tinyint NOT NULL CONSTRAINT DF_DemandExecutionLink_Status DEFAULT(0),AllocatedQty decimal(18,4) NOT NULL,ReleasedQty decimal(18,4) NOT NULL CONSTRAINT DF_DemandExecutionLink_Released DEFAULT(0),DemandRequiredDate datetime2(7) NULL,PlannedDeliveryDate datetime2(7) NULL,CancelledAt datetime2(7) NULL,CancelledBy nvarchar(100) NULL,CancelledReason nvarchar(2000) NULL,ReversedAt datetime2(7) NULL,ReversedBy nvarchar(100) NULL,ReversedReason nvarchar(2000) NULL,Remark nvarchar(2000) NULL,
 CONSTRAINT PK_DemandExecutionLink PRIMARY KEY(Uid),CONSTRAINT CK_DemandExecutionLink_Version CHECK(Version>0),CONSTRAINT CK_DemandExecutionLink_Status CHECK(LinkStatus IN(0,8,9)),CONSTRAINT CK_DemandExecutionLink_Qty CHECK(AllocatedQty>0 AND ReleasedQty>=0 AND ReleasedQty<=AllocatedQty)
);
CREATE UNIQUE INDEX UX_DemandExecutionLink_CommandItem ON dbo.DemandExecutionLink(CommandItemId) WHERE IsDeleted=0;
CREATE INDEX IX_DemandExecutionLink_Allocation ON dbo.DemandExecutionLink(NetDemandAllocationId,LinkStatus) WHERE IsDeleted=0;
CREATE INDEX IX_DemandExecutionLink_Target ON dbo.DemandExecutionLink(TargetHeadId,TargetLineId) WHERE IsDeleted=0;

CREATE TABLE dbo.SupplierProgressReport
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_SupplierProgress_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_SupplierProgress_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 PurchaseOrderLineId uniqueidentifier NOT NULL,Sequence int NOT NULL,ArrangementStatus tinyint NOT NULL,ReadyQty decimal(18,4) NOT NULL,ExpectedReadyDate datetime2(7) NULL,SupplierReportedDate datetime2(7) NOT NULL,Note nvarchar(2000) NULL,IsCorrection bit NOT NULL CONSTRAINT DF_SupplierProgress_Correction DEFAULT(0),CorrectionReason nvarchar(2000) NULL,CommandId uniqueidentifier NOT NULL,
 CONSTRAINT PK_SupplierProgressReport PRIMARY KEY(Uid),CONSTRAINT FK_SupplierProgressReport_Line FOREIGN KEY(PurchaseOrderLineId) REFERENCES dbo.PurchaseOrderLine(Uid),CONSTRAINT CK_SupplierProgress_Version CHECK(Version>0),CONSTRAINT CK_SupplierProgress_Status CHECK(ArrangementStatus IN(0,1,2,8)),CONSTRAINT CK_SupplierProgress_Qty CHECK(ReadyQty>=0),CONSTRAINT CK_SupplierProgress_Sequence CHECK(Sequence>0)
);
CREATE UNIQUE INDEX UX_SupplierProgress_Line_Sequence ON dbo.SupplierProgressReport(PurchaseOrderLineId,Sequence) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_SupplierProgress_Command ON dbo.SupplierProgressReport(CommandId) WHERE IsDeleted=0;

CREATE TABLE dbo.WarehouseSite
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_WarehouseSite_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_WarehouseSite_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 Code nvarchar(100) NOT NULL,Name nvarchar(300) NOT NULL,SiteType tinyint NOT NULL CONSTRAINT DF_WarehouseSite_Type DEFAULT(0),SupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,SupplierNameSnapshot nvarchar(300) NULL,IsActive bit NOT NULL CONSTRAINT DF_WarehouseSite_Active DEFAULT(1),
 CONSTRAINT PK_WarehouseSite PRIMARY KEY(Uid),CONSTRAINT CK_WarehouseSite_Version CHECK(Version>0),CONSTRAINT CK_WarehouseSite_Type CHECK(SiteType IN(0))
);
CREATE UNIQUE INDEX UX_WarehouseSite_Code ON dbo.WarehouseSite(Code) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_WarehouseSite_ActiveSupplier ON dbo.WarehouseSite(SupplierId,SiteType) WHERE IsDeleted=0 AND IsActive=1 AND SupplierId IS NOT NULL;

CREATE TABLE dbo.InventoryBalance
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_InventoryBalance_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_InventoryBalance_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 WarehouseSiteId uniqueidentifier NOT NULL,ProductId uniqueidentifier NOT NULL,SkuSnapshot nvarchar(200) NULL,UnitSnapshot nvarchar(100) NULL,OnHandQty decimal(18,4) NOT NULL CONSTRAINT DF_InventoryBalance_Qty DEFAULT(0),LastLedgerSequence bigint NOT NULL CONSTRAINT DF_InventoryBalance_Sequence DEFAULT(0),
 CONSTRAINT PK_InventoryBalance PRIMARY KEY(Uid),CONSTRAINT CK_InventoryBalance_Version CHECK(Version>0)
);
CREATE UNIQUE INDEX UX_InventoryBalance_Warehouse_Product ON dbo.InventoryBalance(WarehouseSiteId,ProductId) WHERE IsDeleted=0;
CREATE SEQUENCE dbo.InventoryLedgerSequence AS bigint START WITH 1 INCREMENT BY 1;

CREATE TABLE dbo.InventoryLedgerEntry
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_InventoryLedger_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_InventoryLedger_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 Sequence bigint NOT NULL,WarehouseSiteId uniqueidentifier NOT NULL,ProductId uniqueidentifier NOT NULL,TransactionType int NOT NULL,QuantityDelta decimal(18,4) NOT NULL,BalanceAfter decimal(18,4) NOT NULL,SourceDocumentType nvarchar(100) NOT NULL,SourceDocumentId uniqueidentifier NULL,SourceLineId uniqueidentifier NULL,ReversalOfLedgerId uniqueidentifier NULL,CommandId uniqueidentifier NOT NULL,CommandItemId uniqueidentifier NOT NULL,Reason nvarchar(2000) NULL,OccurredDate datetime2(7) NOT NULL,WarehouseCodeSnapshot nvarchar(100) NULL,SkuSnapshot nvarchar(200) NULL,UnitSnapshot nvarchar(100) NULL,
 CONSTRAINT PK_InventoryLedgerEntry PRIMARY KEY(Uid),CONSTRAINT CK_InventoryLedger_Version CHECK(Version>0),CONSTRAINT CK_InventoryLedger_Immutable CHECK(IsDeleted=0),CONSTRAINT CK_InventoryLedger_Type CHECK(TransactionType IN(10,11,12,20,21,90,100,900)),CONSTRAINT CK_InventoryLedger_Delta CHECK(QuantityDelta<>0)
);
CREATE UNIQUE INDEX UX_InventoryLedger_Sequence ON dbo.InventoryLedgerEntry(Sequence);
CREATE UNIQUE INDEX UX_InventoryLedger_CommandItem ON dbo.InventoryLedgerEntry(CommandItemId);
CREATE INDEX IX_InventoryLedger_Source ON dbo.InventoryLedgerEntry(SourceDocumentId,SourceLineId);

CREATE TABLE dbo.InboundDocument
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_InboundDocument_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_InboundDocument_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 DocumentNo nvarchar(100) NOT NULL,DocumentType int NOT NULL,LifecycleStatus tinyint NOT NULL CONSTRAINT DF_InboundDocument_Lifecycle DEFAULT(0),PostingStatus tinyint NOT NULL CONSTRAINT DF_InboundDocument_Posting DEFAULT(0),WarehouseSiteId uniqueidentifier NOT NULL,WarehouseCodeSnapshot nvarchar(100) NULL,WarehouseNameSnapshot nvarchar(300) NULL,SupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,SupplierNameSnapshot nvarchar(300) NULL,ReceiptDate datetime2(7) NOT NULL,Remark nvarchar(2000) NULL,PostedDate datetime2(7) NULL,PostedBy nvarchar(100) NULL,ReversedDate datetime2(7) NULL,ReversedBy nvarchar(100) NULL,ReverseReason nvarchar(2000) NULL,SourcePackingListId uniqueidentifier NULL,CommandId uniqueidentifier NULL,
 CONSTRAINT PK_InboundDocument PRIMARY KEY(Uid),CONSTRAINT CK_InboundDocument_Version CHECK(Version>0),CONSTRAINT CK_InboundDocument_Type CHECK(DocumentType IN(10,11,12,100)),CONSTRAINT CK_InboundDocument_Lifecycle CHECK(LifecycleStatus IN(0,8,9)),CONSTRAINT CK_InboundDocument_Posting CHECK(PostingStatus IN(0,1,8))
);
CREATE UNIQUE INDEX UX_InboundDocument_No ON dbo.InboundDocument(DocumentNo) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_InboundDocument_Command ON dbo.InboundDocument(CommandId) WHERE IsDeleted=0 AND CommandId IS NOT NULL;

CREATE TABLE dbo.InboundDocumentLine
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_InboundLine_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_InboundLine_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 InboundDocumentId uniqueidentifier NOT NULL,LineNumber int NOT NULL,ProductId uniqueidentifier NOT NULL,SkuSnapshot nvarchar(200) NOT NULL,DescriptionSnapshot nvarchar(300) NULL,UnitSnapshot nvarchar(100) NULL,Quantity decimal(18,4) NOT NULL,SourceType int NOT NULL,SourceUid uniqueidentifier NOT NULL,DemandExecutionLinkId uniqueidentifier NULL,NetDemandAllocationId uniqueidentifier NULL,GrossDemandId uniqueidentifier NULL,SalesOrderNoSnapshot nvarchar(100) NULL,SalesOrderLineNumberSnapshot int NULL,OriginalLineId uniqueidentifier NULL,
 CONSTRAINT PK_InboundDocumentLine PRIMARY KEY(Uid),CONSTRAINT FK_InboundLine_Head FOREIGN KEY(InboundDocumentId) REFERENCES dbo.InboundDocument(Uid),CONSTRAINT CK_InboundLine_Version CHECK(Version>0),CONSTRAINT CK_InboundLine_Line CHECK(LineNumber>0),CONSTRAINT CK_InboundLine_Qty CHECK(Quantity>0),CONSTRAINT CK_InboundLine_Source CHECK(SourceType IN(10,11,12))
);
CREATE UNIQUE INDEX UX_InboundLine_Head_Line ON dbo.InboundDocumentLine(InboundDocumentId,LineNumber) WHERE IsDeleted=0;
CREATE INDEX IX_InboundLine_Source ON dbo.InboundDocumentLine(SourceType,SourceUid) WHERE IsDeleted=0;

CREATE TABLE dbo.PackingListDocument
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_PackingListDocument_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_PackingListDocument_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 PackingListNo nvarchar(100) NOT NULL,CustomerId uniqueidentifier NOT NULL,CustomerCodeSnapshot nvarchar(100) NULL,CustomerNameSnapshot nvarchar(300) NULL,PackingDate datetime2(7) NOT NULL,ShipmentDate datetime2(7) NOT NULL,Etd datetime2(7) NULL,Eta datetime2(7) NULL,InvoiceNo nvarchar(100) NULL,ContainerNo nvarchar(100) NULL,SealNo nvarchar(100) NULL,ShippingMark nvarchar(300) NULL,DeliveryAddressSnapshot nvarchar(1000) NULL,LifecycleStatus tinyint NOT NULL CONSTRAINT DF_PackingListDocument_Lifecycle DEFAULT(0),PostingStatus tinyint NOT NULL CONSTRAINT DF_PackingListDocument_Posting DEFAULT(0),PostedDate datetime2(7) NULL,PostedBy nvarchar(100) NULL,ReversedDate datetime2(7) NULL,ReversedBy nvarchar(100) NULL,ReverseReason nvarchar(2000) NULL,CreateCommandId uniqueidentifier NOT NULL,CreateRequestFingerprint nvarchar(300) NULL,PostCommandId uniqueidentifier NULL,PostPreviewToken nvarchar(300) NULL,ReverseCommandId uniqueidentifier NULL,ReversePreviewToken nvarchar(300) NULL,
 CONSTRAINT PK_PackingListDocument PRIMARY KEY(Uid),CONSTRAINT CK_PackingListDocument_Version CHECK(Version>0),CONSTRAINT CK_PackingListDocument_Lifecycle CHECK(LifecycleStatus IN(0,8,9)),CONSTRAINT CK_PackingListDocument_Posting CHECK(PostingStatus IN(0,1,8))
);
CREATE UNIQUE INDEX UX_PackingListDocument_No ON dbo.PackingListDocument(PackingListNo) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_PackingListDocument_CreateCommand ON dbo.PackingListDocument(CreateCommandId) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_PackingListDocument_PostCommand ON dbo.PackingListDocument(PostCommandId) WHERE PostCommandId IS NOT NULL;
CREATE UNIQUE INDEX UX_PackingListDocument_ReverseCommand ON dbo.PackingListDocument(ReverseCommandId) WHERE ReverseCommandId IS NOT NULL;

CREATE TABLE dbo.PackingListContributionLine
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_PackingContribution_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_PackingContribution_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 PackingListDocumentId uniqueidentifier NOT NULL,LineNumber int NOT NULL,ProductId uniqueidentifier NOT NULL,SkuSnapshot nvarchar(200) NOT NULL,CustomerSkuSnapshot nvarchar(200) NULL,DescriptionSnapshot nvarchar(300) NULL,UnitSnapshot nvarchar(100) NULL,Quantity decimal(18,4) NOT NULL,PackageCount decimal(18,4) NULL,NetWeight decimal(18,4) NULL,GrossWeight decimal(18,4) NULL,Volume decimal(18,4) NULL,Marks nvarchar(300) NULL,Note nvarchar(2000) NULL,GrossDemandId uniqueidentifier NOT NULL,NetDemandId uniqueidentifier NULL,NetDemandAllocationId uniqueidentifier NULL,SourceDemandVersionSnapshot int NOT NULL,SourceVersionSnapshot int NULL,DemandExecutionLinkId uniqueidentifier NULL,SalesOrderId uniqueidentifier NULL,SalesOrderLineId uniqueidentifier NULL,SalesOrderNoSnapshot nvarchar(100) NULL,SalesOrderLineNumberSnapshot int NULL,SourceSupplierId uniqueidentifier NULL,SourceSupplierNameSnapshot nvarchar(300) NULL,WarehouseSiteId uniqueidentifier NOT NULL,WarehouseCodeSnapshot nvarchar(100) NULL,WarehouseNameSnapshot nvarchar(300) NULL,SnapshotRequiredDate datetime2(7) NULL,SupplyType tinyint NOT NULL,CommandItemId uniqueidentifier NOT NULL,
 CONSTRAINT PK_PackingListContributionLine PRIMARY KEY(Uid),CONSTRAINT FK_PackingContribution_Head FOREIGN KEY(PackingListDocumentId) REFERENCES dbo.PackingListDocument(Uid),CONSTRAINT CK_PackingContribution_Version CHECK(Version>0),CONSTRAINT CK_PackingContribution_Line CHECK(LineNumber>0),CONSTRAINT CK_PackingContribution_Qty CHECK(Quantity>0)
);
CREATE UNIQUE INDEX UX_PackingContribution_Head_Line ON dbo.PackingListContributionLine(PackingListDocumentId,LineNumber) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_PackingContribution_CommandItem ON dbo.PackingListContributionLine(CommandItemId) WHERE IsDeleted=0;
CREATE INDEX IX_PackingContribution_Allocation ON dbo.PackingListContributionLine(NetDemandAllocationId) WHERE IsDeleted=0;

CREATE TABLE dbo.OutboundDocument
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_OutboundDocument_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_OutboundDocument_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 DocumentNo nvarchar(100) NOT NULL,DocumentType int NOT NULL,LifecycleStatus tinyint NOT NULL CONSTRAINT DF_OutboundDocument_Lifecycle DEFAULT(0),PostingStatus tinyint NOT NULL,WarehouseSiteId uniqueidentifier NOT NULL,WarehouseCodeSnapshot nvarchar(100) NULL,WarehouseNameSnapshot nvarchar(300) NULL,ShipmentDate datetime2(7) NOT NULL,SourcePackingListId uniqueidentifier NULL,PackingListNoSnapshot nvarchar(100) NULL,PostedDate datetime2(7) NULL,PostedBy nvarchar(100) NULL,ReversedDate datetime2(7) NULL,ReversedBy nvarchar(100) NULL,ReverseReason nvarchar(2000) NULL,CommandId uniqueidentifier NOT NULL,
 CONSTRAINT PK_OutboundDocument PRIMARY KEY(Uid),CONSTRAINT CK_OutboundDocument_Version CHECK(Version>0),CONSTRAINT CK_OutboundDocument_Type CHECK(DocumentType IN(20,21)),CONSTRAINT CK_OutboundDocument_Lifecycle CHECK(LifecycleStatus IN(0,8,9)),CONSTRAINT CK_OutboundDocument_Posting CHECK(PostingStatus IN(1,8))
);
CREATE UNIQUE INDEX UX_OutboundDocument_No ON dbo.OutboundDocument(DocumentNo) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_OutboundDocument_Command ON dbo.OutboundDocument(CommandId) WHERE IsDeleted=0;

CREATE TABLE dbo.OutboundDocumentLine
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_OutboundLine_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_OutboundLine_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 OutboundDocumentId uniqueidentifier NOT NULL,LineNumber int NOT NULL,ProductId uniqueidentifier NOT NULL,SkuSnapshot nvarchar(200) NOT NULL,DescriptionSnapshot nvarchar(300) NULL,UnitSnapshot nvarchar(100) NULL,Quantity decimal(18,4) NOT NULL,PackingListContributionId uniqueidentifier NOT NULL,NetDemandAllocationId uniqueidentifier NULL,GrossDemandId uniqueidentifier NULL,SalesOrderNoSnapshot nvarchar(100) NULL,CommandItemId uniqueidentifier NOT NULL,OriginalLineId uniqueidentifier NULL,
 CONSTRAINT PK_OutboundDocumentLine PRIMARY KEY(Uid),CONSTRAINT FK_OutboundLine_Head FOREIGN KEY(OutboundDocumentId) REFERENCES dbo.OutboundDocument(Uid),CONSTRAINT CK_OutboundLine_Version CHECK(Version>0),CONSTRAINT CK_OutboundLine_Line CHECK(LineNumber>0),CONSTRAINT CK_OutboundLine_Qty CHECK(Quantity>0)
);
CREATE UNIQUE INDEX UX_OutboundLine_Head_Line ON dbo.OutboundDocumentLine(OutboundDocumentId,LineNumber) WHERE IsDeleted=0;
CREATE UNIQUE INDEX UX_OutboundLine_CommandItem ON dbo.OutboundDocumentLine(CommandItemId) WHERE IsDeleted=0;
CREATE INDEX IX_OutboundLine_Allocation ON dbo.OutboundDocumentLine(NetDemandAllocationId) WHERE IsDeleted=0;

CREATE TABLE dbo.BusinessOperationLog
(
 Uid uniqueidentifier NOT NULL,Version int NOT NULL CONSTRAINT DF_BusinessLog_Version DEFAULT(1),IsDeleted bit NOT NULL CONSTRAINT DF_BusinessLog_IsDeleted DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
 CorrelationId nvarchar(100) NOT NULL,CommandId uniqueidentifier NOT NULL,Module nvarchar(100) NOT NULL,AggregateType nvarchar(100) NOT NULL,AggregateUid uniqueidentifier NOT NULL,AggregateNoSnapshot nvarchar(100) NULL,OperationType nvarchar(100) NOT NULL,OperationName nvarchar(300) NOT NULL,Reason nvarchar(2000) NULL,Summary nvarchar(2000) NULL,ChangedFieldsJson nvarchar(max) NULL,RelatedObjectsJson nvarchar(max) NULL,UserUid uniqueidentifier NULL,UserNameSnapshot nvarchar(300) NOT NULL,OccurredDate datetime2(7) NOT NULL,
 CONSTRAINT PK_BusinessOperationLog PRIMARY KEY(Uid),CONSTRAINT CK_BusinessLog_Version CHECK(Version>0),CONSTRAINT CK_BusinessLog_Immutable CHECK(IsDeleted=0),CONSTRAINT CK_BusinessLog_ChangedJson CHECK(ChangedFieldsJson IS NULL OR ISJSON(ChangedFieldsJson)=1),CONSTRAINT CK_BusinessLog_RelatedJson CHECK(RelatedObjectsJson IS NULL OR ISJSON(RelatedObjectsJson)=1)
);
CREATE UNIQUE INDEX UX_BusinessOperationLog_Command ON dbo.BusinessOperationLog(Module,OperationType,CommandId);
CREATE INDEX IX_BusinessOperationLog_Aggregate ON dbo.BusinessOperationLog(AggregateType,AggregateUid,OccurredDate DESC);

COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
