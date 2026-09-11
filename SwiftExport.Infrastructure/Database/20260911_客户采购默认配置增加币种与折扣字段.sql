/*
用途：为 dbo.CustomerPoConfig 增加币种、默认折扣率和折扣备注。
边界：仅修改该表；不回填历史数据，不修改唯一索引，不创建数据库外键。
执行：由数据库管理员在目标业务库手工执行，可重复运行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.CustomerPoConfig', N'U') IS NULL
    THROW 51000, N'未找到 dbo.CustomerPoConfig，已停止执行。', 1;

-- 已有同名列时只核对定义，禁止隐式覆盖或破坏性变更。
IF EXISTS
(
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.CustomerPoConfig') AND c.name = N'Currency'
      AND NOT (TYPE_NAME(c.user_type_id) = N'nvarchar' AND c.max_length = 20 AND c.is_nullable = 1)
)
    THROW 51001, N'dbo.CustomerPoConfig.Currency 已存在，但不是 nvarchar(10) NULL。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.CustomerPoConfig') AND c.name = N'DefaultDiscountRate'
      AND NOT (TYPE_NAME(c.user_type_id) = N'decimal' AND c.precision = 9 AND c.scale = 6 AND c.is_nullable = 1)
)
    THROW 51002, N'dbo.CustomerPoConfig.DefaultDiscountRate 已存在，但不是 decimal(9,6) NULL。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.CustomerPoConfig') AND c.name = N'DiscountRemark'
      AND NOT (TYPE_NAME(c.user_type_id) = N'nvarchar' AND c.max_length = 1000 AND c.is_nullable = 1)
)
    THROW 51003, N'dbo.CustomerPoConfig.DiscountRemark 已存在，但不是 nvarchar(500) NULL。', 1;

BEGIN TRANSACTION;
BEGIN TRY
    IF COL_LENGTH(N'dbo.CustomerPoConfig', N'Currency') IS NULL
        ALTER TABLE dbo.CustomerPoConfig ADD Currency nvarchar(10) NULL;

    IF COL_LENGTH(N'dbo.CustomerPoConfig', N'DefaultDiscountRate') IS NULL
        ALTER TABLE dbo.CustomerPoConfig ADD DefaultDiscountRate decimal(9,6) NULL;

    IF COL_LENGTH(N'dbo.CustomerPoConfig', N'DiscountRemark') IS NULL
        ALTER TABLE dbo.CustomerPoConfig ADD DiscountRemark nvarchar(500) NULL;

    -- 新增列后再编译列引用，避免同一批次预编译时报“列名无效”。
    EXEC sys.sp_executesql N'
        IF EXISTS
        (
            SELECT 1 FROM dbo.CustomerPoConfig
            WHERE DefaultDiscountRate IS NOT NULL
              AND DefaultDiscountRate < 0
        )
            THROW 51004, N''DefaultDiscountRate 存在小于 0 的数据，无法创建检查约束。'', 1;

        IF EXISTS
        (
            SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N''dbo.CustomerPoConfig'')
              AND name = N''CK_CustomerPoConfig_DefaultDiscountRate''
        )
            ALTER TABLE dbo.CustomerPoConfig DROP CONSTRAINT CK_CustomerPoConfig_DefaultDiscountRate;

        ALTER TABLE dbo.CustomerPoConfig WITH CHECK
            ADD CONSTRAINT CK_CustomerPoConfig_DefaultDiscountRate
            CHECK (DefaultDiscountRate IS NULL OR DefaultDiscountRate >= 0);
    ';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- 返回最终列定义与约束，供执行后核验。
SELECT c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType,
       c.max_length AS MaxLengthBytes, c.precision AS [Precision],
       c.scale AS Scale, c.is_nullable AS IsNullable
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(N'dbo.CustomerPoConfig')
  AND c.name IN (N'Currency', N'DefaultDiscountRate', N'DiscountRemark')
ORDER BY c.column_id;

SELECT name AS ConstraintName, definition AS ConstraintDefinition, is_disabled AS IsDisabled
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerPoConfig')
  AND name = N'CK_CustomerPoConfig_DefaultDiscountRate';
