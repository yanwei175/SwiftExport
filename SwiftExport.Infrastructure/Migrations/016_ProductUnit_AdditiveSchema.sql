/*
  产品基础计量单位增量结构。
  只补充 Product.Unit，不回填客户产品单位，也不修改任何历史 UnitSnapshot。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
    THROW 51600, N'缺少 dbo.Product，不能补充产品基础单位。', 1;

BEGIN TRY
    BEGIN TRAN;

    IF COL_LENGTH(N'dbo.Product', N'Unit') IS NULL
        ALTER TABLE dbo.Product ADD Unit nvarchar(100) NULL;
    ELSE
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.Product
            WHERE LEN(Unit) > 100
        )
            THROW 51601, N'Product.Unit 存在超过 100 个字符的数据，不能规范列长度。', 1;

        ALTER TABLE dbo.Product ALTER COLUMN Unit nvarchar(100) NULL;
    END;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

SELECT
    N'PASS' AS Result,
    N'Product.Unit' AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType,
    c.max_length AS MaxLengthBytes,
    c.is_nullable AS IsNullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.Product')
  AND c.name = N'Unit';
