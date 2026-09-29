-- 现有数据库增量；空库使用生成基线。脚本未自动在生产库执行。
-- 执行后按既有 DbSeed 权限目录同步流程登记 System.ExcelFieldMapping.Manage，再由管理员分配给需要维护配置的角色。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ExcelFieldMapping', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ExcelFieldMapping]
    (
        [Uid] uniqueidentifier NOT NULL,
        [Version] int NOT NULL CONSTRAINT [DF_ExcelFieldMapping_Version] DEFAULT (1),
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ExcelFieldMapping_IsDeleted] DEFAULT (0),
        [CreatedDate] datetime2(7) NULL,
        [CreatedBy] nvarchar(500) NULL,
        [UpdatedDate] datetime2(7) NULL,
        [UpdatedBy] nvarchar(500) NULL,
        [GroupCode] nvarchar(40) NOT NULL,
        [ExcelHeader] nvarchar(200) NOT NULL,
        [NormalizedHeader] nvarchar(200) NOT NULL,
        [TargetFieldKey] nvarchar(100) NOT NULL,
        [IsEnabled] bit NOT NULL CONSTRAINT [DF_ExcelFieldMapping_IsEnabled] DEFAULT (1),
        [Remark] nvarchar(500) NULL,
        CONSTRAINT [PK_ExcelFieldMapping] PRIMARY KEY ([Uid]),
        CONSTRAINT [CK_ExcelFieldMapping_Version] CHECK ([Version] >= 1)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ExcelFieldMapping') AND name = N'UX_ExcelFieldMapping_Group_Header')
BEGIN
    CREATE UNIQUE INDEX [UX_ExcelFieldMapping_Group_Header]
        ON [dbo].[ExcelFieldMapping] ([GroupCode], [NormalizedHeader])
        WHERE [IsDeleted] = 0;
END;

COMMIT TRANSACTION;
