IF OBJECT_ID('dbo.PackingListHead', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingListHead
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PackingListNo NVARCHAR(50) NOT NULL,
        PoNo NVARCHAR(50) NULL,
        PurchaseOrderNo NVARCHAR(50) NULL,
        SalesOrderNo NVARCHAR(50) NULL,
        SupplierCode NVARCHAR(50) NOT NULL,
        SupplierName NVARCHAR(200) NULL,
        PackingDate DATETIME NULL,
        Status NVARCHAR(50) NULL,
        IsPosted BIT NOT NULL CONSTRAINT DF_PackingListHead_IsPosted DEFAULT(0),
        PostedDate DATETIME NULL,
        PostedBy NVARCHAR(100) NULL,
        ReceiveNo NVARCHAR(50) NULL,
        Remark NVARCHAR(500) NULL,
        Even NVARCHAR(50) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_PackingListHead_IsDeleted DEFAULT(0),
        CreatedDate DATETIME NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedDate DATETIME NULL,
        UpdatedBy NVARCHAR(100) NULL
    );

    CREATE UNIQUE INDEX UX_PackingListHead_PackingListNo
        ON dbo.PackingListHead(PackingListNo)
        WHERE IsDeleted = 0;
END;

IF OBJECT_ID('dbo.PackingListLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingListLine
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PackingListNo NVARCHAR(50) NOT NULL,
        PackingListHeadId INT NULL,
        Sku NVARCHAR(100) NOT NULL,
        CustomerSku NVARCHAR(100) NULL,
        Description NVARCHAR(500) NULL,
        Qty DECIMAL(18,4) NOT NULL,
        Unit NVARCHAR(30) NULL,
        PoLineId INT NULL,
        SoLineId BIGINT NULL,
        PoNo NVARCHAR(50) NULL,
        PurchaseOrderNo NVARCHAR(50) NULL,
        SalesOrderNo NVARCHAR(50) NULL,
        Remark NVARCHAR(500) NULL,
        Even NVARCHAR(50) NULL,
        Status NVARCHAR(50) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_PackingListLine_IsDeleted DEFAULT(0),
        CreatedDate DATETIME NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedDate DATETIME NULL,
        UpdatedBy NVARCHAR(100) NULL
    );

    CREATE INDEX IX_PackingListLine_PackingListNo
        ON dbo.PackingListLine(PackingListNo)
        WHERE IsDeleted = 0;
END;
