-- 来源：Order 47-Inv260820-20260819.xlsx，Sheet1 单头 A11:P15、明细表头 A16:P16。
-- 已淘汰：旧表头别名配置仅保留历史兼容，新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
-- 这是这张模板的表头别名配置，不是全字段目录；可导入目标仍由前端模型属性标记决定。
-- 单头解析器先拆分首个冒号，去掉标签首尾空白再匹配：例如 Phone : -> Phone。
-- Ship to 按用户已确认规则将下面两个单元格文字拼接为收货地址，不另猜收货名称。
-- Terms、Photo、NO、Amount、CTNS、CBMS、Original quantity 不配置业务写入目标。
-- Terms 保留原文；照片后续通过匹配产品获取；金额／箱数／体积仅作核对。
-- 不删除或覆盖已有目标；相同表头映射到不同目标时返回冲突并终止，需先核对。
-- 默认只预览。确认所在数据库和预览结果后，将 @Apply 改为 1 才写入。
-- 未执行数据库；不创建、删除或重建索引，也不处理其他业务分组。
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @Apply bit = 0;
DECLARE @GroupCode nvarchar(40) = N'SalesOrder';
DECLARE @Operator nvarchar(100) = N'销售订单模板映射配置';

IF OBJECT_ID(N'dbo.ExcelFieldMapping', N'U') IS NULL
    THROW 53011, N'缺少 dbo.ExcelFieldMapping，请先部署表结构。', 1;

DECLARE @Mappings TABLE
(
    ExcelHeader nvarchar(200) NOT NULL,
    NormalizedHeader nvarchar(200) NOT NULL PRIMARY KEY,
    TargetFieldKey nvarchar(100) NOT NULL
);

-- 当前模板标签均为 ASCII；显式规范化结果与 ExcelHeaderNormalizer 的空白折叠、转大写一致。
-- 单头标签不含分隔冒号和具体单据值；明细保留原始换行和 Quanity 拼写。
INSERT INTO @Mappings (ExcelHeader, NormalizedHeader, TargetFieldKey) VALUES
    (N'Ship to', N'SHIP TO', N'Header.ShipToAddress'),
    (N'Phone', N'PHONE', N'Header.ShipToPhone'),
    (N'PO No', N'PO NO', N'Header.CustomerPoNo'),
    (N'Invoice No', N'INVOICE NO', N'Header.InvoiceNo'),
    (N'Date', N'DATE', N'Header.OrderDate'),
    (N'ETD', N'ETD', N'Header.Etd'),
    (N'Shipped By', N'SHIPPED BY', N'Header.ShippedBy'),
    (N'Customer Code', N'CUSTOMER CODE', N'Line.CustomerSku'),
    (N'Aquavit Code', N'AQUAVIT CODE', N'Line.LinkSku'),
    (N'Description', N'DESCRIPTION', N'Line.DescriptionEn'),
    (N'Finish', N'FINISH', N'Line.ColorNameEn'),
    (N'Features', N'FEATURES', N'Line.FeaturesEn'),
    (N'FOB Jiangmen' + NCHAR(10) + N'(USD/PC)', N'FOB JIANGMEN (USD/PC)', N'Line.UsdPrice'),
    (N'FOB Jiangmen' + NCHAR(10) + N'(CNY/PC)', N'FOB JIANGMEN (CNY/PC)', N'Line.CnyPrice'),
    (N'Quanity' + NCHAR(10) + N'(PCS)', N'QUANITY (PCS)', N'Line.Qty'),
    (N'PCS/CTN', N'PCS/CTN', N'Line.PcsOfCarton'),
    (N'GW/CTN' + NCHAR(10) + N'(Kgs)', N'GW/CTN (KGS)', N'Line.GwOfCarton'),
    (N'M3/CTN', N'M3/CTN', N'Line.CbmOfCarton');

-- 使用现有数据库的组码／表头比较语义；记录快照避免多条已存在映射导致不确定选择。
DECLARE @Existing TABLE
(
    Uid uniqueidentifier NOT NULL,
    NormalizedHeader nvarchar(200) NOT NULL,
    TargetFieldKey nvarchar(100) NOT NULL,
    IsEnabled bit NOT NULL
);

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT INTO @Existing (Uid, NormalizedHeader, TargetFieldKey, IsEnabled)
    SELECT Uid, NormalizedHeader, TargetFieldKey, IsEnabled
    FROM dbo.ExcelFieldMapping WITH (UPDLOCK, HOLDLOCK)
    WHERE GroupCode = @GroupCode AND IsDeleted = 0 AND NormalizedHeader <> N'';

    -- 即便部署实例没有正确的 UNIQUE 索引，也不在已有重复配置上任取一条。
    IF EXISTS (SELECT NormalizedHeader FROM @Existing GROUP BY NormalizedHeader HAVING COUNT(*) > 1)
    BEGIN
        SELECT e.Uid, e.NormalizedHeader, e.TargetFieldKey
        FROM @Existing AS e
        WHERE e.NormalizedHeader IN
            (SELECT NormalizedHeader FROM @Existing GROUP BY NormalizedHeader HAVING COUNT(*) > 1);
        THROW 53012, N'SalesOrder 存在重复规范化表头，请先核对，未写入任何映射。', 1;
    END;

    IF EXISTS
    (
        SELECT 1 FROM @Existing AS e
        INNER JOIN @Mappings AS m ON m.NormalizedHeader = e.NormalizedHeader
        WHERE e.TargetFieldKey COLLATE Latin1_General_100_BIN2 <> m.TargetFieldKey COLLATE Latin1_General_100_BIN2
           OR DATALENGTH(e.TargetFieldKey) <> DATALENGTH(m.TargetFieldKey)
    )
    BEGIN
        SELECT e.Uid, m.ExcelHeader, e.TargetFieldKey AS ExistingTarget, m.TargetFieldKey AS ProposedTarget
        FROM @Existing AS e
        INNER JOIN @Mappings AS m ON m.NormalizedHeader = e.NormalizedHeader
        WHERE e.TargetFieldKey COLLATE Latin1_General_100_BIN2 <> m.TargetFieldKey COLLATE Latin1_General_100_BIN2
           OR DATALENGTH(e.TargetFieldKey) <> DATALENGTH(m.TargetFieldKey);
        THROW 53013, N'已有表头对应不同目标，请先修正或软删除冲突配置，本脚本不覆盖。', 1;
    END;

    SELECT DB_NAME() AS DatabaseName, @GroupCode AS GroupCode,
           m.ExcelHeader, m.NormalizedHeader, m.TargetFieldKey,
           CASE WHEN e.Uid IS NULL THEN N'新增并启用'
                WHEN e.IsEnabled = 0 THEN N'启用已有正确配置'
                ELSE N'已有正确配置，不修改' END AS PlannedAction
    FROM @Mappings AS m
    LEFT JOIN @Existing AS e ON e.NormalizedHeader = m.NormalizedHeader
    ORDER BY m.TargetFieldKey;

    IF @Apply = 0
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'仅预览，未修改数据库。确认后将 @Apply 改为 1 再执行。';
        RETURN;
    END;

    -- 仅启用目标完全一致的旧配置，同时增加乐观并发版本，避免旧设置页覆盖。
    UPDATE target
    SET IsEnabled = 1, Version = target.Version + 1,
        UpdatedDate = SYSDATETIME(), UpdatedBy = @Operator
    FROM dbo.ExcelFieldMapping AS target
    INNER JOIN @Existing AS e ON e.Uid = target.Uid
    INNER JOIN @Mappings AS m ON m.NormalizedHeader = e.NormalizedHeader
    WHERE e.IsEnabled = 0;

    -- 空表头历史种子保留停用；新增真实别名即可，不按目标键删除或替换这些种子。
    INSERT INTO dbo.ExcelFieldMapping
        (Uid, Version, IsDeleted, CreatedDate, CreatedBy,
         GroupCode, ExcelHeader, NormalizedHeader, TargetFieldKey, IsEnabled, Remark)
    SELECT NEWID(), 1, 0, SYSDATETIME(), @Operator,
           @GroupCode, m.ExcelHeader, m.NormalizedHeader, m.TargetFieldKey, 1,
           N'Order 47 模板标签；单头先拆冒号，Ship to 下两格拼接为地址'
    FROM @Mappings AS m
    WHERE NOT EXISTS (SELECT 1 FROM @Existing AS e WHERE e.NormalizedHeader = m.NormalizedHeader);

    COMMIT TRANSACTION;
    PRINT N'映射配置已保存。已有插件内存缓存需刷新，或关闭导入页面并重新加载插件。';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
