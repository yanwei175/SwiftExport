/*
    Core 订单实体优化迁移脚本
    - 新增净需求执行关系表 DemandExecutionLink
    - 新增正式出库单头/行表 OutboundHead、OutboundLine
    - 补齐 Bom、PackingListLine、PurchaseReceiveLine 的追溯和行号字段
    - 本脚本不删除 BillOfMaterial 表；如生产库存在历史表，单独确认后再清理
*/

IF OBJECT_ID('dbo.DemandExecutionLink', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DemandExecutionLink
    (
        Uid UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DemandExecutionLink PRIMARY KEY,
        Version INT NOT NULL CONSTRAINT DF_DemandExecutionLink_Version DEFAULT(1),
        IsDeleted BIT NOT NULL CONSTRAINT DF_DemandExecutionLink_IsDeleted DEFAULT(0),
        CreatedDate DATETIME2 NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedDate DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,

        DependentDemandId UNIQUEIDENTIFIER NOT NULL,
        TargetType NVARCHAR(50) NOT NULL,
        TargetHeadId UNIQUEIDENTIFIER NULL,
        TargetNo NVARCHAR(50) NULL,
        TargetLineId UNIQUEIDENTIFIER NULL,
        ExecutedQty DECIMAL(18, 4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ExecutedQty DEFAULT(0),
        ReceivedQty DECIMAL(18, 4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ReceivedQty DEFAULT(0),
        ReadyQty DECIMAL(18, 4) NOT NULL CONSTRAINT DF_DemandExecutionLink_ReadyQty DEFAULT(0),
        Remark NVARCHAR(500) NULL
    );

    CREATE INDEX IX_DemandExecutionLink_DependentDemandId
        ON dbo.DemandExecutionLink(DependentDemandId)
        WHERE IsDeleted = 0;

    CREATE INDEX IX_DemandExecutionLink_Target
        ON dbo.DemandExecutionLink(TargetType, TargetLineId)
        WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.OutboundHead', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboundHead
    (
        Uid UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OutboundHead PRIMARY KEY,
        Version INT NOT NULL CONSTRAINT DF_OutboundHead_Version DEFAULT(1),
        IsDeleted BIT NOT NULL CONSTRAINT DF_OutboundHead_IsDeleted DEFAULT(0),
        CreatedDate DATETIME2 NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedDate DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,

        OutboundNo NVARCHAR(50) NOT NULL,
        OutboundDate DATETIME2 NULL,
        BizType NVARCHAR(50) NULL,
        SourceType NVARCHAR(50) NULL,
        SourceNo NVARCHAR(50) NULL,
        WarehouseCode NVARCHAR(50) NULL,
        LocationCode NVARCHAR(50) NULL,
        Handler NVARCHAR(100) NULL,
        IsPosted BIT NOT NULL CONSTRAINT DF_OutboundHead_IsPosted DEFAULT(0),
        PostedDate DATETIME2 NULL,
        PostedBy NVARCHAR(100) NULL,
        Remark NVARCHAR(500) NULL
    );

    CREATE UNIQUE INDEX UX_OutboundHead_OutboundNo
        ON dbo.OutboundHead(OutboundNo)
        WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.OutboundLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboundLine
    (
        Uid UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OutboundLine PRIMARY KEY,
        Version INT NOT NULL CONSTRAINT DF_OutboundLine_Version DEFAULT(1),
        IsDeleted BIT NOT NULL CONSTRAINT DF_OutboundLine_IsDeleted DEFAULT(0),
        CreatedDate DATETIME2 NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedDate DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,

        LineNumber INT NOT NULL CONSTRAINT DF_OutboundLine_LineNumber DEFAULT(1),
        OutboundId UNIQUEIDENTIFIER NOT NULL,
        OutboundNo NVARCHAR(50) NOT NULL,
        Sku NVARCHAR(100) NOT NULL,
        Qty DECIMAL(18, 4) NOT NULL CONSTRAINT DF_OutboundLine_Qty DEFAULT(0),
        Unit NVARCHAR(30) NULL,
        WarehouseCode NVARCHAR(50) NULL,
        LocationCode NVARCHAR(50) NULL,
        SourceType NVARCHAR(50) NULL,
        SourceNo NVARCHAR(50) NULL,
        SourceLineId UNIQUEIDENTIFIER NULL,
        RefNo NVARCHAR(50) NULL,
        Remark NVARCHAR(500) NULL
    );

    CREATE INDEX IX_OutboundLine_OutboundId_LineNumber
        ON dbo.OutboundLine(OutboundId, LineNumber)
        WHERE IsDeleted = 0;

    CREATE INDEX IX_OutboundLine_Source
        ON dbo.OutboundLine(SourceType, SourceNo, SourceLineId)
        WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.Bom', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Bom', 'BomVersion') IS NULL
        ALTER TABLE dbo.Bom ADD BomVersion INT NOT NULL CONSTRAINT DF_Bom_BomVersion DEFAULT(0);

    IF COL_LENGTH('dbo.Bom', 'ParentSkuId') IS NULL
        ALTER TABLE dbo.Bom ADD ParentSkuId UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.Bom', 'ParentSku') IS NULL
        ALTER TABLE dbo.Bom ADD ParentSku NVARCHAR(100) NOT NULL CONSTRAINT DF_Bom_ParentSku DEFAULT(N'');

    IF COL_LENGTH('dbo.Bom', 'LineNumber') IS NULL
        ALTER TABLE dbo.Bom ADD LineNumber INT NOT NULL CONSTRAINT DF_Bom_LineNumber DEFAULT(1);

    IF COL_LENGTH('dbo.Bom', 'SubSkuId') IS NOT NULL
        ALTER TABLE dbo.Bom ALTER COLUMN SubSkuId UNIQUEIDENTIFIER NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bom_BomNo_LineNumber' AND object_id = OBJECT_ID('dbo.Bom'))
        CREATE INDEX IX_Bom_BomNo_LineNumber
            ON dbo.Bom(BomNo, LineNumber)
            WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.PackingListLine', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PackingListLine', 'LineNumber') IS NULL
        ALTER TABLE dbo.PackingListLine ADD LineNumber INT NOT NULL CONSTRAINT DF_PackingListLine_LineNumber DEFAULT(1);

    IF COL_LENGTH('dbo.PackingListLine', 'SourceType') IS NULL
        ALTER TABLE dbo.PackingListLine ADD SourceType NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.PackingListLine', 'SourceNo') IS NULL
        ALTER TABLE dbo.PackingListLine ADD SourceNo NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.PackingListLine', 'SourceLineId') IS NULL
        ALTER TABLE dbo.PackingListLine ADD SourceLineId UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.PackingListLine', 'DependentDemandId') IS NULL
        ALTER TABLE dbo.PackingListLine ADD DependentDemandId UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.PackingListLine', 'PurchaseOrderLineId') IS NULL
        ALTER TABLE dbo.PackingListLine ADD PurchaseOrderLineId UNIQUEIDENTIFIER NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PackingListLine_Head_LineNumber' AND object_id = OBJECT_ID('dbo.PackingListLine'))
        CREATE INDEX IX_PackingListLine_Head_LineNumber
            ON dbo.PackingListLine(PackingListHeadId, LineNumber)
            WHERE IsDeleted = 0;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PackingListLine_Source' AND object_id = OBJECT_ID('dbo.PackingListLine'))
        CREATE INDEX IX_PackingListLine_Source
            ON dbo.PackingListLine(SourceType, SourceNo, SourceLineId)
            WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.PurchaseReceiveLine', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PurchaseReceiveLine', 'LineNumber') IS NULL
        ALTER TABLE dbo.PurchaseReceiveLine ADD LineNumber INT NOT NULL CONSTRAINT DF_PurchaseReceiveLine_LineNumber DEFAULT(1);

    IF COL_LENGTH('dbo.PurchaseReceiveLine', 'WarehouseCode') IS NULL
        ALTER TABLE dbo.PurchaseReceiveLine ADD WarehouseCode NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.PurchaseReceiveLine', 'LocationCode') IS NULL
        ALTER TABLE dbo.PurchaseReceiveLine ADD LocationCode NVARCHAR(50) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseReceiveLine_ReceiveId_LineNumber' AND object_id = OBJECT_ID('dbo.PurchaseReceiveLine'))
        CREATE INDEX IX_PurchaseReceiveLine_ReceiveId_LineNumber
            ON dbo.PurchaseReceiveLine(ReceiveId, LineNumber)
            WHERE IsDeleted = 0;
END;
