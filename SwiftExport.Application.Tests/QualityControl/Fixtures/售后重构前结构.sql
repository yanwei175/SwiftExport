-- 仅用于唯一隔离测试库，保留升级前头行结构以验证迁移。
DROP TABLE dbo.AfterSalesLine;
DROP TABLE dbo.AfterSalesHeader;
EXEC(N'CREATE TABLE [dbo].[AfterSalesHeader]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_AfterSalesHeader_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_AfterSalesHeader_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [AfterSalesNo] nvarchar(300) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [CustomerCodeSnapshot] nvarchar(300) NOT NULL,
    [CustomerNameSnapshot] nvarchar(500) NULL,
    [AcceptedDate] datetime2(7) NOT NULL,
    [OwnerId] uniqueidentifier NULL,
    [DocumentStatus] tinyint NOT NULL,
    [ConfirmationStatus] tinyint NOT NULL,
    [DemandConfirmedAt] datetime2(7) NULL,
    [DemandConfirmedBy] nvarchar(500) NULL,
    [ExpectedCompletionDate] datetime2(7) NULL,
    [Conclusion] nvarchar(2000) NULL,
    [Remark] nvarchar(2000) NULL,
    CONSTRAINT [PK_AfterSalesHeader] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_AfterSalesHeader_Status] CHECK ([DocumentStatus] IN (0, 8, 9) AND [ConfirmationStatus] IN (0, 1)),
    CONSTRAINT [CK_AfterSalesHeader_Version] CHECK ([Version] >= 1)
);
CREATE TABLE [dbo].[AfterSalesLine]
(
    [Uid] uniqueidentifier NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_AfterSalesLine_Version] DEFAULT (1),
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_AfterSalesLine_IsDeleted] DEFAULT (0),
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(500) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(500) NULL,
    [HeaderId] uniqueidentifier NOT NULL,
    [LineNumber] int NOT NULL,
    [OriginalSalesOrderLineId] uniqueidentifier NULL,
    [CustomerComplaintNo] nvarchar(100) NULL,
    [ResponsibleSupplierId] uniqueidentifier NULL,
    [IssueProductId] uniqueidentifier NULL,
    [IssueSkuSnapshot] nvarchar(300) NULL,
    [IssueCustomerSkuSnapshot] nvarchar(300) NULL,
    [IssueLinkSkuSnapshot] nvarchar(300) NULL,
    [IssueColorId] uniqueidentifier NULL,
    [IssueColorSnapshot] nvarchar(500) NULL,
    [IssueProductDescription] nvarchar(2000) NULL,
    [IssueDescription] nvarchar(2000) NOT NULL,
    [QualityCategoryId] uniqueidentifier NULL,
    [CauseAnalysis] nvarchar(500) NULL,
    [ProductId] uniqueidentifier NULL,
    [IsNonStandard] bit NOT NULL,
    [SkuSnapshot] nvarchar(300) NULL,
    [CustomerSkuSnapshot] nvarchar(300) NULL,
    [LinkSkuSnapshot] nvarchar(300) NULL,
    [DescriptionSnapshot] nvarchar(2000) NULL,
    [ColorId] uniqueidentifier NULL,
    [ColorSnapshot] nvarchar(500) NULL,
    [SpecificationSnapshot] nvarchar(500) NULL,
    [Quantity] decimal(18,4) NULL,
    [UnitSnapshot] nvarchar(300) NOT NULL,
    [RequiredDate] datetime2(7) NULL,
    [ServiceAmountCny] decimal(18,4) NULL,
    [ReturnedReceivedDate] datetime2(7) NULL,
    [TreatmentDescription] nvarchar(2000) NULL,
    [Remark] nvarchar(2000) NULL,
    CONSTRAINT [PK_AfterSalesLine] PRIMARY KEY ([Uid]),
    CONSTRAINT [CK_AfterSalesLine_Line] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_AfterSalesLine_Quantity] CHECK ([Quantity] IS NULL OR [Quantity] > 0),
    CONSTRAINT [CK_AfterSalesLine_Version] CHECK ([Version] >= 1)
);
CREATE INDEX IX_Test_LegacySupplier ON dbo.AfterSalesLine(ResponsibleSupplierId) INCLUDE(ServiceAmountCny);
ALTER TABLE dbo.AfterSalesLine ADD CONSTRAINT DF_Test_LegacyAmount DEFAULT(0) FOR ServiceAmountCny;
ALTER TABLE dbo.AfterSalesLine ADD CONSTRAINT CK_Test_LegacyAmount CHECK(ServiceAmountCny>=0);
');
