-- Excel 单据地址读取配置：仅创建两个新表，并初始化销售和采购默认空配置。
-- 请先在 SSMS 选择目标业务数据库；不创建数据库，不修改旧映射，不覆盖既有配置。
-- 本次明确授权的现有库增量脚本；表、列、索引、CHECK 和中文描述来源于实体生成基线。
-- 可重复执行；不插入虚构地址，不恢复用户已经软删除的默认配置。
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_ID()<=4 THROW 53100,N'请切换到目标业务数据库，不能在系统库执行。',1;
IF @@TRANCOUNT<>0 THROW 53101,N'请在没有外部事务的独立会话执行。',1;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @LockResult int;
EXEC @LockResult=sys.sp_getapplock @Resource=N'SwiftExport.ExcelReadProfile.SchemaSeed',
    @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
IF @LockResult<0 THROW 53102,N'无法获取初始化锁，请稍后重试。',1;

IF OBJECT_ID(N'dbo.ExcelReadField',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ExcelReadField]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_ExcelReadField_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_ExcelReadField_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [ProfileUid] uniqueidentifier NOT NULL,
    [TargetFieldKey] nvarchar(100) NOT NULL,
    [WorksheetName] nvarchar(31) NULL,
    [CellAddress] nvarchar(10) NOT NULL,
    [IsDownward] bit NOT NULL CONSTRAINT [DF_ExcelReadField_IsDownward] DEFAULT (0),
    CONSTRAINT [PK_ExcelReadField] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_ExcelReadField_Version] CHECK ([Version] >= 1)
);
END;

IF OBJECT_ID(N'dbo.ExcelReadProfile',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ExcelReadProfile]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_ExcelReadProfile_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_ExcelReadProfile_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [GroupCode] nvarchar(40) NOT NULL,
    [ConfigName] nvarchar(100) NOT NULL,
    [EndMode] int NOT NULL CONSTRAINT [DF_ExcelReadProfile_EndMode] DEFAULT (0),
    [LastRowColumn] nvarchar(3) NULL,
    [SummaryCellAddress] nvarchar(10) NULL,
    [Remark] nvarchar(500) NULL,
    CONSTRAINT [PK_ExcelReadProfile] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_ExcelReadProfile_EndMode] CHECK ([EndMode] IN (0, 1, 2)),
    CONSTRAINT [CK_ExcelReadProfile_Version] CHECK ([Version] >= 1)
);
END;

-- 已存在的表必须兼容当前实体列；不通过跳过建表掩盖结构错误。
DECLARE @ExpectedColumns TABLE(TableName sysname,ColumnName sysname,TypeName sysname,MaxLength int,IsNullable bit);
INSERT INTO @ExpectedColumns VALUES
(N'ExcelReadField',N'Uid',N'uniqueidentifier',16,0),
(N'ExcelReadField',N'Version',N'int',4,0),
(N'ExcelReadField',N'IsDeleted',N'bit',1,0),
(N'ExcelReadField',N'CreatedDate',N'datetime2',8,1),
(N'ExcelReadField',N'CreatedBy',N'nvarchar',1000,1),
(N'ExcelReadField',N'UpdatedDate',N'datetime2',8,1),
(N'ExcelReadField',N'UpdatedBy',N'nvarchar',1000,1),
(N'ExcelReadField',N'ProfileUid',N'uniqueidentifier',16,0),
(N'ExcelReadField',N'TargetFieldKey',N'nvarchar',200,0),
(N'ExcelReadField',N'WorksheetName',N'nvarchar',62,1),
(N'ExcelReadField',N'CellAddress',N'nvarchar',20,0),
(N'ExcelReadField',N'IsDownward',N'bit',1,0),
(N'ExcelReadProfile',N'Uid',N'uniqueidentifier',16,0),
(N'ExcelReadProfile',N'Version',N'int',4,0),
(N'ExcelReadProfile',N'IsDeleted',N'bit',1,0),
(N'ExcelReadProfile',N'CreatedDate',N'datetime2',8,1),
(N'ExcelReadProfile',N'CreatedBy',N'nvarchar',1000,1),
(N'ExcelReadProfile',N'UpdatedDate',N'datetime2',8,1),
(N'ExcelReadProfile',N'UpdatedBy',N'nvarchar',1000,1),
(N'ExcelReadProfile',N'GroupCode',N'nvarchar',80,0),
(N'ExcelReadProfile',N'ConfigName',N'nvarchar',200,0),
(N'ExcelReadProfile',N'EndMode',N'int',4,0),
(N'ExcelReadProfile',N'LastRowColumn',N'nvarchar',6,1),
(N'ExcelReadProfile',N'SummaryCellAddress',N'nvarchar',20,1),
(N'ExcelReadProfile',N'Remark',N'nvarchar',1000,1);
IF EXISTS (
 SELECT 1 FROM @ExpectedColumns e
 LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName
 LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.column_id IS NULL OR t.name<>e.TypeName OR c.max_length<>e.MaxLength OR c.is_nullable<>e.IsNullable
    OR (e.TypeName=N'datetime2' AND c.scale<>7)
) THROW 53103,N'已存在的地址配置表结构与实体不一致，请核对后再执行。',1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ExcelReadField') AND name=N'UX_ExcelReadField_Profile_Key')
CREATE UNIQUE INDEX [UX_ExcelReadField_Profile_Key] ON [dbo].[ExcelReadField] ([ProfileUid], [TargetFieldKey]) WHERE [IsDeleted] = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND name=N'UX_ExcelReadProfile_Group_Name')
CREATE UNIQUE INDEX [UX_ExcelReadProfile_Group_Name] ON [dbo].[ExcelReadProfile] ([GroupCode], [ConfigName]) WHERE [IsDeleted] = 0;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=0 AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'保存配置内一个目标字段的工作表、起始单元格和读取方式；只能随主配置事务维护。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'Uid','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'Uid';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'Version','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'IsDeleted','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'软删除标记；为真时业务查询默认不再返回该记录。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'IsDeleted';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'CreatedDate','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'CreatedDate';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'CreatedBy','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'CreatedBy';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'UpdatedDate','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'UpdatedDate';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'UpdatedBy','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'UpdatedBy';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'ProfileUid','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'所属主配置的逻辑主键。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'ProfileUid';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'TargetFieldKey','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'客户端认可的稳定字段路径；服务端仅校验格式，不绑定 ViewModel 类型。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'TargetFieldKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'WorksheetName','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'指定位置时的可选工作表名称，仅供参考；客户端实际读取使用本次捕获的活动工作表。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'WorksheetName';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'CellAddress','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'规范化的单个 A1 地址；向下读取时代表该列的首条明细位置。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'CellAddress';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadField') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadField'),N'IsDownward','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'是否从起始单元格逐行向下读取；同一配置的向下字段必须共用起始行，名称不决定读取源。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadField',
    @level2type=N'COLUMN', @level2name=N'IsDownward';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=0 AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'保存全员共享的一份 Excel 单据读取配置及明细结束规则；不保存工作簿数据。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'Uid','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'Uid';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'Version','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'IsDeleted','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'软删除标记；为真时业务查询默认不再返回该记录。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'IsDeleted';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'CreatedDate','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'CreatedDate';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'CreatedBy','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录创建人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'CreatedBy';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'UpdatedDate','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新时间。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'UpdatedDate';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'UpdatedBy','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'记录最后更新人标识。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'UpdatedBy';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'GroupCode','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'稳定单据分组，与客户端注册的导入模型分组对应，创建后不可修改。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'GroupCode';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'ConfigName','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'同一单据分组内唯一的配置名称。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'ConfigName';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'EndMode','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'明细结束方式：0 未配置，1 指定列最后有值行，2 指定汇总单元格所在行之前。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'EndMode';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'LastRowColumn','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'末行判断列的字母标识，例如 A；仅结束方式为 1 时保存。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'LastRowColumn';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'SummaryCellAddress','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'汇总单元格的规范 A1 地址，例如 D30；仅结束方式为 2 时保存。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'SummaryCellAddress';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.ExcelReadProfile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.ExcelReadProfile'),N'Remark','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description', @value=N'配置说明，不参与单元格读取。',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE', @level1name=N'ExcelReadProfile',
    @level2type=N'COLUMN', @level2name=N'Remark';

-- 仅插入选择器需要的默认空配置；真实字段地址由单据页面设置。
DECLARE @Now datetime2(7)=SYSDATETIME();
DECLARE @Operator nvarchar(500)=ORIGINAL_LOGIN();
INSERT INTO dbo.ExcelReadProfile
 (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,GroupCode,ConfigName,EndMode,LastRowColumn,SummaryCellAddress,Remark)
SELECT NEWID(),1,0,@Now,@Operator,@Now,@Operator,s.GroupCode,s.ConfigName,0,NULL,NULL,
 N'默认空配置，请在单据页面指定字段地址及明细结束规则。'
FROM (VALUES (N'SalesOrder',N'默认销售订单配置'),(N'PurchaseOrder',N'默认采购订单配置')) s(GroupCode,ConfigName)
WHERE NOT EXISTS (
 SELECT 1 FROM dbo.ExcelReadProfile p WITH (UPDLOCK,HOLDLOCK)
 WHERE p.GroupCode=s.GroupCode AND p.ConfigName=s.ConfigName
);
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;

SELECT Uid,Version,GroupCode,ConfigName,EndMode FROM dbo.ExcelReadProfile
WHERE IsDeleted=0 AND GroupCode IN (N'SalesOrder',N'PurchaseOrder') ORDER BY GroupCode,ConfigName,Uid;

