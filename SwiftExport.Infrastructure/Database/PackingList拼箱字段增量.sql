-- 拼箱仅扩展真实来源行；不新建组表、不回填旧单、不增加强外键。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.PackingListLine',N'U') IS NULL
    THROW 51000, N'请先部署 PackingListLine 基线表。', 1;
IF COL_LENGTH(N'dbo.PackingListLine',N'PackingGroupId') IS NULL
    ALTER TABLE dbo.PackingListLine ADD PackingGroupId uniqueidentifier NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListLine') AND name=N'IX_PackingListLine_PackingGroup')
    EXEC(N'CREATE INDEX IX_PackingListLine_PackingGroup ON dbo.PackingListLine(PackingListHeadId,PackingGroupId) INCLUDE(LineNumber);');
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.PackingListLine')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.PackingListLine'),N'PackingGroupId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',
        @value=N'同单据内一只拼箱的稳定标识；NULL 为普通行。同组共享箱级指标，汇总只计一次。',
        @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'PackingListLine',
        @level2type=N'COLUMN',@level2name=N'PackingGroupId';
COMMIT;
