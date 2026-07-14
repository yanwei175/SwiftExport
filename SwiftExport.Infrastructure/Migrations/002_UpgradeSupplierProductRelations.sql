SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.SupplierProduct', 'SupplierId') IS NULL
    ALTER TABLE dbo.SupplierProduct ADD SupplierId uniqueidentifier NULL;

IF COL_LENGTH('dbo.SupplierProduct', 'SkuId') IS NULL
    ALTER TABLE dbo.SupplierProduct ADD SkuId uniqueidentifier NULL;
GO

BEGIN TRY

    UPDATE sp
       SET SupplierId = s.Uid
      FROM dbo.SupplierProduct sp
      JOIN dbo.Supplier s ON s.SupplierCode = sp.SupplierCode
     WHERE sp.SupplierId IS NULL;

    UPDATE sp
       SET SkuId = p.Uid
      FROM dbo.SupplierProduct sp
      JOIN dbo.Product p ON p.Sku = sp.ProductSku
     WHERE sp.SkuId IS NULL;

    IF EXISTS (
        SELECT 1
          FROM dbo.SupplierProduct
         WHERE SupplierId IS NULL
            OR SkuId IS NULL
            OR NULLIF(LTRIM(RTRIM(SupplierCode)), '') IS NULL
            OR NULLIF(LTRIM(RTRIM(ProductSku)), '') IS NULL
            OR NULLIF(LTRIM(RTRIM(SupplierSku)), '') IS NULL
    )
        THROW 51001, 'SupplierProduct contains orphaned relations or empty business codes.', 1;

    IF EXISTS (
        SELECT 1
          FROM dbo.SupplierProduct
         WHERE ISNULL(IsDeleted, 0) = 0
         GROUP BY SupplierId, SupplierSku
        HAVING COUNT(*) > 1
    )
        THROW 51002, 'SupplierProduct contains duplicate supplier SKU values for one supplier.', 1;

    IF EXISTS (
        SELECT 1
          FROM dbo.Supplier
         WHERE NULLIF(LTRIM(RTRIM(SupplierCode)), '') IS NULL
    )
        THROW 51003, 'Supplier contains an empty supplier code.', 1;

    IF EXISTS (
        SELECT 1
          FROM dbo.Supplier
         GROUP BY SupplierCode
        HAVING COUNT(*) > 1
    )
        THROW 51004, 'Supplier contains duplicate supplier codes.', 1;

    ALTER TABLE dbo.SupplierProduct ALTER COLUMN SupplierId uniqueidentifier NOT NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN SkuId uniqueidentifier NOT NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN SupplierCode nvarchar(50) NOT NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN ProductSku nvarchar(255) NOT NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN SupplierSku nvarchar(255) NOT NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN UnitPrice decimal(10,2) NULL;
    ALTER TABLE dbo.SupplierProduct ALTER COLUMN TaxPrice decimal(10,2) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Supplier') AND name = 'UX_Supplier_SupplierCode')
        CREATE UNIQUE INDEX UX_Supplier_SupplierCode ON dbo.Supplier(SupplierCode);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SupplierProduct') AND name = 'IX_SupplierProduct_SupplierId')
        CREATE INDEX IX_SupplierProduct_SupplierId ON dbo.SupplierProduct(SupplierId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SupplierProduct') AND name = 'IX_SupplierProduct_SkuId')
        CREATE INDEX IX_SupplierProduct_SkuId ON dbo.SupplierProduct(SkuId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SupplierProduct') AND name = 'IX_SupplierProduct_SupplierId_ProductSku')
        CREATE INDEX IX_SupplierProduct_SupplierId_ProductSku ON dbo.SupplierProduct(SupplierId, ProductSku);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SupplierProduct') AND name = 'UX_SupplierProduct_SupplierId_SupplierSku_Active')
        CREATE UNIQUE INDEX UX_SupplierProduct_SupplierId_SupplierSku_Active
            ON dbo.SupplierProduct(SupplierId, SupplierSku)
            WHERE IsDeleted = 0;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
