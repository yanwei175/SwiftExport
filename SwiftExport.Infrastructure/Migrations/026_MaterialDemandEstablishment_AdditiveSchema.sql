SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.GrossDemand', N'DemandBasisFingerprint') IS NULL
    ALTER TABLE dbo.GrossDemand ADD DemandBasisFingerprint nvarchar(64) NULL;

IF COL_LENGTH(N'dbo.NetDemand', N'SourceDemandVersionSnapshot') IS NULL
    ALTER TABLE dbo.NetDemand ADD SourceDemandVersionSnapshot int NULL;

IF COL_LENGTH(N'dbo.NetDemand', N'SourceVersionSnapshot') IS NULL
    ALTER TABLE dbo.NetDemand ADD SourceVersionSnapshot int NULL;

IF COL_LENGTH(N'dbo.NetDemand', N'SourceDemandBasisFingerprintSnapshot') IS NULL
    ALTER TABLE dbo.NetDemand ADD SourceDemandBasisFingerprintSnapshot nvarchar(64) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.NetDemand') AND name = N'IX_NetDemand_GrossDemand_Active')
BEGIN
    CREATE INDEX IX_NetDemand_GrossDemand_Active
        ON dbo.NetDemand(GrossDemandId,LifecycleStatus,DecompositionRevision)
        INCLUDE(SourceDemandVersionSnapshot,SourceVersionSnapshot,SourceDemandBasisFingerprintSnapshot)
        WHERE IsDeleted=0;
END;

COMMIT TRANSACTION;
