-- 手动执行于目标库：只增加售后单头资料路径，不创建外键、触发器或来源需求字段。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.AfterSalesHeader', N'LocalFileFolder') IS NULL
    ALTER TABLE dbo.AfterSalesHeader ADD LocalFileFolder nvarchar(1000) NULL;

IF EXISTS (SELECT 1 FROM sys.extended_properties
           WHERE major_id=OBJECT_ID(N'dbo.AfterSalesHeader')
             AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.AfterSalesHeader'), N'LocalFileFolder', 'ColumnId')
             AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',
        @value=N'本机投诉资料文件夹路径，仅保存文本，不参与来源需求或文件系统操作。',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'AfterSalesHeader',
        @level2type=N'COLUMN', @level2name=N'LocalFileFolder';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',
        @value=N'本机投诉资料文件夹路径，仅保存文本，不参与来源需求或文件系统操作。',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'AfterSalesHeader',
        @level2type=N'COLUMN', @level2name=N'LocalFileFolder';
COMMIT TRANSACTION;
