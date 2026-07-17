SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.PurchaseOrderHead', N'U') IS NULL
        THROW 51000, N'采购工作台迁移阻断：dbo.PurchaseOrderHead 不存在。请先恢复既有采购物理表，不得由本迁移重建采购模块。', 1;

    IF OBJECT_ID(N'dbo.PurchaseOrderLine', N'U') IS NULL
        THROW 51001, N'采购工作台迁移阻断：dbo.PurchaseOrderLine 不存在。', 1;

    IF COL_LENGTH(N'dbo.PurchaseOrderHead', N'ConfirmedDate') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD ConfirmedDate datetime2(7) NULL;
    IF COL_LENGTH(N'dbo.PurchaseOrderHead', N'ConfirmedBy') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD ConfirmedBy nvarchar(100) NULL;

    IF OBJECT_ID(N'dbo.FollowUpLog', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FollowUpLog
        (
            Uid uniqueidentifier NOT NULL,
            Version int NOT NULL CONSTRAINT DF_FollowUpLog_Version DEFAULT(1),
            IsDeleted bit NOT NULL CONSTRAINT DF_FollowUpLog_IsDeleted DEFAULT(0),
            CreatedDate datetime2(7) NULL,
            CreatedBy nvarchar(100) NULL,
            UpdatedDate datetime2(7) NULL,
            UpdatedBy nvarchar(100) NULL,
            RefLineId uniqueidentifier NULL,
            NoteContent nvarchar(2000) NULL,
            CONSTRAINT PK_FollowUpLog PRIMARY KEY (Uid)
        );
    END;

    IF COL_LENGTH(N'dbo.FollowUpLog', N'RefLineType') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD RefLineType nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'Sequence') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD Sequence int NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'ArrangementStatus') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD ArrangementStatus int NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'ReadyQty') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD ReadyQty decimal(18,4) NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'ExpectedReadyDate') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD ExpectedReadyDate datetime2(7) NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'SupplierReportedDate') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD SupplierReportedDate datetime2(7) NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'IsCorrection') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD IsCorrection bit NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'CorrectionReason') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD CorrectionReason nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.FollowUpLog', N'CommandId') IS NULL
        ALTER TABLE dbo.FollowUpLog ADD CommandId uniqueidentifier NULL;

    IF OBJECT_ID(N'dbo.BusinessOperationLog', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.BusinessOperationLog
        (
            Uid uniqueidentifier NOT NULL,
            Version int NOT NULL CONSTRAINT DF_BusinessOperationLog_Version DEFAULT(1),
            IsDeleted bit NOT NULL CONSTRAINT DF_BusinessOperationLog_IsDeleted DEFAULT(0),
            CreatedDate datetime2(7) NULL,
            CreatedBy nvarchar(100) NULL,
            UpdatedDate datetime2(7) NULL,
            UpdatedBy nvarchar(100) NULL,
            CorrelationId nvarchar(64) NOT NULL,
            CommandId uniqueidentifier NOT NULL,
            Module nvarchar(50) NOT NULL,
            AggregateType nvarchar(100) NOT NULL,
            AggregateUid uniqueidentifier NOT NULL,
            AggregateNoSnapshot nvarchar(100) NULL,
            OperationType nvarchar(100) NOT NULL,
            OperationName nvarchar(100) NOT NULL,
            Reason nvarchar(1000) NULL,
            Summary nvarchar(2000) NULL,
            ChangedFieldsJson nvarchar(max) NULL,
            RelatedObjectsJson nvarchar(max) NULL,
            UserUid uniqueidentifier NULL,
            UserNameSnapshot nvarchar(100) NOT NULL,
            OccurredDate datetime2(7) NOT NULL,
            CONSTRAINT PK_BusinessOperationLog PRIMARY KEY (Uid)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FollowUpLog') AND name=N'UX_FollowUpLog_CommandId')
        EXEC(N'CREATE UNIQUE INDEX UX_FollowUpLog_CommandId ON dbo.FollowUpLog(CommandId) WHERE CommandId IS NOT NULL AND IsDeleted=0;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FollowUpLog') AND name=N'UX_FollowUpLog_RefLine_Sequence')
        EXEC(N'CREATE UNIQUE INDEX UX_FollowUpLog_RefLine_Sequence ON dbo.FollowUpLog(RefLineType,RefLineId,Sequence) WHERE RefLineType IS NOT NULL AND RefLineId IS NOT NULL AND Sequence IS NOT NULL AND IsDeleted=0;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FollowUpLog') AND name=N'IX_FollowUpLog_RefLine_ReportedDate')
        EXEC(N'CREATE INDEX IX_FollowUpLog_RefLine_ReportedDate ON dbo.FollowUpLog(RefLineType,RefLineId,SupplierReportedDate DESC) INCLUDE(Sequence,ArrangementStatus,ReadyQty,ExpectedReadyDate,IsCorrection);');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BusinessOperationLog') AND name=N'UX_BusinessOperationLog_Command')
        EXEC(N'CREATE UNIQUE INDEX UX_BusinessOperationLog_Command ON dbo.BusinessOperationLog(Module,OperationType,CommandId) WHERE IsDeleted=0;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BusinessOperationLog') AND name=N'IX_BusinessOperationLog_Aggregate')
        EXEC(N'CREATE INDEX IX_BusinessOperationLog_Aggregate ON dbo.BusinessOperationLog(Module,AggregateUid,OccurredDate DESC) INCLUDE(OperationType,OperationName,UserNameSnapshot,CorrelationId);');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FollowUpLog') AND name=N'CK_FollowUpLog_ProcurementValues')
        EXEC(N'ALTER TABLE dbo.FollowUpLog WITH CHECK ADD CONSTRAINT CK_FollowUpLog_ProcurementValues CHECK
          ((RefLineType IS NULL AND Sequence IS NULL AND ArrangementStatus IS NULL AND ReadyQty IS NULL AND SupplierReportedDate IS NULL AND CommandId IS NULL)
           OR (RefLineType=N''PurchaseOrderLine'' AND Sequence>0 AND ArrangementStatus IN(0,1,2,8) AND ReadyQty>=0 AND SupplierReportedDate IS NOT NULL AND CommandId IS NOT NULL));');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
