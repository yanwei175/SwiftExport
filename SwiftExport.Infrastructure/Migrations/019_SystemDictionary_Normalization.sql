/*
  规范化客户与销售订单使用的通用系统字典。
  - 字典类型受系统保护，字典值仍允许在字典管理页面维护。
  - Currency 继续由 SysDicCurrency 维护。
  - ProductCategory/ProductColor 已有独立主数据，本迁移仅删除它们在通用字典中的遗留记录。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SysDictType', N'U') IS NULL
    THROW 51900, N'dbo.SysDictType 不存在，无法规范化系统字典。', 1;
IF OBJECT_ID(N'dbo.SysDictData', N'U') IS NULL
    THROW 51901, N'dbo.SysDictData 不存在，无法规范化系统字典。', 1;

BEGIN TRY
    BEGIN TRAN;

    DELETE FROM dbo.SysDictData WHERE DictCode IN (N'ProductCategory', N'ProductColor');
    DELETE FROM dbo.SysDictType WHERE DictCode IN (N'ProductCategory', N'ProductColor');

    DECLARE @Types TABLE
    (
        DictCode nvarchar(100) NOT NULL PRIMARY KEY,
        DictName nvarchar(200) NOT NULL,
        SortOrder int NOT NULL
    );
    INSERT @Types(DictCode, DictName, SortOrder) VALUES
        (N'Incoterm', N'国际贸易术语', 20),
        (N'PaymentMethod', N'付款方式', 30),
        (N'ShippedBy', N'运输方式', 40);

    ;WITH ranked_types AS
    (
        SELECT target.Uid,
               ROW_NUMBER() OVER
               (
                   PARTITION BY target.DictCode
                   ORDER BY ISNULL(target.IsDeleted, 0), target.Uid
               ) AS RowNo
        FROM dbo.SysDictType target
        JOIN @Types source ON source.DictCode = target.DictCode
    )
    DELETE target
    FROM dbo.SysDictType target
    JOIN ranked_types ranked ON ranked.Uid = target.Uid
    WHERE ranked.RowNo > 1;

    UPDATE target
       SET target.DictName = source.DictName,
           target.IsSystem = 1,
           target.SortOrder = source.SortOrder,
           target.GroupCode = N'Sales',
           target.GroupName = N'销售',
           target.IsDeleted = 0,
           target.Version = CASE WHEN target.Version IS NULL OR target.Version < 1 THEN 1 ELSE target.Version END,
           target.UpdatedDate = SYSUTCDATETIME()
    FROM dbo.SysDictType target
    JOIN @Types source ON source.DictCode = target.DictCode;

    INSERT dbo.SysDictType
    (
        Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy,
        DictCode, DictName, IsSystem, Remark, ParentId, SortOrder, TargetViewKey, GroupCode, GroupName
    )
    SELECT NEWID(), 1, 0, SYSUTCDATETIME(), N'migration-019', NULL, NULL,
           source.DictCode, source.DictName, 1, N'系统字典类型；字典值允许维护。', NULL,
           source.SortOrder, NULL, N'Sales', N'销售'
    FROM @Types source
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.SysDictType target WHERE target.DictCode = source.DictCode
    );

    DECLARE @Values TABLE
    (
        DictCode nvarchar(100) NOT NULL,
        DataNameEn nvarchar(200) NOT NULL,
        DataNameZh nvarchar(200) NOT NULL,
        SortOrder int NOT NULL,
        PRIMARY KEY(DictCode, DataNameEn)
    );
    INSERT @Values(DictCode, DataNameEn, DataNameZh, SortOrder) VALUES
        (N'Incoterm', N'EXW', N'工厂交货', 10),
        (N'Incoterm', N'FOB', N'船上交货', 20),
        (N'Incoterm', N'CIF', N'成本、保险加运费', 30),
        (N'Incoterm', N'DAP', N'目的地交货', 40),
        (N'Incoterm', N'DDP', N'完税后交货', 50),
        (N'Incoterm', N'CPT', N'运费付至', 60),
        (N'Incoterm', N'CIP', N'运费和保险费付至', 70),
        (N'PaymentMethod', N'TT', N'电汇', 10),
        (N'PaymentMethod', N'LC', N'信用证', 20),
        (N'PaymentMethod', N'OA', N'赊账', 30),
        (N'PaymentMethod', N'DP', N'付款交单', 40),
        (N'PaymentMethod', N'DA', N'承兑交单', 50),
        (N'ShippedBy', N'SEA', N'海运', 10),
        (N'ShippedBy', N'AIR', N'空运', 20),
        (N'ShippedBy', N'COURIER', N'快递', 30);

    INSERT dbo.SysDictData
    (
        Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy,
        DictCode, DataNameEn, DataNameZh, Remark, SortOrder, CssClass, IsSystem, IsEnabled
    )
    SELECT NEWID(), 1, 0, SYSUTCDATETIME(), N'migration-019', NULL, NULL,
           source.DictCode, source.DataNameEn, source.DataNameZh, NULL, source.SortOrder, NULL, 0, 1
    FROM @Values source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.SysDictData target
        WHERE target.DictCode = source.DictCode
          AND target.DataNameEn = source.DataNameEn
          AND ISNULL(target.IsDeleted, 0) = 0
    );

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

IF EXISTS (SELECT 1 FROM dbo.SysDictType WHERE DictCode IN (N'ProductCategory', N'ProductColor'))
    THROW 51902, N'通用字典中的 ProductCategory/ProductColor 遗留类型清理失败。', 1;
IF EXISTS (SELECT 1 FROM dbo.SysDictData WHERE DictCode IN (N'ProductCategory', N'ProductColor'))
    THROW 51903, N'通用字典中的 ProductCategory/ProductColor 遗留值清理失败。', 1;
IF EXISTS
(
    SELECT required.DictCode
    FROM (VALUES(N'Incoterm'), (N'PaymentMethod'), (N'ShippedBy')) required(DictCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.SysDictType actual
        WHERE actual.DictCode = required.DictCode AND actual.IsSystem = 1 AND ISNULL(actual.IsDeleted, 0) = 0
    )
)
    THROW 51904, N'客户与销售订单系统字典类型初始化失败。', 1;
