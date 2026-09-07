/*
  来源需求交付与 PL 可选计划追溯增量草案。先运行 025 Preflight；本脚本不由应用自动执行。
*/
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.PackingListContributionLine',N'SourceDemandVersionSnapshot') IS NULL
    ALTER TABLE dbo.PackingListContributionLine ADD SourceDemandVersionSnapshot int NULL;
IF COL_LENGTH(N'dbo.PackingListContributionLine',N'SourceVersionSnapshot') IS NULL
    ALTER TABLE dbo.PackingListContributionLine ADD SourceVersionSnapshot int NULL;

/*
  SQL Server 会先编译整个批次；首次执行时若在 ADD COLUMN 后直接静态引用新列，会在 ALTER 生效前报 207。
  使用新的动态批次完成确定性映射和非空收口，仍只按已保存的 GrossDemandId 关联，不从 SKU 或销售单号猜测来源。
*/
EXEC sys.sp_executesql N'
UPDATE pl
SET SourceDemandVersionSnapshot=gd.Version,
    SourceVersionSnapshot=gd.SourceVersion
FROM dbo.PackingListContributionLine pl
INNER JOIN dbo.GrossDemand gd ON gd.Uid=pl.GrossDemandId
WHERE pl.SourceDemandVersionSnapshot IS NULL;

IF EXISTS(SELECT 1 FROM dbo.PackingListContributionLine WHERE SourceDemandVersionSnapshot IS NULL)
    THROW 50025,N''存在无法映射来源需求版本的 PL 贡献，请查看 025 只读预检。'',1;

ALTER TABLE dbo.PackingListContributionLine ALTER COLUMN SourceDemandVersionSnapshot int NOT NULL;';

ALTER TABLE dbo.PackingListContributionLine ALTER COLUMN NetDemandId uniqueidentifier NULL;
ALTER TABLE dbo.PackingListContributionLine ALTER COLUMN NetDemandAllocationId uniqueidentifier NULL;
ALTER TABLE dbo.PackingListContributionLine ALTER COLUMN SourceSupplierId uniqueidentifier NULL;
ALTER TABLE dbo.OutboundDocumentLine ALTER COLUMN NetDemandAllocationId uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'IX_OutboundLine_GrossDemand_Posting')
    CREATE INDEX IX_OutboundLine_GrossDemand_Posting ON dbo.OutboundDocumentLine(GrossDemandId,OutboundDocumentId) INCLUDE(Quantity) WHERE IsDeleted=0;

COMMIT TRANSACTION;
