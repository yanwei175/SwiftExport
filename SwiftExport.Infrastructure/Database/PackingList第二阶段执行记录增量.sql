-- PackingList 第二阶段：仅新增正式执行证据，不修改现有业务表或数据。
-- 在人工核对的目标 SQL Server 数据库执行；本任务不执行此脚本。
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.PackingListHead',N'U') IS NULL OR OBJECT_ID(N'dbo.PackingListLine',N'U') IS NULL
    THROW 53200,N'缺少 PackingList 头行表，停止部署。',1;
IF OBJECT_ID(N'dbo.PackingListPostingExecution',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PackingListPostingExecutionLine',N'U') IS NOT NULL
    THROW 53201,N'执行证据表已存在；请人工检查，不覆盖既有事实。',1;
BEGIN TRANSACTION;

CREATE TABLE [dbo].[PackingListPostingExecution]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_PackingListPostingExecution_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_PackingListPostingExecution_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [PackingListHeadId] uniqueidentifier NOT NULL,
    [CommandId] uniqueidentifier NOT NULL,
    [Operation] int NOT NULL,
    [OriginalExecutionId] uniqueidentifier NULL,
    [BusinessDate] datetime2(7) NOT NULL,
    [PackingListNo] nvarchar(300) NOT NULL,
    [ExpectedVersion] int NOT NULL,
    [RequestFingerprint] nvarchar(64) NOT NULL,
    [PreviewToken] nvarchar(64) NOT NULL,
    [RuleVersion] int NOT NULL,
    [Reason] nvarchar(2000) NULL,
    [ResultJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_PackingListPostingExecution] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_PackingListPostingExecution_Operation] CHECK ([Operation] IN (1,2)),
    CONSTRAINT [CK_PackingListPostingExecution_Version] CHECK ([Version] >= 1)
);

CREATE TABLE [dbo].[PackingListPostingExecutionLine]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_PackingListPostingExecutionLine_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_PackingListPostingExecutionLine_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [ExecutionId] uniqueidentifier NOT NULL,
    [LineNumber] int NOT NULL,
    [PackingListLineId] uniqueidentifier NOT NULL,
    [GrossDemandId] uniqueidentifier NOT NULL,
    [ActionKind] nvarchar(500) NOT NULL,
    [ProductId] uniqueidentifier NULL,
    [Unit] nvarchar(300) NOT NULL,
    [WarehouseSiteId] uniqueidentifier NULL,
    [PurchaseLineId] uniqueidentifier NULL,
    [RootNetDemandId] uniqueidentifier NULL,
    [ComponentNetDemandId] uniqueidentifier NULL,
    [RootQtySnapshot] decimal(18,4) NOT NULL,
    [ComponentQtySnapshot] decimal(18,4) NOT NULL,
    [ShippedQtySnapshot] decimal(18,4) NOT NULL,
    [Qty] decimal(18,4) NOT NULL,
    [DocumentId] uniqueidentifier NULL,
    [DocumentNo] nvarchar(300) NULL,
    [DocumentVersion] int NULL,
    [DocumentLineId] uniqueidentifier NULL,
    [LedgerId] uniqueidentifier NULL,
    [OriginalExecutionLineId] uniqueidentifier NULL,
    CONSTRAINT [PK_PackingListPostingExecutionLine] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_PackingListPostingExecutionLine_Action] CHECK ([ActionKind] IN (N'Delivery',N'Receipt',N'Shipment',N'Consumption')),
    CONSTRAINT [CK_PackingListPostingExecutionLine_Inventory] CHECK (([ActionKind]=N'Delivery' AND [WarehouseSiteId] IS NULL AND [DocumentId] IS NULL AND [DocumentLineId] IS NULL AND [LedgerId] IS NULL) OR ([ActionKind]<>N'Delivery' AND [ProductId] IS NOT NULL AND [WarehouseSiteId] IS NOT NULL AND [DocumentId] IS NOT NULL AND [DocumentLineId] IS NOT NULL AND [LedgerId] IS NOT NULL)),
    CONSTRAINT [CK_PackingListPostingExecutionLine_Quantity] CHECK ([Qty]>0 AND [LineNumber]>0),
    CONSTRAINT [CK_PackingListPostingExecutionLine_Version] CHECK ([Version] >= 1)
);

CREATE INDEX [IX_PackingListPostingExecution_Head] ON [dbo].[PackingListPostingExecution] ([PackingListHeadId], [Operation]);

CREATE UNIQUE INDEX [UX_PackingListPostingExecution_Command] ON [dbo].[PackingListPostingExecution] ([CommandId]);

CREATE INDEX [IX_PackingListPostingExecutionLine_Ledger] ON [dbo].[PackingListPostingExecutionLine] ([LedgerId]);

CREATE INDEX [IX_PackingListPostingExecutionLine_Line] ON [dbo].[PackingListPostingExecutionLine] ([PackingListLineId]);

CREATE INDEX [IX_PackingListPostingExecutionLine_Source] ON [dbo].[PackingListPostingExecutionLine] ([GrossDemandId]);

CREATE UNIQUE INDEX [UX_PackingListPostingExecutionLine_Number] ON [dbo].[PackingListPostingExecutionLine] ([ExecutionId], [LineNumber]);

EXEC(N'CREATE TRIGGER [dbo].[TR_PackingListPostingExecution_AppendOnly] ON [dbo].[PackingListPostingExecution] INSTEAD OF UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 53100, N''PackingListPostingExecution 是只追加事实，禁止更新或删除。'', 1; END;');

EXEC(N'CREATE TRIGGER [dbo].[TR_PackingListPostingExecutionLine_AppendOnly] ON [dbo].[PackingListPostingExecutionLine] INSTEAD OF UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 53100, N''PackingListPostingExecutionLine 是只追加事实，禁止更新或删除。'', 1; END;');

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'箱单一次正式过账或反过账的不可变执行证据，不承担采购分摊或库存余额。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'Uid';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'Version';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'软删除标记；为真时业务查询默认不再返回该记录。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'IsDeleted';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'CreatedDate';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'CreatedBy';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'UpdatedDate';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'UpdatedBy';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际执行的箱单头。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'PackingListHeadId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'外层幂等命令身份，跨操作唯一。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'CommandId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'1 正向过账，2 整单反向；不是单据生命周期。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'Operation';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'反向时指向原正向执行，正向为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'OriginalExecutionId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'库存业务日期，与 InvoiceDate 独立。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'BusinessDate';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'操作时箱单编号快照。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'PackingListNo';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'执行采用的箱单技术版本。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'ExpectedVersion';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'用户及操作完整输入规范化后的 SHA-256。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'RequestFingerprint';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'确认时校验的只读预览令牌。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'PreviewToken';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'计算规则版本，解释历史快照而不重新计算。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'RuleVersion';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'反向原因；正向可以为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'Reason';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'完整命令结果 JSON，重放使用原结果而不是重新取号。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecution',
    @level2type=N'COLUMN', @level2name=N'ResultJson';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'箱单物理行的实际执行动作及计算快照，关联生成单据和流水，不作为采购剩余库存账。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'Uid';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'Version';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'软删除标记；为真时业务查询默认不再返回该记录。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'IsDeleted';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'CreatedDate';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'CreatedBy';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'UpdatedDate';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'UpdatedBy';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'所属执行证据头。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ExecutionId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'执行内稳定排序号。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'LineNumber';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'原箱单物理行，拆箱后仍保留各行身份。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'PackingListLineId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'来源交付锚点。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'GrossDemandId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'Delivery、Receipt、Shipment、Consumption；方向由执行头表达。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ActionKind';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际库存产品或无库存交付的可空产品。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ProductId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际采用的单位文本。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'Unit';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际库存仓，无库存交付为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'WarehouseSiteId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'自动收货的采购依据，其余动作为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'PurchaseLineId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'采用的根物料需求。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'RootNetDemandId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'耗用所采用的客供件物料需求。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ComponentNetDemandId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'采用的根需求 GrossQty 快照。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'RootQtySnapshot';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'采用的子需求 GrossQty 快照。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ComponentQtySnapshot';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'该来源本批总出货量，解释比例用量。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'ShippedQtySnapshot';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'本实际动作数量，方向由执行头决定。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'Qty';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'生成单据身份，无库存动作为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'DocumentId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'生成单据编号快照。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'DocumentNo';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'生成单据当次返回的版本，用于反向前复核。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'DocumentVersion';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际入出库单据行。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'DocumentLineId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'实际正向或反向流水。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'LedgerId';

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'反向时关联原执行明细；正向为空。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'PackingListPostingExecutionLine',
    @level2type=N'COLUMN', @level2name=N'OriginalExecutionLineId';

COMMIT TRANSACTION;
