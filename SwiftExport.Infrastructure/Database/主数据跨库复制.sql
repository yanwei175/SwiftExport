/*
用途：
  在同一 SQL Server 实例内，把同版本 SwiftExport 数据库的主数据复制到已经执行
  Generated/SwiftExport.Schema.sql 的全新空数据库。

使用前：
  1. 备份旧数据库。
  2. 在新数据库执行当前 Generated/SwiftExport.Schema.sql。
  3. 不要先运行 DbSeed；本脚本要求目标主数据表全部为空。
  4. 修改下面两个数据库名，在维护窗口内执行本脚本。

边界：
  - 只复制 @MasterTables 中列出的主数据、账号和权限关系。
  - 不复制销售、需求、采购、入库、出库、库存、PackingList、售后或操作日志。
  - 源库和目标库必须位于同一 SQL Server 实例且都使用当前 RBAC 结构；不转换旧授权数据。
  - 权限定义先复制，再复制角色权限关系，并核对 PermissionKey 对应的 Uid 相同。
  - 目标必填列若在旧库不存在且没有默认值，脚本会失败并回滚。
  - 无目标的逻辑引用会原样保留并作为迁移警告返回，不阻断主数据复制。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SourceDatabase sysname = N'dv_next';
DECLARE @TargetDatabase sysname = N'dv_next_new';

IF @SourceDatabase IN (N'', N'请填写旧数据库名')
    THROW 52200, N'请先填写 @SourceDatabase。', 1;

IF @TargetDatabase IN (N'', N'请填写新数据库名')
    THROW 52201, N'请先填写 @TargetDatabase。', 1;

IF @SourceDatabase = @TargetDatabase
    THROW 52202, N'源数据库和目标数据库不能相同。', 1;

IF DB_ID(@SourceDatabase) IS NULL
    THROW 52203, N'源数据库不存在或当前账号无权访问。', 1;

IF DB_ID(@TargetDatabase) IS NULL
    THROW 52204, N'目标数据库不存在或当前账号无权访问。', 1;

/*
必须从拼接链起点使用 nvarchar(max)。仅把最终接收变量声明为 nvarchar(max) 不足以防止
右侧表达式先按 nvarchar(4000) 截断；完整性检查 SQL 较长，曾因此截在 Customer 标识符中间。
*/
DECLARE @SourceQuoted nvarchar(max) = CONVERT(nvarchar(max), QUOTENAME(@SourceDatabase));
DECLARE @TargetQuoted nvarchar(max) = CONVERT(nvarchar(max), QUOTENAME(@TargetDatabase));

DECLARE @MasterTables table
(
    SortOrder int NOT NULL PRIMARY KEY,
    TableName sysname NOT NULL UNIQUE
);

/*
复制顺序按当前逻辑引用排列：先独立主表，再产品，再关系表，最后账号权限关系。
表名与当前实体生成基线保持一致。
*/
INSERT INTO @MasterTables(SortOrder, TableName)
VALUES
    (10,  N'ProductCategory'),
    (20,  N'ProductColor'),
    (30,  N'Supplier'),
    (40,  N'Customer'),
    (50,  N'SysDicCurrency'),
    (60,  N'SysDicCurrencyExchange'),
    (70,  N'SysDictType'),
    (80,  N'SysDictData'),
    (85,  N'ExcelFieldMapping'),
    (90,  N'SysRoles'),
    (100, N'SysUser'),
    (120, N'ProductMaster'),
    (130, N'Product'),
    (140, N'ProductNeutral'),
    (150, N'ProductDetails'),
    (160, N'ProductPackage'),
    (170, N'Bom'),
    (180, N'CustomerPoConfig'),
    (190, N'CustomerProduct'),
    (200, N'SupplierProduct'),
    (210, N'WarehouseSite'),
    (220, N'SysPermissionDefinitions'),
    (230, N'SysRole_Permissions'),
    (240, N'SysUser_Roles');

/* 支持在同一个查询会话内修正配置后安全重跑。 */
DROP TABLE IF EXISTS #MigrationCounts;
DROP TABLE IF EXISTS #IntegrityIssues;

CREATE TABLE #MigrationCounts
(
    SortOrder int NOT NULL,
    TableName sysname NOT NULL,
    SourceRows bigint NOT NULL,
    InsertedRows bigint NOT NULL,
    TargetRows bigint NOT NULL
);

CREATE TABLE #IntegrityIssues
(
    RelationName nvarchar(300) NOT NULL,
    InvalidRows bigint NOT NULL
);

DECLARE
    @CurrentOrder int,
    @MaxOrder int,
    @TableName sysname,
    @Sql nvarchar(max),
    @Columns nvarchar(max),
    @MissingColumns nvarchar(max),
    @SourceRows bigint,
    @TargetRows bigint,
    @InsertedRows bigint,
    @Exists bit,
    @Stage nvarchar(100),
    @IntegritySummary nvarchar(max),
    @ErrorMessage nvarchar(2048);

SELECT
    @CurrentOrder = MIN(SortOrder),
    @MaxOrder = MAX(SortOrder)
FROM @MasterTables;

BEGIN TRY
    /* 在写入前验证源库和目标库都包含全部目标表。 */
    WHILE @CurrentOrder IS NOT NULL AND @CurrentOrder <= @MaxOrder
    BEGIN
        SELECT @TableName = TableName
        FROM @MasterTables
        WHERE SortOrder = @CurrentOrder;

        IF @TableName IS NOT NULL
        BEGIN
            SET @Stage = N'验证源表存在';
            SET @Exists = 0;
            SET @Sql = N'
                SELECT @Exists = CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM ' + @SourceQuoted + N'.sys.tables t
                    INNER JOIN ' + @SourceQuoted + N'.sys.schemas s ON s.schema_id=t.schema_id
                    WHERE s.name=N''dbo'' AND t.name=@TableName
                ) THEN 1 ELSE 0 END);';
            EXEC sys.sp_executesql
                @Sql,
                N'@TableName sysname,@Exists bit OUTPUT',
                @TableName,
                @Exists OUTPUT;

            IF @Exists = 0
            BEGIN
                SET @ErrorMessage = N'源数据库缺少 dbo.' + @TableName + N'。';
                THROW 52205, @ErrorMessage, 1;
            END;

            SET @Stage = N'验证目标表存在';
            SET @Exists = 0;
            SET @Sql = N'
                SELECT @Exists = CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM ' + @TargetQuoted + N'.sys.tables t
                    INNER JOIN ' + @TargetQuoted + N'.sys.schemas s ON s.schema_id=t.schema_id
                    WHERE s.name=N''dbo'' AND t.name=@TableName
                ) THEN 1 ELSE 0 END);';
            EXEC sys.sp_executesql
                @Sql,
                N'@TableName sysname,@Exists bit OUTPUT',
                @TableName,
                @Exists OUTPUT;

            IF @Exists = 0
            BEGIN
                SET @ErrorMessage = N'目标数据库缺少 dbo.' + @TableName
                    + N'，请先执行当前 Generated/SwiftExport.Schema.sql。';
                THROW 52206, @ErrorMessage, 1;
            END;
        END;

        SELECT @CurrentOrder = MIN(SortOrder)
        FROM @MasterTables
        WHERE SortOrder > @CurrentOrder;
    END;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    SELECT @CurrentOrder = MIN(SortOrder)
    FROM @MasterTables;

    WHILE @CurrentOrder IS NOT NULL AND @CurrentOrder <= @MaxOrder
    BEGIN
        SELECT @TableName = TableName
        FROM @MasterTables
        WHERE SortOrder = @CurrentOrder;

        SET @Stage = N'验证目标表为空';
        SET @TargetRows = 0;
        SET @Sql = N'SELECT @Rows=COUNT_BIG(1) FROM '
            + @TargetQuoted + N'.dbo.' + QUOTENAME(@TableName) + N' WITH (HOLDLOCK);';
        EXEC sys.sp_executesql @Sql, N'@Rows bigint OUTPUT', @TargetRows OUTPUT;

        IF @TargetRows <> 0
        BEGIN
            SET @ErrorMessage = N'目标表 dbo.' + @TableName
                + N' 不是空表。为避免覆盖或合并已有数据，迁移已回滚。';
            THROW 52207, @ErrorMessage, 1;
        END;

        SET @Columns = NULL;
        SET @MissingColumns = NULL;
        SET @Stage = N'生成同名列清单';
        SET @Sql = N'
            SELECT @Columns = STRING_AGG(
                    CONVERT(nvarchar(max),QUOTENAME(tc.name)),N'','')
            FROM ' + @TargetQuoted + N'.sys.tables tt
            INNER JOIN ' + @TargetQuoted + N'.sys.schemas ts ON ts.schema_id=tt.schema_id
            INNER JOIN ' + @TargetQuoted + N'.sys.columns tc ON tc.object_id=tt.object_id
            INNER JOIN ' + @SourceQuoted + N'.sys.schemas ss ON ss.name=ts.name
            INNER JOIN ' + @SourceQuoted + N'.sys.tables st
                ON st.schema_id=ss.schema_id AND st.name=tt.name
            INNER JOIN ' + @SourceQuoted + N'.sys.columns sc
                ON sc.object_id=st.object_id AND sc.name=tc.name
            WHERE ts.name=N''dbo'' AND tt.name=@TableName
              AND tc.is_computed=0
              AND tc.is_identity=0;

            SELECT @MissingColumns = STRING_AGG(
                    CONVERT(nvarchar(max),QUOTENAME(tc.name)),N'', '')
            FROM ' + @TargetQuoted + N'.sys.tables tt
            INNER JOIN ' + @TargetQuoted + N'.sys.schemas ts ON ts.schema_id=tt.schema_id
            INNER JOIN ' + @TargetQuoted + N'.sys.columns tc ON tc.object_id=tt.object_id
            LEFT JOIN ' + @SourceQuoted + N'.sys.schemas ss ON ss.name=ts.name
            LEFT JOIN ' + @SourceQuoted + N'.sys.tables st
                ON st.schema_id=ss.schema_id AND st.name=tt.name
            LEFT JOIN ' + @SourceQuoted + N'.sys.columns sc
                ON sc.object_id=st.object_id AND sc.name=tc.name
            WHERE ts.name=N''dbo'' AND tt.name=@TableName
              AND tc.is_computed=0
              AND tc.is_identity=0
              AND tc.is_nullable=0
              AND tc.default_object_id=0
              AND sc.column_id IS NULL;';

        EXEC sys.sp_executesql
            @Sql,
            N'@TableName sysname,@Columns nvarchar(max) OUTPUT,@MissingColumns nvarchar(max) OUTPUT',
            @TableName,
            @Columns OUTPUT,
            @MissingColumns OUTPUT;

        IF NULLIF(@MissingColumns, N'') IS NOT NULL
        BEGIN
            SET @ErrorMessage = N'旧表 dbo.' + @TableName
                + N' 缺少目标库必填列：' + @MissingColumns + N'。迁移已回滚。';
            THROW 52208, @ErrorMessage, 1;
        END;

        IF NULLIF(@Columns, N'') IS NULL
        BEGIN
            SET @ErrorMessage = N'源表和目标表 dbo.' + @TableName + N' 没有可复制的同名列。';
            THROW 52209, @ErrorMessage, 1;
        END;

        SET @Stage = N'统计源表行数';
        SET @SourceRows = 0;
        SET @Sql = N'SELECT @Rows=COUNT_BIG(1) FROM '
            + @SourceQuoted + N'.dbo.' + QUOTENAME(@TableName) + N' WITH (HOLDLOCK);';
        EXEC sys.sp_executesql @Sql, N'@Rows bigint OUTPUT', @SourceRows OUTPUT;

        SET @Stage = N'复制主数据';
        SET @InsertedRows = 0;
        SET @Sql = N'
            INSERT INTO ' + @TargetQuoted + N'.dbo.' + QUOTENAME(@TableName)
                + N' (' + @Columns + N')
            SELECT ' + @Columns + N'
            FROM ' + @SourceQuoted + N'.dbo.' + QUOTENAME(@TableName) + N' WITH (HOLDLOCK);
            SET @Inserted=CONVERT(bigint,@@ROWCOUNT);';
        EXEC sys.sp_executesql @Sql, N'@Inserted bigint OUTPUT', @InsertedRows OUTPUT;

        SET @Stage = N'核对目标表行数';
        SET @TargetRows = 0;
        SET @Sql = N'SELECT @Rows=COUNT_BIG(1) FROM '
            + @TargetQuoted + N'.dbo.' + QUOTENAME(@TableName) + N' WITH (HOLDLOCK);';
        EXEC sys.sp_executesql @Sql, N'@Rows bigint OUTPUT', @TargetRows OUTPUT;

        IF @SourceRows <> @InsertedRows OR @SourceRows <> @TargetRows
        BEGIN
            SET @ErrorMessage = N'dbo.' + @TableName
                + N' 的源行数、插入行数和目标行数不一致，迁移已回滚。';
            THROW 52210, @ErrorMessage, 1;
        END;

        INSERT INTO #MigrationCounts(SortOrder,TableName,SourceRows,InsertedRows,TargetRows)
        VALUES(@CurrentOrder,@TableName,@SourceRows,@InsertedRows,@TargetRows);

        SELECT @CurrentOrder = MIN(SortOrder)
        FROM @MasterTables
        WHERE SortOrder > @CurrentOrder;
    END;

    /*
    当前生成基线不建立数据库外键，因此迁移后显式验证关键逻辑引用。
    被软删除的主数据也会完整复制，并可继续作为历史引用目标。
    */
    SET @Stage = N'验证逻辑引用';
    SET @TableName = NULL;
    SET @Sql = N'
        INSERT INTO #IntegrityIssues(RelationName,InvalidRows)
        SELECT N''ProductCategory.ParentId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductCategory] c
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductCategory] p ON p.Uid=c.ParentId
        WHERE c.ParentId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''SysDictType.ParentId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SysDictType] c
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[SysDictType] p ON p.Uid=c.ParentId
        WHERE c.ParentId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''ProductMaster.CategoryId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductMaster] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductCategory] p ON p.Uid=x.CategoryId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''ProductMaster.DefaultSupplierId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductMaster] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Supplier] p ON p.Uid=x.DefaultSupplierId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''Product.ProductMasterId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[Product] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductMaster] p ON p.Uid=x.ProductMasterId
        WHERE x.ProductMasterId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''Product.ColorId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[Product] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductColor] p ON p.Uid=x.ColorId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''Product.DefaultSupplierId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[Product] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Supplier] p ON p.Uid=x.DefaultSupplierId
        WHERE x.DefaultSupplierId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''ProductNeutral.ProductMasterId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductNeutral] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductMaster] p ON p.Uid=x.ProductMasterId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''ProductNeutral.ColorId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductNeutral] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[ProductColor] p ON p.Uid=x.ColorId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''ProductDetails.ProductId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductDetails] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.ProductId
        WHERE x.ProductId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''ProductPackage.ProductId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[ProductPackage] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.ProductId
        WHERE x.ProductId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''Bom.ParentProductId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[Bom] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.ParentProductId
        WHERE x.ParentProductId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''Bom.ComponentProductId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[Bom] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.ComponentProductId
        WHERE x.ComponentProductId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''CustomerPoConfig.CustomerId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[CustomerPoConfig] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Customer] p ON p.Uid=x.CustomerId
        WHERE x.CustomerId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''CustomerPoConfig.SupplierId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[CustomerPoConfig] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Supplier] p ON p.Uid=x.SupplierId
        WHERE x.SupplierId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''CustomerProduct.CustomerId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[CustomerProduct] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Customer] p ON p.Uid=x.CustomerId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''CustomerProduct.SkuId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[CustomerProduct] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.SkuId
        WHERE x.SkuId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''SupplierProduct.SupplierId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SupplierProduct] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Supplier] p ON p.Uid=x.SupplierId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''SupplierProduct.SkuId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SupplierProduct] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Product] p ON p.Uid=x.SkuId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''WarehouseSite.SupplierId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[WarehouseSite] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[Supplier] p ON p.Uid=x.SupplierId
        WHERE x.SupplierId IS NOT NULL AND p.Uid IS NULL
        UNION ALL SELECT N''SysRole_Permissions.RoleId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SysRole_Permissions] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[SysRoles] p ON p.Uid=x.RoleId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''SysRole_Permissions.PermissionId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SysRole_Permissions] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[SysPermissionDefinitions] p ON p.Uid=x.PermissionId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''SysUser_Roles.UserId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SysUser_Roles] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[SysUser] p ON p.Uid=x.UserId
        WHERE p.Uid IS NULL
        UNION ALL SELECT N''SysUser_Roles.RoleId'',COUNT_BIG(1)
        FROM ' + @TargetQuoted + N'.dbo.[SysUser_Roles] x
        LEFT JOIN ' + @TargetQuoted + N'.dbo.[SysRoles] p ON p.Uid=x.RoleId
        WHERE p.Uid IS NULL;';

    EXEC sys.sp_executesql @Sql;

    /* 同版本复制必须保持稳定键与标识一致，否则角色权限关系不得提交。 */
    SET @Stage = N'核对权限定义稳定键与标识';
    SET @Sql = N'
        INSERT INTO #IntegrityIssues(RelationName,InvalidRows)
        SELECT N''SysPermissionDefinitions.PermissionKey->Uid'',COUNT_BIG(1)
        FROM ' + @SourceQuoted + N'.dbo.[SysPermissionDefinitions] s
        FULL JOIN ' + @TargetQuoted + N'.dbo.[SysPermissionDefinitions] t
            ON t.PermissionKey=s.PermissionKey
        WHERE s.Uid IS NULL OR t.Uid IS NULL OR s.Uid<>t.Uid;';
    EXEC sys.sp_executesql @Sql;

    DELETE FROM #IntegrityIssues WHERE InvalidRows=0;

    SET @IntegritySummary = N'';
    IF EXISTS(SELECT 1 FROM #IntegrityIssues)
    BEGIN
        SELECT @IntegritySummary = @IntegritySummary
            + CASE WHEN LEN(@IntegritySummary) = 0 THEN N'' ELSE N'；' END
            + RelationName + N'=' + CONVERT(nvarchar(30), InvalidRows)
        FROM #IntegrityIssues
        ORDER BY RelationName;
    END;

    COMMIT TRANSACTION;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    SELECT TableName,SourceRows,InsertedRows,TargetRows
    FROM #MigrationCounts
    ORDER BY SortOrder;

    /* 逻辑引用警告单独返回，供前端或人工筛选治理，不修改旧数据。 */
    SELECT RelationName,InvalidRows
    FROM #IntegrityIssues
    ORDER BY RelationName;

    DROP TABLE IF EXISTS #IntegrityIssues;
    DROP TABLE IF EXISTS #MigrationCounts;

    SELECT
        CASE WHEN NULLIF(@IntegritySummary, N'') IS NULL
            THEN N'主数据跨库复制成功；业务单据、库存事实和操作日志均未复制。'
            ELSE N'主数据跨库复制成功，但存在无效逻辑引用；请由前端或人工筛选治理。'
        END AS Result,
        NULLIF(@IntegritySummary, N'') AS IntegrityWarnings;
END TRY
BEGIN CATCH
    DECLARE @CaughtErrorNumber int = ERROR_NUMBER();
    DECLARE @CaughtErrorLine int = ERROR_LINE();
    DECLARE @CaughtErrorMessage nvarchar(2048) = ERROR_MESSAGE();

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    DROP TABLE IF EXISTS #IntegrityIssues;
    DROP TABLE IF EXISTS #MigrationCounts;

    SELECT
        @Stage AS MigrationStage,
        @TableName AS CurrentTable,
        @CaughtErrorNumber AS SqlErrorNumber,
        @CaughtErrorLine AS SqlErrorLine,
        @CaughtErrorMessage AS SqlErrorMessage;

    SET @ErrorMessage = LEFT(
        N'主数据复制失败；阶段=' + COALESCE(@Stage, N'未知')
        + N'；表=' + COALESCE(@TableName, N'无')
        + N'；原始错误=' + CONVERT(nvarchar(20), @CaughtErrorNumber)
        + N'；原始行=' + CONVERT(nvarchar(20), @CaughtErrorLine)
        + N'；消息=' + COALESCE(@CaughtErrorMessage, N'无')
        + N'；动态SQL=' + COALESCE(LEFT(@Sql, 900), N'无'),
        2048);
    THROW 52212, @ErrorMessage, 1;
END CATCH;
