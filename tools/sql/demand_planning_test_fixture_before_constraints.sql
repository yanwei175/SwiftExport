/*
    需求计划确定性测试夹具（004 之前）。
    前置条件：已执行 003。脚本会清空无参考意义的旧 BOM、净需求和执行 Link，
    保留已有毛需求与销售订单，并建立 A/B/C 产品及 A -> B/C BOM。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'dv'
    THROW 51510, N'Refusing to rebuild demand planning fixtures outside database dv.', 1;

IF COL_LENGTH(N'dbo.Bom', N'ParentSkuId') IS NULL
 OR COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleStatus') IS NULL
 OR OBJECT_ID(N'dbo.DemandExecutionLink', N'U') IS NULL
    THROW 51511, N'Demand planning additive schema is missing. Run 003 first.', 1;

DECLARE @ProductA uniqueidentifier = '10000000-0000-0000-0000-00000000000A';
DECLARE @ProductB uniqueidentifier = '10000000-0000-0000-0000-00000000000B';
DECLARE @ProductC uniqueidentifier = '10000000-0000-0000-0000-00000000000C';
DECLARE @Now datetime2 = SYSUTCDATETIME();

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (@ProductA, N'DP-TEST-A'),
        (@ProductB, N'DP-TEST-B'),
        (@ProductC, N'DP-TEST-C')
    ) fixture(Uid, Sku)
    JOIN dbo.Product product ON product.Uid = fixture.Uid OR product.Sku = fixture.Sku
    WHERE product.Uid <> fixture.Uid OR product.Sku <> fixture.Sku
)
    THROW 51512, N'Deterministic demand planning Product Uid/Sku conflicts with existing data.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM dbo.DemandExecutionLink;
    DELETE FROM dbo.DependentDemand;
    DELETE FROM dbo.Bom;

    DECLARE @Products TABLE
    (
        Uid uniqueidentifier NOT NULL,
        Sku nvarchar(255) NOT NULL,
        DescriptionEn nvarchar(255) NOT NULL,
        DescriptionZh nvarchar(255) NOT NULL,
        SupplyType nvarchar(255) NOT NULL,
        BomNo nvarchar(255) NULL,
        LinkSku nvarchar(255) NULL,
        MaterialType varchar(255) NOT NULL,
        BomVersion int NOT NULL
    );

    INSERT INTO @Products (Uid, Sku, DescriptionEn, DescriptionZh, SupplyType, BomNo, LinkSku, MaterialType, BomVersion)
    VALUES
        (@ProductA, N'DP-TEST-A', N'Demand planning fixture product A', N'需求计划夹具成品 A', N'采购', N'DP-TEST-BOM-A', N'DP-LINK-A', '成品', 1),
        (@ProductB, N'DP-TEST-B', N'Customer-supplied component B', N'客供件 B', N'采购', NULL, N'DP-LINK-B', '客供件', 0),
        (@ProductC, N'DP-TEST-C', N'Ordinary component C', N'普通零件 C', N'采购', NULL, N'DP-LINK-C', '零件', 0);

    UPDATE product
       SET Version = 1,
           DescriptionEn = fixture.DescriptionEn,
           DescriptionZh = fixture.DescriptionZh,
           SupplyType = fixture.SupplyType,
           BomNo = fixture.BomNo,
           LinkSku = fixture.LinkSku,
           MaterialType = fixture.MaterialType,
           BomVersion = fixture.BomVersion,
           Note = N'DemandPlanning deterministic test fixture',
           IsDeleted = 0,
           UpdatedDate = @Now,
           UpdatedBy = N'demand-planning-fixture'
    FROM dbo.Product product
    JOIN @Products fixture ON fixture.Uid = product.Uid;

    INSERT INTO dbo.Product
    (
        Uid, Version, Sku, DescriptionEn, DescriptionZh, SupplyType, BomNo, Note,
        LinkSku, IsDeleted, CreatedDate, CreatedBy, MaterialType, BomVersion
    )
    SELECT fixture.Uid, 1, fixture.Sku, fixture.DescriptionEn, fixture.DescriptionZh,
           fixture.SupplyType, fixture.BomNo, N'DemandPlanning deterministic test fixture',
           fixture.LinkSku, 0, @Now, N'demand-planning-fixture', fixture.MaterialType, fixture.BomVersion
    FROM @Products fixture
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Product product WHERE product.Uid = fixture.Uid);

    INSERT INTO dbo.Bom
    (
        Uid, Version, SubSkuId, BomNo, SubSku, Numerator, BomSubType,
        IsDeleted, CreatedDate, IsCustomerSupplied, Denominator, Note, CreatedBy,
        BomVersion, ParentSkuId, ParentSku, LineNumber
    )
    VALUES
        ('20000000-0000-0000-0000-0000000000B1', 1, @ProductB, N'DP-TEST-BOM-A', N'DP-TEST-B', 2, N'常规',
         0, @Now, 1, 1, N'客供件 B：送达 A 的计划供应商', N'demand-planning-fixture', 1, @ProductA, N'DP-TEST-A', 1),
        ('20000000-0000-0000-0000-0000000000C1', 1, @ProductC, N'DP-TEST-BOM-A', N'DP-TEST-C', 3, N'常规',
         0, @Now, 0, 1, N'普通子件 C：毛需求拆解时忽略', N'demand-planning-fixture', 1, @ProductA, N'DP-TEST-A', 2);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT (SELECT COUNT(*) FROM dbo.Bom WHERE IsDeleted = 0) AS ActiveBomCount,
       (SELECT COUNT(*) FROM dbo.Product WHERE Uid IN (@ProductA, @ProductB, @ProductC) AND IsDeleted = 0) AS FixtureProductCount,
       (SELECT COUNT(*) FROM dbo.IndependentDemand) AS PreservedGrossDemandCount,
       (SELECT COUNT(*) FROM dbo.DependentDemand) AS NetDemandCount,
       (SELECT COUNT(*) FROM dbo.DemandExecutionLink) AS ExecutionLinkCount;
