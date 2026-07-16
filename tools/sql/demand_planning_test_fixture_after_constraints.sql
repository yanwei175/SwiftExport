/*
    需求计划确定性手工毛需求夹具（004 之后）。
    固定创建一条标准件 MD 和一条非标件 MD；没有活动下游时重复执行会恢复到活动、待拆解基线，
    并保留历史最大拆解 revision，避免与已回退历史冲突。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'dv'
    THROW 51520, N'Refusing to create demand planning manual fixtures outside database dv.', 1;

IF COL_LENGTH(N'dbo.IndependentDemand', N'LifecycleStatus') IS NULL
 OR COL_LENGTH(N'dbo.IndependentDemand', N'DecompositionStatus') IS NULL
 OR OBJECT_ID(N'dbo.CK_IndependentDemand_TargetStatuses', N'C') IS NULL
    THROW 51521, N'Demand planning constraints are missing. Run 004 first.', 1;

DECLARE @ProductA uniqueidentifier = '10000000-0000-0000-0000-00000000000A';
DECLARE @StandardDemand uniqueidentifier = '30000000-0000-0000-0000-000000000001';
DECLARE @NonStandardDemand uniqueidentifier = '30000000-0000-0000-0000-000000000002';
DECLARE @Now datetime2 = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM dbo.Product WHERE Uid = @ProductA AND Sku = N'DP-TEST-A' AND IsDeleted = 0)
    THROW 51522, N'Deterministic Product A fixture is missing.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.IndependentDemand
    WHERE SourceType = N'MD'
      AND SourceNo IN (N'MD-DP-TEST-001', N'MD-DP-TEST-002')
      AND Uid NOT IN (@StandardDemand, @NonStandardDemand)
)
    THROW 51523, N'Deterministic manual demand SourceNo conflicts with existing data.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM dbo.DependentDemand
        WHERE IndependentDemandId IN (@StandardDemand, @NonStandardDemand)
          AND IsDeleted = 0
          AND LifecycleStatus = 0
    ) OR EXISTS (
        SELECT 1
        FROM dbo.DemandExecutionLink link
        JOIN dbo.DependentDemand net ON net.Uid = link.DependentDemandId
        WHERE net.IndependentDemandId IN (@StandardDemand, @NonStandardDemand)
          AND link.IsDeleted = 0
          AND link.LinkStatus = 0
    )
        THROW 51524, N'Cannot reset manual demand fixtures while active downstream demand or execution links exist.', 1;

    DECLARE @Demands TABLE
    (
        Uid uniqueidentifier NOT NULL,
        SourceNo nvarchar(50) NOT NULL,
        ProductId uniqueidentifier NULL,
        IsNonStandard bit NOT NULL,
        Sku nvarchar(100) NOT NULL,
        LinkSku nvarchar(255) NULL,
        CustomerSku nvarchar(255) NULL,
        Description nvarchar(255) NOT NULL,
        ColorName nvarchar(255) NULL,
        Features nvarchar(max) NULL,
        OriginalQty decimal(18,4) NOT NULL,
        Remark nvarchar(max) NULL
    );

    INSERT INTO @Demands
    (Uid, SourceNo, ProductId, IsNonStandard, Sku, LinkSku, CustomerSku, Description, ColorName, Features, OriginalQty, Remark)
    VALUES
        (@StandardDemand, N'MD-DP-TEST-001', @ProductA, 0, N'DP-TEST-A', N'DP-LINK-A', N'CUSTOMER-A',
         N'标准件手工毛需求测试夹具', N'Fixture Color', N'Fixture Specification A', 10, N'标准件 A 手工需求'),
        (@NonStandardDemand, N'MD-DP-TEST-002', NULL, 1, N'NS-DP-TEST-001', NULL, N'CUSTOMER-NS-001',
         N'非标手工毛需求测试夹具', N'Custom Color', N'Custom Specification', 5, N'非标件不查询 Product/BOM');

    UPDATE demand
       SET Version = 1,
           IsDeleted = 0,
           UpdatedDate = @Now,
           UpdatedBy = N'demand-planning-fixture',
           ApprovalStatus = 0,
           OrderStatus = 1,
           ConfirmationStatus = 1,
           SourceType = N'MD',
           SourceUid = fixture.Uid,
           SourceNo = fixture.SourceNo,
           SourceLineId = fixture.Uid,
           SourceLineNumber = 1,
           ProductId = fixture.ProductId,
           SourceSku = fixture.CustomerSku,
           Sku = fixture.Sku,
           LinkSkuSnapshot = fixture.LinkSku,
           Description = fixture.Description,
           ColorName = fixture.ColorName,
           Features = fixture.Features,
           OriginalQty = fixture.OriginalQty,
           FilledQty = 0,
           ShippedQty = 0,
           IsShipped = 0,
           KittingRate = 0,
           HopefulDate = DATEADD(day, 30, CONVERT(date, @Now)),
           Remark = fixture.Remark,
           IsDecomposed = 0,
           DecomposedTime = NULL,
           IsNonStandard = fixture.IsNonStandard,
           LifecycleStatus = 0,
           DecompositionStatus = 0,
           DecompositionRevision = ISNULL((
               SELECT MAX(history.DecompositionRevision)
               FROM dbo.DependentDemand history
               WHERE history.IndependentDemandId = demand.Uid
           ), 0),
           ImagePathSnapshot = NULL,
           DecomposedBy = NULL,
           LifecycleChangedAt = NULL,
           LifecycleChangedBy = NULL,
           LifecycleChangedReason = NULL
    FROM dbo.IndependentDemand demand
    JOIN @Demands fixture ON fixture.Uid = demand.Uid;

    INSERT INTO dbo.IndependentDemand
    (
        Uid, Version, CreatedDate, CreatedBy, IsDeleted,
        ApprovalStatus, OrderStatus, ConfirmationStatus,
        SourceType, SourceUid, SourceNo, SourceLineId, SourceLineNumber, ProductId,
        SourceSku, Sku, LinkSkuSnapshot, Description, ColorName, Features,
        OriginalQty, FilledQty, ShippedQty, IsShipped, KittingRate, HopefulDate, Remark,
        IsDecomposed, DecomposedTime, IsNonStandard,
        LifecycleStatus, DecompositionStatus, DecompositionRevision
    )
    SELECT fixture.Uid, 1, @Now, N'demand-planning-fixture', 0,
           0, 1, 1,
           N'MD', fixture.Uid, fixture.SourceNo, fixture.Uid, 1, fixture.ProductId,
           fixture.CustomerSku, fixture.Sku, fixture.LinkSku, fixture.Description, fixture.ColorName, fixture.Features,
           fixture.OriginalQty, 0, 0, 0, 0, DATEADD(day, 30, CONVERT(date, @Now)), fixture.Remark,
           0, NULL, fixture.IsNonStandard,
           0, 0, 0
    FROM @Demands fixture
    WHERE NOT EXISTS (SELECT 1 FROM dbo.IndependentDemand demand WHERE demand.Uid = fixture.Uid);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT SourceType, SourceNo, Sku, IsNonStandard, LifecycleStatus, DecompositionStatus, DecompositionRevision
FROM dbo.IndependentDemand
WHERE Uid IN (@StandardDemand, @NonStandardDemand)
ORDER BY SourceNo;
