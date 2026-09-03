-- 独立采购执行增量迁移：保留全部历史订单和旧入口，只给新链增加身份、依据与回执。
-- 必须手工填入当前目标库名；本脚本不会创建数据库，不允许在 master 或误连业务库时运行。
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ExpectedDatabaseName sysname = NULL;
IF @ExpectedDatabaseName IS NULL OR DB_NAME()<>@ExpectedDatabaseName OR DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb')
    THROW 52000,N'请显式填写并核对独立采购迁移目标库。',1;
IF OBJECT_ID(N'dbo.PurchaseOrderHead',N'U') IS NULL OR OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NULL
    OR OBJECT_ID(N'dbo.Product',N'U') IS NULL OR OBJECT_ID(N'dbo.Customer',N'U') IS NULL OR OBJECT_ID(N'dbo.Supplier',N'U') IS NULL
    THROW 52001,N'缺少采购基线或主数据表，禁止迁移。',1;
BEGIN TRY
BEGIN TRANSACTION;


IF COL_LENGTH(N'dbo.Product',N'IsActive') IS NULL
    ALTER TABLE dbo.Product ADD IsActive bit NOT NULL CONSTRAINT DF_Product_ExecutionActive DEFAULT(1) WITH VALUES;

IF COL_LENGTH(N'dbo.Customer',N'IsActive') IS NULL
    ALTER TABLE dbo.Customer ADD IsActive bit NOT NULL CONSTRAINT DF_Customer_ExecutionActive DEFAULT(1) WITH VALUES;

IF COL_LENGTH(N'dbo.Supplier',N'IsActive') IS NULL
    ALTER TABLE dbo.Supplier ADD IsActive bit NOT NULL CONSTRAINT DF_Supplier_ExecutionActive DEFAULT(1) WITH VALUES;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'OriginKind') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD OriginKind int NOT NULL CONSTRAINT DF_PO_OriginKind DEFAULT(0) WITH VALUES;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'CustomerId') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD CustomerId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'CustomerNameSnapshot') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD CustomerNameSnapshot nvarchar(300) NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ActiveConfirmationId') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD ActiveConfirmationId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'PackagingRequirements') IS NULL ALTER TABLE dbo.PurchaseOrderHead ADD PackagingRequirements nvarchar(2000) NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'UnlinkedQty') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD UnlinkedQty decimal(18,4) NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryWarehouseSiteId') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD DeliveryWarehouseSiteId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryWarehouseCodeSnapshot') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD DeliveryWarehouseCodeSnapshot nvarchar(100) NULL;

IF COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryWarehouseNameSnapshot') IS NULL ALTER TABLE dbo.PurchaseOrderLine ADD DeliveryWarehouseNameSnapshot nvarchar(300) NULL;

-- 新手工单真正没有需求来源，不能用空 Guid 或伪造来源代替；历史值原样保留。
ALTER TABLE dbo.PurchaseOrderHead ALTER COLUMN DemandSourceType nvarchar(100) NULL;
ALTER TABLE dbo.PurchaseOrderHead ALTER COLUMN DemandSourceUid uniqueidentifier NULL;
ALTER TABLE dbo.PurchaseOrderHead ALTER COLUMN DemandSourceNo nvarchar(100) NULL;
ALTER TABLE dbo.PurchaseOrderLine ALTER COLUMN SourceType nvarchar(100) NULL;
ALTER TABLE dbo.PurchaseOrderLine ALTER COLUMN SourceNo nvarchar(100) NULL;

IF OBJECT_ID(N'dbo.ProcurementCommandReceipt',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProcurementCommandReceipt(
        Uid uniqueidentifier NOT NULL,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
        CommandId uniqueidentifier NOT NULL,ActorUid uniqueidentifier NOT NULL,PurchaseOrderId uniqueidentifier NOT NULL,
        Operation nvarchar(30) NOT NULL,RequestFingerprint varchar(64) NOT NULL,ResultJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_ProcurementCommandReceipt PRIMARY KEY(Uid),
        CONSTRAINT UX_ProcurementCommandReceipt_Command UNIQUE(CommandId),
        CONSTRAINT FK_ProcurementReceipt_Order FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrderHead(Uid),
        CONSTRAINT CK_ProcurementReceipt_Result CHECK(ISJSON(ResultJson)=1)
    );
END;
IF OBJECT_ID(N'dbo.PurchaseConfirmationLine',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseConfirmationLine(
        Uid uniqueidentifier NOT NULL,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
        ConfirmationId uniqueidentifier NOT NULL,PurchaseOrderId uniqueidentifier NOT NULL,
        PurchaseOrderLineId uniqueidentifier NOT NULL,CommandId uniqueidentifier NOT NULL,RootProductId uniqueidentifier NOT NULL,
        LineVersion int NOT NULL,HasBom bit NOT NULL,Fingerprint varchar(64) NOT NULL,ConfirmedAt datetime2(7) NOT NULL,
        RatioPolicy nvarchar(100) NOT NULL,
        CONSTRAINT PK_PurchaseConfirmationLine PRIMARY KEY(Uid),
        CONSTRAINT UX_PurchaseConfirmationLine_Batch UNIQUE(ConfirmationId,PurchaseOrderLineId),
        CONSTRAINT FK_ConfirmationLine_Order FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrderHead(Uid),
        CONSTRAINT FK_ConfirmationLine_Line FOREIGN KEY(PurchaseOrderLineId) REFERENCES dbo.PurchaseOrderLine(Uid)
    );
    CREATE INDEX IX_PurchaseConfirmationLine_Product ON dbo.PurchaseConfirmationLine(RootProductId);
END;
IF OBJECT_ID(N'dbo.PurchaseConfirmationNode',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseConfirmationNode(
        Uid uniqueidentifier NOT NULL,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,
        ConfirmationLineId uniqueidentifier NOT NULL,Path nvarchar(4000) NOT NULL,
        PathHash AS CONVERT(binary(32),HASHBYTES('SHA2_256',Path)) PERSISTED,
        RootProductId uniqueidentifier NOT NULL,ParentProductId uniqueidentifier NOT NULL,
        ComponentProductId uniqueidentifier NOT NULL,BomLineId uniqueidentifier NOT NULL,
        ParentBomNo nvarchar(100) NOT NULL,ParentBomVersion int NOT NULL,
        ParentSkuSnapshot nvarchar(200) NOT NULL,ComponentSkuSnapshot nvarchar(200) NOT NULL,
        UnitSnapshot nvarchar(100) NOT NULL,MaterialType nvarchar(100) NOT NULL,SupplyType nvarchar(100) NOT NULL,
        Numerator int NOT NULL,Denominator int NOT NULL,CumulativeRatio decimal(18,4) NOT NULL,IsCustomerSupplied bit NOT NULL,
        ConsumptionWarehouseSiteId uniqueidentifier NULL,WarehouseCodeSnapshot nvarchar(100) NULL,WarehouseNameSnapshot nvarchar(300) NULL,
        ConsumptionSupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,SupplierNameSnapshot nvarchar(300) NULL,
        CONSTRAINT PK_PurchaseConfirmationNode PRIMARY KEY(Uid),
        CONSTRAINT FK_ConfirmationNode_Line FOREIGN KEY(ConfirmationLineId) REFERENCES dbo.PurchaseConfirmationLine(Uid),
        CONSTRAINT UX_ConfirmationNode_Path UNIQUE(ConfirmationLineId,PathHash),
        CONSTRAINT CK_ConfirmationNode_Ratio CHECK(Numerator>0 AND Denominator>0 AND CumulativeRatio>0),
        CONSTRAINT CK_ConfirmationNode_Target CHECK(
            (IsCustomerSupplied=1 AND ConsumptionWarehouseSiteId IS NOT NULL AND ConsumptionSupplierId IS NOT NULL)
            OR (IsCustomerSupplied=0 AND ConsumptionWarehouseSiteId IS NULL AND ConsumptionSupplierId IS NULL))
    );
    CREATE INDEX IX_PurchaseConfirmationNode_Product ON dbo.PurchaseConfirmationNode(ComponentProductId);
END;

IF OBJECT_ID(N'dbo.CK_PO_OriginIdentity',N'C') IS NULL
EXEC(N'ALTER TABLE dbo.PurchaseOrderHead WITH CHECK ADD CONSTRAINT CK_PO_OriginIdentity CHECK(
    OriginKind IN(0,1,2) AND
    (OriginKind<>2 OR (CustomerId IS NOT NULL AND CustomerId<>''00000000-0000-0000-0000-000000000000''
        AND SupplierId<>''00000000-0000-0000-0000-000000000000'' AND Currency=N''CNY''
        AND DemandSourceType IS NULL AND DemandSourceUid IS NULL AND DemandSourceNo IS NULL
        AND ((ConfirmationStatus=0 AND ActiveConfirmationId IS NULL) OR (ConfirmationStatus=1 AND ActiveConfirmationId IS NOT NULL)))));');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_PurchaseOrder_FixedIdentity ON dbo.PurchaseOrderHead AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    -- 手工采购的固定商业身份永远不通过更新转换。
    IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Uid=i.Uid WHERE d.OriginKind=2 AND EXISTS(
            SELECT i.OriginKind,i.CustomerId,i.SupplierId,i.OrderNo,i.OrderDate,i.Currency,i.CustomerCode,i.CustomerNameSnapshot,i.SupplierCode,i.SupplierName,i.ExecutionRequestId
            EXCEPT SELECT d.OriginKind,d.CustomerId,d.SupplierId,d.OrderNo,d.OrderDate,d.Currency,d.CustomerCode,d.CustomerNameSnapshot,d.SupplierCode,d.SupplierName,d.ExecutionRequestId))
        THROW 52002,N''手工采购固定身份不可更新。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_PurchaseOrderLine_IndependentIdentity ON dbo.PurchaseOrderLine AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PurchaseOrderHead h ON h.Uid=i.PurchaseOrderId WHERE h.OriginKind=2 AND
       (i.ProductId IS NULL OR i.UnlinkedQty IS NULL OR i.UnlinkedQty<>i.QTY OR i.DeliveryWarehouseSiteId IS NULL OR i.DeliveryTargetSupplierId IS NULL
        OR NULLIF(LTRIM(RTRIM(i.UnitSnapshot)),N'''') IS NULL OR i.DeliveryTargetAllocationId IS NOT NULL
        OR i.SourceType IS NOT NULL OR i.SourceNo IS NOT NULL OR i.SourceUid IS NOT NULL OR i.SourceLineId IS NOT NULL))
        THROW 52003,N''独立采购行需要产品单位、未关联数量和明确收货目标，不接受需求来源。'',1;
    IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Uid=i.Uid JOIN dbo.PurchaseOrderHead h ON h.Uid=d.PurchaseOrderId
       WHERE h.OriginKind=2 AND EXISTS(
           SELECT i.PurchaseOrderId,i.LineNumber,i.ProductId,i.Sku,i.QTY,i.UnlinkedQty,i.UnitSnapshot,i.DeliveryWarehouseSiteId,
               i.DeliveryTargetSupplierId,i.DeliveryWarehouseCodeSnapshot,i.DeliveryWarehouseNameSnapshot,i.DeliverySupplierCodeSnapshot,i.DeliverySupplierNameSnapshot
           EXCEPT SELECT d.PurchaseOrderId,d.LineNumber,d.ProductId,d.Sku,d.QTY,d.UnlinkedQty,d.UnitSnapshot,d.DeliveryWarehouseSiteId,
               d.DeliveryTargetSupplierId,d.DeliveryWarehouseCodeSnapshot,d.DeliveryWarehouseNameSnapshot,d.DeliverySupplierCodeSnapshot,d.DeliverySupplierNameSnapshot))
        THROW 52004,N''独立采购保存不得修改行身份、数量、单位或收货目标。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_DemandExecutionLink_IndependentOwnership ON dbo.DemandExecutionLink AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PurchaseOrderHead h ON h.Uid=i.TargetHeadId WHERE h.OriginKind=2)
        THROW 52005,N''本切片独立采购不接受需求贡献。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_PurchaseConfirmationLine_AppendOnly ON dbo.PurchaseConfirmationLine AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52006,N''确认依据和命令回执只能追加，不可改写历史。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_PurchaseConfirmationNode_AppendOnly ON dbo.PurchaseConfirmationNode AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52006,N''确认依据和命令回执只能追加，不可改写历史。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_ProcurementCommandReceipt_AppendOnly ON dbo.ProcurementCommandReceipt AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52006,N''确认依据和命令回执只能追加，不可改写历史。'',1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_Product_ReferencedUnit ON dbo.Product AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    -- 以稳定产品身份检查引用。流水即使已冲销仍是历史计量依据，不按当前余额解冻。
    IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Uid=i.Uid
      WHERE ISNULL(UPPER(LTRIM(RTRIM(i.Unit))),N'''')<>ISNULL(UPPER(LTRIM(RTRIM(d.Unit))),N'''')
      AND (EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.SalesOrderLine x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.OutboundDocumentLine x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.GrossDemand x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.NetDemand x WHERE x.ProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.Bom x WHERE ISNULL(x.IsDeleted,0)=0 AND (x.ParentProductId=i.Uid OR x.ComponentProductId=i.Uid))
        OR EXISTS(SELECT 1 FROM dbo.PurchaseConfirmationLine x WHERE x.RootProductId=i.Uid)
        OR EXISTS(SELECT 1 FROM dbo.PurchaseConfirmationNode x WHERE x.ParentProductId=i.Uid OR x.ComponentProductId=i.Uid)))
        THROW 52007,N''产品已存在业务或BOM引用，不能修改基础单位。'',1;
END;');

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
