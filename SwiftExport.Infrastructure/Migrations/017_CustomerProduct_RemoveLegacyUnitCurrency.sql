/*
  退役 CustomerProduct 上错误归属的单位与币种字段。
  - 产品当前基础单位由 Product.Unit 维护。
  - 客户当前默认币种由 Customer.Currency 维护。
  - 本迁移不回填 Product.Unit，也不修改任何历史单据快照。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.CustomerProduct', N'U') IS NULL
    THROW 51700, N'dbo.CustomerProduct 不存在，无法执行遗留字段退役。', 1;

BEGIN TRY
    BEGIN TRAN;

    DECLARE @ColumnName sysname;
    DECLARE @sql nvarchar(max);
    DECLARE legacy_column_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT ColumnName
        FROM (VALUES(N'CustomerSkuUnit'), (N'Currency')) columns_to_drop(ColumnName)
        WHERE COL_LENGTH(N'dbo.CustomerProduct', ColumnName) IS NOT NULL;

    OPEN legacy_column_cursor;
    FETCH NEXT FROM legacy_column_cursor INTO @ColumnName;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = NULL;
        SELECT @sql = STRING_AGG(
            N'ALTER TABLE dbo.CustomerProduct DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';',
            CHAR(13) + CHAR(10))
        FROM sys.default_constraints dc
        JOIN sys.columns c
          ON c.object_id = dc.parent_object_id
         AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CustomerProduct')
          AND c.name = @ColumnName;
        IF @sql IS NOT NULL EXEC sys.sp_executesql @sql;

        SET @sql = NULL;
        SELECT @sql = STRING_AGG(
            N'ALTER TABLE dbo.CustomerProduct DROP CONSTRAINT ' + QUOTENAME(kc.name) + N';',
            CHAR(13) + CHAR(10))
        FROM sys.key_constraints kc
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.CustomerProduct')
          AND kc.type = N'UQ'
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns ic
              JOIN sys.columns c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
              WHERE ic.object_id = kc.parent_object_id
                AND ic.index_id = kc.unique_index_id
                AND c.name = @ColumnName
          );
        IF @sql IS NOT NULL EXEC sys.sp_executesql @sql;

        SET @sql = NULL;
        SELECT @sql = STRING_AGG(
            N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.CustomerProduct;',
            CHAR(13) + CHAR(10))
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.CustomerProduct')
          AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns ic
              JOIN sys.columns c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND c.name = @ColumnName
          );
        IF @sql IS NOT NULL EXEC sys.sp_executesql @sql;

        SET @sql = N'ALTER TABLE dbo.CustomerProduct DROP COLUMN ' + QUOTENAME(@ColumnName) + N';';
        EXEC sys.sp_executesql @sql;

        FETCH NEXT FROM legacy_column_cursor INTO @ColumnName;
    END;

    CLOSE legacy_column_cursor;
    DEALLOCATE legacy_column_cursor;

    COMMIT;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local', 'legacy_column_cursor') >= 0 CLOSE legacy_column_cursor;
    IF CURSOR_STATUS('local', 'legacy_column_cursor') > -3 DEALLOCATE legacy_column_cursor;
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

IF COL_LENGTH(N'dbo.CustomerProduct', N'CustomerSkuUnit') IS NOT NULL
    THROW 51701, N'CustomerProduct.CustomerSkuUnit 退役失败。', 1;
IF COL_LENGTH(N'dbo.CustomerProduct', N'Currency') IS NOT NULL
    THROW 51702, N'CustomerProduct.Currency 退役失败。', 1;
