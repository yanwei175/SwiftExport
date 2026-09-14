-- PackingList 空表重建：仅删除 dbo.PackingListDocument、dbo.PackingListContributionLine。
-- 必须在目标数据库中执行，并填写下面的数据库名；本脚本不迁移数据、不创建外键。
-- 新表未接入应用保存/过账入口；旧历史 SQL 不会自动适配。
-- WarehouseSiteRepository 仍查询旧贡献表：执行后需要后续适配仓库引用保护，方可恢复相关操作。
-- 审计/Uid 由应用提供；InvoiceNosSummary 是派生展示，不建列。
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @ExpectedDatabaseName sysname = N'请填写目标数据库名';
IF @ExpectedDatabaseName = N'请填写目标数据库名' OR DB_NAME() <> @ExpectedDatabaseName
    THROW 51000, N'请填写目标数据库名，并切换到同名数据库后执行。', 1;
IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 51001, N'禁止在系统数据库执行。', 1;
IF @@TRANCOUNT <> 0
    THROW 51002, N'请在没有外层事务的独立会话中执行。', 1;

BEGIN TRY
    BEGIN TRAN;
    IF OBJECT_ID(N'dbo.PackingListDocument',N'U') IS NULL
       OR OBJECT_ID(N'dbo.PackingListContributionLine',N'U') IS NULL
        THROW 51003, N'两个旧表必须同时存在；脚本非幂等，不允许重复执行或猜测当前结构。', 1;
    IF OBJECT_ID(N'dbo.PackingListHead') IS NOT NULL OR OBJECT_ID(N'dbo.PackingListLine') IS NOT NULL
        THROW 51004, N'新表名称已被占用，拒绝覆盖。', 1;

    -- 事务级排他表锁：检查为空后持续锁定，阻止并发插入导致误删数据。
    EXEC sys.sp_executesql N'
        IF EXISTS(SELECT 1 FROM dbo.PackingListDocument WITH(TABLOCKX,HOLDLOCK))
            THROW 51005, N''旧装箱单头存在数据，拒绝删除。'', 1;
        IF EXISTS(SELECT 1 FROM dbo.PackingListContributionLine WITH(TABLOCKX,HOLDLOCK))
            THROW 51006, N''旧装箱单行存在数据，拒绝删除。'', 1;';

    IF EXISTS(SELECT 1 FROM sys.foreign_keys
              WHERE referenced_object_id IN (OBJECT_ID(N'dbo.PackingListDocument'),OBJECT_ID(N'dbo.PackingListContributionLine')))
        THROW 51007, N'旧表被外键引用，拒绝自动拆除其他对象，请人工核查。', 1;

    DROP TABLE dbo.PackingListContributionLine;
    DROP TABLE dbo.PackingListDocument;

    CREATE TABLE dbo.PackingListHead
    (
        [Uid] uniqueidentifier NOT NULL,
        [Version] int NOT NULL CONSTRAINT DF_PackingListHead_Version DEFAULT(1),
        [IsDeleted] bit NOT NULL CONSTRAINT DF_PackingListHead_IsDeleted DEFAULT(0),
        [CreatedDate] datetime2(7) NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedDate] datetime2(7) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [PackingListNo] nvarchar(100) NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [CustomerNameSnapshot] nvarchar(300) NULL,
        [CustomerAddressSnapshot] nvarchar(2000) NULL,
        [AttentionSnapshot] nvarchar(2000) NULL,
        [InvoiceDate] datetime2(7) NOT NULL,
        [ContainerNo] nvarchar(100) NULL,
        [SealNo] nvarchar(100) NULL,
        [ShippingMark] nvarchar(2000) NULL,
        [Note] nvarchar(2000) NULL,
        [Etd] datetime2(7) NULL,
        [Eta] datetime2(7) NULL,
        [LifecycleStatus] int NOT NULL CONSTRAINT DF_PackingListHead_LifecycleStatus DEFAULT(0),
        [PostingStatus] int NOT NULL CONSTRAINT DF_PackingListHead_PostingStatus DEFAULT(0),
        [PostingDate] datetime2(7) NULL,
        [PostedDate] datetime2(7) NULL,
        [PostedBy] nvarchar(100) NULL,
        [ReversedDate] datetime2(7) NULL,
        [ReversedBy] nvarchar(100) NULL,
        [ReverseReason] nvarchar(2000) NULL,
        [CreateCommandId] uniqueidentifier NOT NULL,
        [PostCommandId] uniqueidentifier NULL,
        [ReverseCommandId] uniqueidentifier NULL,
        CONSTRAINT PK_PackingListHead PRIMARY KEY CLUSTERED ([Uid]),
        CONSTRAINT CK_PackingListHead_Version CHECK ([Version] >= 1),
        CONSTRAINT CK_PackingListHead_Lifecycle CHECK (LifecycleStatus IN (0,8,9)),
        CONSTRAINT CK_PackingListHead_Posting CHECK (PostingStatus IN (0,1,8))
    );

    CREATE TABLE dbo.PackingListLine
    (
        [Uid] uniqueidentifier NOT NULL,
        [Version] int NOT NULL CONSTRAINT DF_PackingListLine_Version DEFAULT(1),
        [IsDeleted] bit NOT NULL CONSTRAINT DF_PackingListLine_IsDeleted DEFAULT(0),
        [CreatedDate] datetime2(7) NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedDate] datetime2(7) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [PackingListHeadId] uniqueidentifier NOT NULL,
        [LineNumber] int NOT NULL,
        [GrossDemandId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [SkuSnapshot] nvarchar(100) NOT NULL,
        [LinkSkuSnapshot] nvarchar(200) NULL,
        [CustomerSkuSnapshot] nvarchar(200) NULL,
        [DescriptionEnSnapshot] nvarchar(2000) NULL,
        [ColorNameEnSnapshot] nvarchar(100) NULL,
        [UnitSnapshot] nvarchar(100) NULL,
        [Qty] decimal(18,4) NOT NULL,
        [PcsPerPackage] decimal(18,4) NULL,
        [PackageCount] decimal(18,4) NULL,
        [NwPerPackage] decimal(18,4) NULL,
        [GwPerPackage] decimal(18,4) NULL,
        [CbmPerPackage] decimal(18,4) NULL,
        [SubTotalNw] decimal(18,4) NULL,
        [SubTotalGw] decimal(18,4) NULL,
        [SubTotalCbm] decimal(18,4) NULL,
        [CustomerPoNo] nvarchar(200) NULL,
        [InvoiceNo] nvarchar(200) NULL,
        [Marks] nvarchar(2000) NULL,
        [Note] nvarchar(2000) NULL,
        [WarehouseSiteId] uniqueidentifier NULL,
        [WarehouseCodeSnapshot] nvarchar(100) NULL,
        [WarehouseNameSnapshot] nvarchar(300) NULL,
        CONSTRAINT PK_PackingListLine PRIMARY KEY CLUSTERED ([Uid]),
        CONSTRAINT CK_PackingListLine_Version CHECK ([Version] >= 1),
        CONSTRAINT CK_PackingListLine_LineNumber CHECK (LineNumber > 0),
        CONSTRAINT CK_PackingListLine_Qty CHECK (Qty >= 0),
        CONSTRAINT CK_PackingListLine_Pcs CHECK (PcsPerPackage IS NULL OR PcsPerPackage > 0),
        CONSTRAINT CK_PackingListLine_Packages CHECK (PackageCount IS NULL OR (PackageCount >= 0 AND PackageCount = FLOOR(PackageCount))),
        CONSTRAINT CK_PackingListLine_Measures CHECK (NwPerPackage >= 0 AND GwPerPackage >= 0 AND CbmPerPackage >= 0 AND SubTotalNw >= 0 AND SubTotalGw >= 0 AND SubTotalCbm >= 0)
    );

    CREATE UNIQUE INDEX UX_PackingListHead_ActiveNo ON dbo.PackingListHead(PackingListNo) WHERE IsDeleted=0 AND LifecycleStatus=0;
    CREATE UNIQUE INDEX UX_PackingListLine_HeadLine ON dbo.PackingListLine(PackingListHeadId,LineNumber) WHERE IsDeleted=0;
    CREATE INDEX IX_PackingListHead_CustomerDate ON dbo.PackingListHead(CustomerId,InvoiceDate,Uid) INCLUDE(LifecycleStatus,PostingStatus) WHERE IsDeleted=0;
    CREATE INDEX IX_PackingListLine_GrossDemand ON dbo.PackingListLine(GrossDemandId,PackingListHeadId) INCLUDE(ProductId,Qty) WHERE IsDeleted=0;
    CREATE INDEX IX_PackingListLine_Warehouse ON dbo.PackingListLine(WarehouseSiteId) WHERE WarehouseSiteId IS NOT NULL;

    -- 表与字段说明存为 MS_Description，SSMS 中可查看，不只是脚本文本注释。
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'装箱单头：客户文档快照和独立过账事实。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'技术主键，由应用生成并保持不变。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'Uid';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本，新增为1。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'Version';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'软删除标志。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间，由应用审计打戳。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CreatedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CreatedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后修改时间。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'UpdatedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后修改操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'UpdatedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'装箱单业务编号。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PackingListNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最终客户逻辑标识。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CustomerId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据采用的客户名称快照，不随主数据更新。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CustomerNameSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据采用的客户地址快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CustomerAddressSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'自由填写的备注快照，不关联联系人实体。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'AttentionSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户自定义单据日期，不作为库存记账日期。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'InvoiceDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'集装箱号。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ContainerNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'封条号。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'SealNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'装运箱唛。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ShippingMark';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据备注。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'Note';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预计离港日期。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'Etd';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预计到达日期。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'Eta';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据生命周期，与库存状态分别表达。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'LifecycleStatus';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'库存过账状态，不表示已实现过账入口。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PostingStatus';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'库存业务日期，未过账可空，与单据日期独立。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PostingDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际过账操作时间。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PostedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过账操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PostedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'反向操作时间。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ReversedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'反向操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ReversedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'反向原因。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ReverseReason';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'保留建单命令身份，具体命令校验另行实现。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'CreateCommandId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过账命令身份，用于后续幂等处理。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'PostCommandId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'反向命令身份，用于后续幂等处理。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListHead', @level2type=N'COLUMN', @level2name=N'ReverseCommandId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'装箱单行：来源需求交付、产品包装快照；不承担采购分摊。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'技术主键，由应用生成并保持不变。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'Uid';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本，新增为1。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'Version';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'软删除标志。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间，由应用审计打戳。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'CreatedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'CreatedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后修改时间。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'UpdatedDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后修改操作人。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'UpdatedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属装箱单头 Uid，逻辑关联。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'PackingListHeadId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据内行号，同来源允许多行。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'LineNumber';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一交付来源锚点，不表示采购或物料分摊。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'GrossDemandId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次交付的产品标识。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'ProductId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'产品编码快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'SkuSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关联型号快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'LinkSkuSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户型号快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'CustomerSkuSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'英文描述快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'DescriptionEnSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'英文颜色名称快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'ColorNameEnSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'库存及交付数量的计量单位快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'UnitSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本行计划交付数量，草稿不形成正式交付事实。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'Qty';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每包数量；未知为空，已知时须大于零。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'PcsPerPackage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际整包装数；默认建议向上取整，允许人工指定，未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'PackageCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每包净重。未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'NwPerPackage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每包毛重。未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'GwPerPackage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每包体积。未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'CbmPerPackage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'SubTotalNw';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'SubTotalGw';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'SubTotalCbm';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户采购编号文本快照，不作为关系主键。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'CustomerPoNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行上手工填写的发票号文本，不关联财务发票。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'InvoiceNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本行箱唛。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'Marks';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本行备注。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'Note';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次库存操作仓库，草稿允许未确定。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'WarehouseSiteId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'采用的仓库编码快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'WarehouseCodeSnapshot';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'采用的仓库名称快照。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PackingListLine', @level2type=N'COLUMN', @level2name=N'WarehouseNameSnapshot';

    -- 完整性后检失败时同事务回滚，恢复旧表。
    IF EXISTS(
        SELECT 1 FROM sys.columns c
        WHERE c.object_id IN (OBJECT_ID(N'dbo.PackingListHead'),OBJECT_ID(N'dbo.PackingListLine'))
          AND NOT EXISTS(SELECT 1 FROM sys.extended_properties p
                         WHERE p.class=1 AND p.major_id=c.object_id AND p.minor_id=c.column_id AND p.name=N'MS_Description'))
        THROW 51008, N'存在缺少字段备注的新列，已拒绝提交。', 1;
    COMMIT;
    SELECT N'PackingList 空表重建完成；仍需适配应用历史 SQL。' AS Result;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
