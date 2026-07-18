/*
    015_PurchaseOrder_Rebuild_DevelopmentOnly.sql

    用途：仅供开发库把采购头行重建为当前 C# 权威模型。
    风险：会删除 PurchaseOrderHead、PurchaseOrderLine 的全部数据。
    约束：本脚本不属于生产升级路径；006-010 等历史兼容迁移不得在本脚本之后重新执行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowDestructiveRebuild bit = 0;
IF @AllowDestructiveRebuild <> 1
    THROW 51500, N'默认禁止重建采购表。仅确认当前是可丢弃数据的开发库后，将 @AllowDestructiveRebuild 改为 1。', 1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NOT NULL
    DELETE FROM dbo.DemandExecutionLink WHERE TargetType=N'PO';

IF OBJECT_ID(N'dbo.FollowUpLog', N'U') IS NOT NULL
    DELETE FROM dbo.FollowUpLog WHERE RefLineType=N'PurchaseOrderLine';

IF OBJECT_ID(N'dbo.PurchaseOrderLine', N'U') IS NOT NULL
    DROP TABLE dbo.PurchaseOrderLine;

IF OBJECT_ID(N'dbo.PurchaseOrderHead', N'U') IS NOT NULL
    DROP TABLE dbo.PurchaseOrderHead;

CREATE TABLE dbo.PurchaseOrderHead
(
    Uid uniqueidentifier NOT NULL,
    Version int NOT NULL CONSTRAINT DF_PurchaseOrderHead_Version_015 DEFAULT(1),
    IsDeleted bit NOT NULL CONSTRAINT DF_PurchaseOrderHead_IsDeleted_015 DEFAULT(0),
    CreatedDate datetime2(7) NULL,
    CreatedBy nvarchar(100) NULL,
    UpdatedDate datetime2(7) NULL,
    UpdatedBy nvarchar(100) NULL,

    ConfirmationStatus smallint NOT NULL CONSTRAINT DF_PurchaseOrderHead_ConfirmationStatus_015 DEFAULT(0),
    LifecycleStatus smallint NOT NULL CONSTRAINT DF_PurchaseOrderHead_LifecycleStatus_015 DEFAULT(0),
    OrderNo nvarchar(100) NOT NULL,
    BoNo nvarchar(100) NULL,
    CustomerPoNo nvarchar(100) NULL,
    CustomerCode nvarchar(100) NULL,
    Currency nvarchar(10) NOT NULL CONSTRAINT DF_PurchaseOrderHead_Currency_015 DEFAULT(N'CNY'),

    SupplierId uniqueidentifier NOT NULL,
    SupplierCode nvarchar(100) NOT NULL,
    SupplierName nvarchar(300) NULL,
    DemandSourceType nvarchar(30) NOT NULL,
    DemandSourceUid uniqueidentifier NOT NULL,
    DemandSourceNo nvarchar(100) NOT NULL,
    ExecutionRequestId uniqueidentifier NOT NULL,

    SoPerson nvarchar(100) NULL,
    PoPerson nvarchar(100) NULL,
    InletrHoseType nvarchar(200) NULL,
    AeratorType nvarchar(200) NULL,
    PalletType nvarchar(200) NULL,
    BoxType nvarchar(200) NULL,
    LabelType nvarchar(200) NULL,
    IMType nvarchar(200) NULL,
    CartonType nvarchar(200) NULL,
    LogoType nvarchar(200) NULL,
    ReviewedBy nvarchar(100) NULL,
    PoFolder nvarchar(500) NULL,
    ZheKouBeiZhu nvarchar(1000) NULL,
    Remark nvarchar(2000) NULL,

    OrderDate datetime2(7) NOT NULL,
    ExpectedDate datetime2(7) NULL,
    ReplyDate datetime2(7) NULL,
    ConfirmedDate datetime2(7) NULL,
    ConfirmedBy nvarchar(100) NULL,

    CONSTRAINT PK_PurchaseOrderHead_015 PRIMARY KEY CLUSTERED(Uid),
    CONSTRAINT CK_PurchaseOrderHead_Version_015 CHECK(Version>0),
    CONSTRAINT CK_PurchaseOrderHead_ConfirmationStatus_015 CHECK(ConfirmationStatus IN (0,1)),
    CONSTRAINT CK_PurchaseOrderHead_LifecycleStatus_015 CHECK(LifecycleStatus IN (0,8,9)),
    CONSTRAINT CK_PurchaseOrderHead_Identity_015 CHECK(
        LEN(LTRIM(RTRIM(OrderNo)))>0
        AND SupplierId<>'00000000-0000-0000-0000-000000000000'
        AND LEN(LTRIM(RTRIM(SupplierCode)))>0
        AND LEN(LTRIM(RTRIM(DemandSourceType)))>0
        AND DemandSourceUid<>'00000000-0000-0000-0000-000000000000'
        AND LEN(LTRIM(RTRIM(DemandSourceNo)))>0
        AND ExecutionRequestId<>'00000000-0000-0000-0000-000000000000')
);

CREATE TABLE dbo.PurchaseOrderLine
(
    Uid uniqueidentifier NOT NULL,
    Version int NOT NULL CONSTRAINT DF_PurchaseOrderLine_Version_015 DEFAULT(1),
    IsDeleted bit NOT NULL CONSTRAINT DF_PurchaseOrderLine_IsDeleted_015 DEFAULT(0),
    CreatedDate datetime2(7) NULL,
    CreatedBy nvarchar(100) NULL,
    UpdatedDate datetime2(7) NULL,
    UpdatedBy nvarchar(100) NULL,

    PurchaseOrderId uniqueidentifier NOT NULL,
    LineNumber int NOT NULL,
    OrderNo nvarchar(100) NOT NULL,
    ProductId uniqueidentifier NULL,
    Sku nvarchar(200) NOT NULL,
    CustomerSku nvarchar(200) NULL,
    LinkSku nvarchar(200) NULL,
    SupplierSku nvarchar(200) NULL,
    DescriptionZh nvarchar(1000) NULL,
    FinishZh nvarchar(500) NULL,
    FeaturesZh nvarchar(1000) NULL,
    UnitSnapshot nvarchar(50) NULL,
    QTY decimal(18,4) NOT NULL,
    UnitPrice decimal(18,6) NULL,
    TaxPrice decimal(18,6) NULL,
    UnitAmount decimal(18,4) NULL,
    TaxAmount decimal(18,4) NULL,
    PlannedDeliveryDate datetime2(7) NULL,
    DeliveryTargetAllocationId uniqueidentifier NULL,
    DeliveryTargetSupplierId uniqueidentifier NULL,
    DeliverySupplierCodeSnapshot nvarchar(100) NULL,
    DeliverySupplierNameSnapshot nvarchar(300) NULL,
    SubNote nvarchar(1000) NULL,
    SupplyType nvarchar(50) NULL,
    MaterialType nvarchar(100) NULL,
    SourceType nvarchar(30) NOT NULL,
    SourceNo nvarchar(100) NOT NULL,
    SourceUid uniqueidentifier NULL,
    SourceLineId uniqueidentifier NULL,
    SourceLineNumber int NULL,

    CONSTRAINT PK_PurchaseOrderLine_015 PRIMARY KEY CLUSTERED(Uid),
    CONSTRAINT FK_PurchaseOrderLine_Head_015 FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrderHead(Uid),
    CONSTRAINT CK_PurchaseOrderLine_Version_015 CHECK(Version>0),
    CONSTRAINT CK_PurchaseOrderLine_LineNumber_015 CHECK(LineNumber>0),
    CONSTRAINT CK_PurchaseOrderLine_Quantity_015 CHECK(QTY>0),
    CONSTRAINT CK_PurchaseOrderLine_DeliveryTarget_015 CHECK(
        (DeliveryTargetAllocationId IS NULL AND DeliveryTargetSupplierId IS NULL)
        OR (DeliveryTargetAllocationId IS NOT NULL AND DeliveryTargetSupplierId IS NOT NULL))
);

CREATE UNIQUE INDEX UX_PurchaseOrderHead_OrderNo_Active_015
    ON dbo.PurchaseOrderHead(OrderNo) WHERE IsDeleted=0;

CREATE UNIQUE INDEX UX_PurchaseOrderHead_ExecutionScope_Active_015
    ON dbo.PurchaseOrderHead(ExecutionRequestId,DemandSourceType,DemandSourceUid,SupplierId)
    WHERE IsDeleted=0;

CREATE UNIQUE INDEX UX_PurchaseOrderLine_HeadLineNumber_Active_015
    ON dbo.PurchaseOrderLine(PurchaseOrderId,LineNumber) WHERE IsDeleted=0;

CREATE INDEX IX_PurchaseOrderLine_Head_015
    ON dbo.PurchaseOrderLine(PurchaseOrderId,IsDeleted)
    INCLUDE(LineNumber,Sku,QTY,PlannedDeliveryDate);

COMMIT TRANSACTION;

SELECT N'PurchaseOrderHead' AS TableName, COUNT_BIG(1) AS RowCount FROM dbo.PurchaseOrderHead
UNION ALL
SELECT N'PurchaseOrderLine', COUNT_BIG(1) FROM dbo.PurchaseOrderLine;
