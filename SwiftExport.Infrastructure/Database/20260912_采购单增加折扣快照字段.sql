/*
用途：为 dbo.PurchaseOrderHead 增加价格倍率快照和用户手工填写的整单优惠金额快照。
边界：DiscountAmount 不由 DiscountRate 自动计算或覆盖；不新增或回填采购总额；不把 NULL 解释为原价或零优惠；不计算折后金额。
执行：由数据库管理员在目标业务库审核后手工执行，可重复运行。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PurchaseOrderHead', N'U') IS NULL
    THROW 51000, N'未找到 dbo.PurchaseOrderHead，已停止执行。', 1;

-- 已有同名列时只核对定义，禁止隐式覆盖或破坏性变更。
IF EXISTS
(
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.PurchaseOrderHead') AND c.name = N'DiscountRate'
      AND NOT (TYPE_NAME(c.user_type_id) = N'decimal' AND c.precision = 9 AND c.scale = 6 AND c.is_nullable = 1)
)
    THROW 51001, N'dbo.PurchaseOrderHead.DiscountRate 已存在，但不是 decimal(9,6) NULL。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.PurchaseOrderHead') AND c.name = N'DiscountAmount'
      AND NOT (TYPE_NAME(c.user_type_id) = N'decimal' AND c.precision = 18 AND c.scale = 4 AND c.is_nullable = 1)
)
    THROW 51002, N'dbo.PurchaseOrderHead.DiscountAmount 已存在，但不是 decimal(18,4) NULL。', 1;

BEGIN TRANSACTION;
BEGIN TRY
    IF COL_LENGTH(N'dbo.PurchaseOrderHead', N'DiscountRate') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD DiscountRate decimal(9,6) NULL;

    IF COL_LENGTH(N'dbo.PurchaseOrderHead', N'DiscountAmount') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead ADD DiscountAmount decimal(18,4) NULL;

    -- 新增列后再编译列引用，避免同一批次预编译时报“列名无效”。
    EXEC sys.sp_executesql N'
        IF EXISTS
        (
            SELECT 1 FROM dbo.PurchaseOrderHead
            WHERE DiscountRate IS NOT NULL AND DiscountRate < 0
        )
            THROW 51003, N''DiscountRate 存在小于 0 的数据，无法创建检查约束。'', 1;

        IF EXISTS
        (
            SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N''dbo.PurchaseOrderHead'')
              AND name = N''CK_PurchaseOrderHead_DiscountRate''
        )
            ALTER TABLE dbo.PurchaseOrderHead DROP CONSTRAINT CK_PurchaseOrderHead_DiscountRate;

        ALTER TABLE dbo.PurchaseOrderHead WITH CHECK
            ADD CONSTRAINT CK_PurchaseOrderHead_DiscountRate
            CHECK (DiscountRate IS NULL OR DiscountRate >= 0);
    ';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- 返回最终列定义与约束，供执行后核验。
SELECT c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType,
       c.precision AS [Precision], c.scale AS Scale, c.is_nullable AS IsNullable
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(N'dbo.PurchaseOrderHead')
  AND c.name IN (N'DiscountRate', N'DiscountAmount')
ORDER BY c.column_id;

SELECT name AS ConstraintName, definition AS ConstraintDefinition, is_disabled AS IsDisabled
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.PurchaseOrderHead')
  AND name = N'CK_PurchaseOrderHead_DiscountRate';
