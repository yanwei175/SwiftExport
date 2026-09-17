-- 在实际业务库执行后再部署新后端；仅新增客户追溯字段，不回填历史、不创建外键或触发器。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.AfterSalesLine', N'U') IS NULL
    THROW 53001, N'售后行表不存在，请核对目标数据库。', 1;

IF COL_LENGTH(N'dbo.AfterSalesLine', N'CustomerComplaintNo') IS NULL
    ALTER TABLE dbo.AfterSalesLine ADD CustomerComplaintNo nvarchar(100) NULL;

-- 已存在的列必须符合约定，不能静默接受另一种结构或自动改写既有数据。
IF NOT EXISTS (SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.AfterSalesLine') AND name=N'CustomerComplaintNo'
      AND system_type_id=231 AND max_length=200 AND is_nullable=1)
    THROW 53002, N'客户投诉单号现有列结构与 nvarchar(100) NULL 不一致。', 1;

IF EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE class=1 AND major_id=OBJECT_ID(N'dbo.AfterSalesLine')
      AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.AfterSalesLine'),N'CustomerComplaintNo','ColumnId')
      AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',
        @value=N'客户侧投诉单号，可空且允许重复；开放单据确认后仍可补充，不影响来源实物要求。',
        @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'AfterSalesLine',
        @level2type=N'COLUMN',@level2name=N'CustomerComplaintNo';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',
        @value=N'客户侧投诉单号，可空且允许重复；开放单据确认后仍可补充，不影响来源实物要求。',
        @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'AfterSalesLine',
        @level2type=N'COLUMN',@level2name=N'CustomerComplaintNo';
COMMIT TRANSACTION;
