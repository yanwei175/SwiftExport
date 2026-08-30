/*
  销售订单工作区数据库迁移与测试数据重建

  警告：本脚本会物理删除 dv 数据库中的全部销售订单头、销售订单行，
  以及 SourceType 为 SO / SalesOrder / 销售订单 的毛需求，然后重建三张演示单。

  首次成功执行：2026-07-16 12:01 +08:00
  最终测试数据恢复：2026-07-16 12:13 +08:00
  执行结果：3 个订单头、6 个订单行、3 条活动 SO 毛需求。

  保护措施：
  - 仅允许在数据库 dv 执行。
  - 如发现净需求、采购、收货或出货引用，立即终止。
  - 清库、DDL、约束、种子数据和最终断言全部位于同一事务。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    IF DB_NAME() <> N'dv'
        THROW 51000, N'本脚本只允许在 dv 开发测试数据库执行。', 1;

    BEGIN TRANSACTION;

    /* 一、执行前保护检查 */
    IF EXISTS (
        SELECT 1
        FROM dbo.DependentDemand dd
        INNER JOIN dbo.IndependentDemand d ON d.Uid = dd.IndependentDemandId
        WHERE d.SourceType IN (N'SO', N'SalesOrder', N'销售订单')
          AND dd.IsDeleted = 0
    )
        THROW 51001, N'销售毛需求已存在净需求，禁止重置销售测试数据。', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.PurchaseOrderLine pol
        INNER JOIN dbo.DependentDemand dd ON dd.Uid = pol.DemandId
        INNER JOIN dbo.IndependentDemand d ON d.Uid = dd.IndependentDemandId
        WHERE d.SourceType IN (N'SO', N'SalesOrder', N'销售订单')
          AND pol.IsDeleted = 0
    )
        THROW 51002, N'销售毛需求已存在采购订单引用，禁止重置销售测试数据。', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.PackingListLine pl
        INNER JOIN dbo.SalesOrderLine line ON line.Uid = pl.SoLineId
        WHERE pl.IsDeleted = 0
    )
        THROW 51003, N'销售订单行已存在出货引用，禁止重置销售测试数据。', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.PurchaseReceiveLine receiveLine
        INNER JOIN dbo.SalesOrderLine line ON line.Uid = receiveLine.SoLineId
        WHERE receiveLine.IsDeleted = 0
    )
        THROW 51004, N'销售订单行已存在收货引用，禁止重置销售测试数据。', 1;

    /* 二、物理清理旧销售测试链路 */
    DELETE FROM dbo.IndependentDemand
    WHERE SourceType IN (N'SO', N'SalesOrder', N'销售订单');

    DELETE FROM dbo.SalesOrderLine;
    DELETE FROM dbo.SalesOrderHeader;

    /* 三、销售订单头结构升级 */
    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'DocumentStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.SalesOrderHeader
        ADD DocumentStatus int NOT NULL
            CONSTRAINT DF_SalesOrderHeader_DocumentStatus DEFAULT (0);
    END;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'CloseReason') IS NULL
        ALTER TABLE dbo.SalesOrderHeader ADD CloseReason nvarchar(1000) NULL;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'ClosedDate') IS NULL
        ALTER TABLE dbo.SalesOrderHeader ADD ClosedDate datetime2 NULL;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'ClosedBy') IS NULL
        ALTER TABLE dbo.SalesOrderHeader ADD ClosedBy nvarchar(100) NULL;

    ALTER TABLE dbo.SalesOrderHeader
    ALTER COLUMN ConfirmationStatus smallint NOT NULL;

    DECLARE @DropLegacyDefaults nvarchar(max) = N'';
    SELECT @DropLegacyDefaults = @DropLegacyDefaults
        + N'ALTER TABLE dbo.SalesOrderHeader DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SalesOrderHeader')
      AND c.name IN (N'OrderStatus', N'ApprovalStatus', N'VersionNo', N'IsLocked');

    IF @DropLegacyDefaults <> N''
        EXEC sys.sp_executesql @DropLegacyDefaults;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'OrderStatus') IS NOT NULL
        ALTER TABLE dbo.SalesOrderHeader DROP COLUMN OrderStatus;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'ApprovalStatus') IS NOT NULL
        ALTER TABLE dbo.SalesOrderHeader DROP COLUMN ApprovalStatus;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'VersionNo') IS NOT NULL
        ALTER TABLE dbo.SalesOrderHeader DROP COLUMN VersionNo;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'IsLocked') IS NOT NULL
        ALTER TABLE dbo.SalesOrderHeader DROP COLUMN IsLocked;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

BEGIN TRY

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.SalesOrderHeader')
          AND name = N'CK_SalesOrderHeader_DocumentStatus'
    )
        ALTER TABLE dbo.SalesOrderHeader DROP CONSTRAINT CK_SalesOrderHeader_DocumentStatus;

    ALTER TABLE dbo.SalesOrderHeader WITH CHECK
    ADD CONSTRAINT CK_SalesOrderHeader_DocumentStatus
    CHECK (DocumentStatus IN (0, 9));

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.SalesOrderHeader')
          AND name = N'CK_SalesOrderHeader_ConfirmationStatus'
    )
        ALTER TABLE dbo.SalesOrderHeader DROP CONSTRAINT CK_SalesOrderHeader_ConfirmationStatus;

    ALTER TABLE dbo.SalesOrderHeader WITH CHECK
    ADD CONSTRAINT CK_SalesOrderHeader_ConfirmationStatus
    CHECK (ConfirmationStatus IN (0, 1));

    /* 四、销售订单行结构升级 */
    IF COL_LENGTH(N'dbo.SalesOrderLine', N'ProductId') IS NULL
        ALTER TABLE dbo.SalesOrderLine ADD ProductId uniqueidentifier NULL;

    IF COL_LENGTH(N'dbo.SalesOrderLine', N'CustomerProductId') IS NULL
        ALTER TABLE dbo.SalesOrderLine ADD CustomerProductId uniqueidentifier NULL;

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'CK_SalesOrderLine_ProductRelation'
    )
        ALTER TABLE dbo.SalesOrderLine DROP CONSTRAINT CK_SalesOrderLine_ProductRelation;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Uid'
    )
        DROP INDEX UX_SalesOrderLine_Uid ON dbo.SalesOrderLine;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Active_Order_LineNumber'
    )
        DROP INDEX UX_SalesOrderLine_Active_Order_LineNumber ON dbo.SalesOrderLine;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Active_NonStandard_Sku'
    )
        DROP INDEX UX_SalesOrderLine_Active_NonStandard_Sku ON dbo.SalesOrderLine;

    ALTER TABLE dbo.SalesOrderLine ALTER COLUMN Uid uniqueidentifier NOT NULL;
    ALTER TABLE dbo.SalesOrderLine ALTER COLUMN SalesOrderId uniqueidentifier NOT NULL;
    ALTER TABLE dbo.SalesOrderLine ALTER COLUMN IsNonStandard bit NOT NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND c.name = N'IsNonStandard'
    )
        ALTER TABLE dbo.SalesOrderLine
        ADD CONSTRAINT DF_SalesOrderLine_IsNonStandard DEFAULT (0) FOR IsNonStandard;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

BEGIN TRY

    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'CK_SalesOrderLine_ProductRelation'
    )
        ALTER TABLE dbo.SalesOrderLine DROP CONSTRAINT CK_SalesOrderLine_ProductRelation;

    ALTER TABLE dbo.SalesOrderLine WITH CHECK
    ADD CONSTRAINT CK_SalesOrderLine_ProductRelation
    CHECK (
        IsDeleted = 1
        OR (IsNonStandard = 0 AND ProductId IS NOT NULL)
        OR (IsNonStandard = 1 AND ProductId IS NULL AND CustomerProductId IS NULL)
    );

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Uid'
    )
        DROP INDEX UX_SalesOrderLine_Uid ON dbo.SalesOrderLine;

    CREATE UNIQUE INDEX UX_SalesOrderLine_Uid
    ON dbo.SalesOrderLine(Uid);

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Active_Order_LineNumber'
    )
        DROP INDEX UX_SalesOrderLine_Active_Order_LineNumber ON dbo.SalesOrderLine;

    CREATE UNIQUE INDEX UX_SalesOrderLine_Active_Order_LineNumber
    ON dbo.SalesOrderLine(SalesOrderId, LineNumber)
    WHERE IsDeleted = 0;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SalesOrderLine')
          AND name = N'UX_SalesOrderLine_Active_NonStandard_Sku'
    )
        DROP INDEX UX_SalesOrderLine_Active_NonStandard_Sku ON dbo.SalesOrderLine;

    CREATE UNIQUE INDEX UX_SalesOrderLine_Active_NonStandard_Sku
    ON dbo.SalesOrderLine(Sku)
    WHERE IsNonStandard = 1 AND IsDeleted = 0;

    /* 五、销售毛需求约束 */
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.IndependentDemand')
          AND name = N'CK_IndependentDemand_SalesOrderSource'
    )
        ALTER TABLE dbo.IndependentDemand DROP CONSTRAINT CK_IndependentDemand_SalesOrderSource;

    ALTER TABLE dbo.IndependentDemand WITH CHECK
    ADD CONSTRAINT CK_IndependentDemand_SalesOrderSource
    CHECK (
        IsDeleted = 1
        OR SourceType <> N'SO'
        OR (SourceUid IS NOT NULL AND SourceLineId IS NOT NULL AND IsNonStandard IS NOT NULL)
    );

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IndependentDemand')
          AND name = N'UX_IndependentDemand_Active_SourceLine'
    )
        DROP INDEX UX_IndependentDemand_Active_SourceLine ON dbo.IndependentDemand;

    CREATE UNIQUE INDEX UX_IndependentDemand_Active_SourceLine
    ON dbo.IndependentDemand(SourceType, SourceLineId)
    WHERE IsDeleted = 0 AND SourceType = N'SO';

    /* 六、选择演示客户和前三个有效客户产品 */
    DECLARE @CustomerId uniqueidentifier;
    DECLARE @CustomerCode nvarchar(510);
    DECLARE @CustomerName nvarchar(510);
    DECLARE @Currency nvarchar(20);
    DECLARE @PaymentMethod nvarchar(100);
    DECLARE @ShippedBy nvarchar(510);
    DECLARE @Incoterm nvarchar(40);
    DECLARE @IncotermLocation nvarchar(100);
    DECLARE @IncotermSortRemark nvarchar(max);
    DECLARE @DepositRate decimal(5, 2);
    DECLARE @LeadTimeDays int;
    DECLARE @ShipToAddress nvarchar(510);
    DECLARE @ShipToPhone nvarchar(510);

    SELECT
        @CustomerId = Uid,
        @CustomerCode = CustomerCode,
        @CustomerName = ISNULL(CustomerName, CustomerCode),
        @Currency = CASE WHEN NULLIF(LTRIM(RTRIM(Currency)), N'') IS NULL THEN N'USD' ELSE UPPER(LTRIM(RTRIM(Currency))) END,
        @PaymentMethod = ISNULL(PaymentMethod, N''),
        @ShippedBy = ISNULL(ShippedBy, N''),
        @Incoterm = ISNULL(Incoterm, N''),
        @IncotermLocation = ISNULL(IncotermLocation, N''),
        @IncotermSortRemark = ISNULL(IncotermSortRemark, N''),
        @DepositRate = ISNULL(DepositRate, 0),
        @LeadTimeDays = ISNULL(LeadTimeDays, 45),
        @ShipToAddress = ISNULL(Address, N''),
        @ShipToPhone = ISNULL(PhoneNumber, N'')
    FROM dbo.Customer
    WHERE CustomerCode = N'1007-AT' AND IsDeleted = 0;

    IF @CustomerId IS NULL
        THROW 51005, N'未找到活动客户 1007-AT，无法创建销售演示单。', 1;

    DECLARE @Products TABLE (
        RowNo int NOT NULL,
        CustomerProductId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        CustomerSku nvarchar(510) NULL,
        Sku nvarchar(510) NOT NULL,
        LinkSku nvarchar(510) NULL,
        DescriptionEn nvarchar(510) NULL,
        ColorNameEn nvarchar(100) NULL,
        FeaturesEn nvarchar(max) NULL,
        MainImage nvarchar(510) NULL
    );

    INSERT INTO @Products (
        RowNo, CustomerProductId, ProductId, CustomerSku, Sku, LinkSku,
        DescriptionEn, ColorNameEn, FeaturesEn, MainImage
    )
    SELECT
        ROW_NUMBER() OVER (ORDER BY p.Sku, cp.Uid),
        cp.Uid,
        p.Uid,
        cp.CustomerSku,
        p.Sku,
        COALESCE(cp.LinkSku, p.LinkSku),
        COALESCE(cp.DescriptionEn, p.DescriptionEn),
        pc.ColorNameEn,
        COALESCE(cp.FeaturesEn, p.FeaturesEn),
        p.MainImage
    FROM dbo.CustomerProduct cp
    INNER JOIN dbo.Product p
        ON p.Uid = cp.SkuId AND p.IsDeleted = 0
    LEFT JOIN dbo.ProductColor pc
        ON pc.Uid = p.ColorId AND pc.IsDeleted = 0
    WHERE cp.CustomerId = @CustomerId
      AND cp.IsDeleted = 0
      AND cp.IsActive = 1
      AND cp.SkuId IS NOT NULL;

    IF (SELECT COUNT(*) FROM @Products) < 3
        THROW 51006, N'客户 1007-AT 不足三个有效客户产品，无法创建销售演示单。', 1;

    DECLARE
        @Cp1 uniqueidentifier, @Product1 uniqueidentifier, @CustomerSku1 nvarchar(510),
        @Sku1 nvarchar(510), @LinkSku1 nvarchar(510), @Description1 nvarchar(510),
        @Color1 nvarchar(100), @Features1 nvarchar(max), @Image1 nvarchar(510),
        @Cp2 uniqueidentifier, @Product2 uniqueidentifier, @CustomerSku2 nvarchar(510),
        @Sku2 nvarchar(510), @LinkSku2 nvarchar(510), @Description2 nvarchar(510),
        @Color2 nvarchar(100), @Features2 nvarchar(max), @Image2 nvarchar(510),
        @Cp3 uniqueidentifier, @Product3 uniqueidentifier, @CustomerSku3 nvarchar(510),
        @Sku3 nvarchar(510), @LinkSku3 nvarchar(510), @Description3 nvarchar(510),
        @Color3 nvarchar(100), @Features3 nvarchar(max), @Image3 nvarchar(510);

    SELECT @Cp1 = CustomerProductId, @Product1 = ProductId, @CustomerSku1 = CustomerSku,
           @Sku1 = Sku, @LinkSku1 = LinkSku, @Description1 = DescriptionEn,
           @Color1 = ColorNameEn, @Features1 = FeaturesEn, @Image1 = MainImage
    FROM @Products WHERE RowNo = 1;

    SELECT @Cp2 = CustomerProductId, @Product2 = ProductId, @CustomerSku2 = CustomerSku,
           @Sku2 = Sku, @LinkSku2 = LinkSku, @Description2 = DescriptionEn,
           @Color2 = ColorNameEn, @Features2 = FeaturesEn, @Image2 = MainImage
    FROM @Products WHERE RowNo = 2;

    SELECT @Cp3 = CustomerProductId, @Product3 = ProductId, @CustomerSku3 = CustomerSku,
           @Sku3 = Sku, @LinkSku3 = LinkSku, @Description3 = DescriptionEn,
           @Color3 = ColorNameEn, @Features3 = FeaturesEn, @Image3 = MainImage
    FROM @Products WHERE RowNo = 3;

    /* 七、创建三张演示订单 */
    DECLARE @Now datetime2 = SYSDATETIME();
    DECLARE @CreatedBy nvarchar(100) = N'codex-db-migration';
    DECLARE @DraftOrderId uniqueidentifier = NEWID();
    DECLARE @ConfirmedOrderId uniqueidentifier = NEWID();
    DECLARE @ClosedOrderId uniqueidentifier = NEWID();
    DECLARE @DraftOrderNo nvarchar(510) = N'SO-DEMO-202607-001';
    DECLARE @ConfirmedOrderNo nvarchar(510) = N'SO-DEMO-202607-002';
    DECLARE @ClosedOrderNo nvarchar(510) = N'SO-DEMO-202607-003';

    INSERT INTO dbo.SalesOrderHeader (
        Uid, Version, CustomerId, OrderNo, CustomerCode, CustomerPoNo,
        ShippedBy, Currency, ExchangeRate, TotalAmountOriginalCurrency,
        TotalAmountLocalCurrency, InvoiceNo, ShipToName, ShipToAddress,
        ShipToPhone, Incoterm, IncotermLocation, DepositRate, LeadTimeDays,
        PaymentMethod, SoFolder, Remark, IsDeleted, CreatedBy, UpdatedBy,
        OrderDate, DeliveryDate, ReplyDate, ETA, ETD, CreatedDate, UpdatedDate,
        ConfirmationStatus, IncotermSortRemark, DocumentStatus,
        CloseReason, ClosedDate, ClosedBy
    )
    VALUES
    (
        @DraftOrderId, 1, @CustomerId, @DraftOrderNo, @CustomerCode, N'PO-DEMO-DRAFT',
        @ShippedBy, @Currency, 7.200000, 200.00, 1400.00, N'INV-DEMO-DRAFT',
        @CustomerName, @ShipToAddress, @ShipToPhone, @Incoterm, @IncotermLocation,
        @DepositRate, @LeadTimeDays, @PaymentMethod, NULL, N'待确认演示订单',
        0, @CreatedBy, NULL, '2026-07-10', '2026-08-20', NULL, NULL, NULL,
        @Now, NULL, 0, @IncotermSortRemark, 0, NULL, NULL, NULL
    ),
    (
        @ConfirmedOrderId, 1, @CustomerId, @ConfirmedOrderNo, @CustomerCode, N'PO-DEMO-CONFIRMED',
        @ShippedBy, @Currency, 7.200000, 108.00, 756.00, N'INV-DEMO-CONFIRMED',
        @CustomerName, @ShipToAddress, @ShipToPhone, @Incoterm, @IncotermLocation,
        @DepositRate, @LeadTimeDays, @PaymentMethod, NULL, N'已确认演示订单',
        0, @CreatedBy, NULL, '2026-07-11', '2026-08-21', '2026-07-12', NULL, NULL,
        @Now, NULL, 1, @IncotermSortRemark, 0, NULL, NULL, NULL
    ),
    (
        @ClosedOrderId, 1, @CustomerId, @ClosedOrderNo, @CustomerCode, N'PO-DEMO-CLOSED',
        @ShippedBy, @Currency, 7.200000, 42.00, 294.00, N'INV-DEMO-CLOSED',
        @CustomerName, @ShipToAddress, @ShipToPhone, @Incoterm, @IncotermLocation,
        @DepositRate, @LeadTimeDays, @PaymentMethod, NULL, N'已关闭演示订单',
        0, @CreatedBy, @CreatedBy, '2026-07-12', '2026-08-22', '2026-07-13', NULL, NULL,
        @Now, @Now, 1, @IncotermSortRemark, 9, N'销售工作区数据库迁移演示关闭',
        @Now, @CreatedBy
    );

    DECLARE @DraftLine1 uniqueidentifier = NEWID();
    DECLARE @DraftLine2 uniqueidentifier = NEWID();
    DECLARE @DraftLine3 uniqueidentifier = NEWID();
    DECLARE @ConfirmedLine1 uniqueidentifier = NEWID();
    DECLARE @ConfirmedLine2 uniqueidentifier = NEWID();
    DECLARE @ClosedLine1 uniqueidentifier = NEWID();
    DECLARE @NonStandardSku nvarchar(510)
        = N'非标件-' + LEFT(UPPER(REPLACE(CONVERT(nvarchar(36), @DraftLine3), N'-', N'')), 12);

    INSERT INTO dbo.SalesOrderLine (
        Uid, Version, SalesOrderId, LineNumber, OrderNo, Sku, LinkSku,
        CustomerSku, DescriptionEn, ColorNameEn, FeaturesEn, CnyPrice,
        UsdPrice, Qty, CnyAmount, UsdAmount, PcsOfCarton, NwOfCarton,
        GwOfCarton, CbmOfCarton, Ctns, NwSubTotal, GwSubTotal, CbmSubTotal,
        SubNote, IsDeleted, CreatedDate, LastShippedDate, ExpectedDeliveryDate,
        MainImage, CreatedBy, UpdatedDate, UpdatedBy, IsNonStandard,
        ProductId, CustomerProductId
    )
    VALUES
    (
        @DraftLine1, 1, @DraftOrderId, 1, @DraftOrderNo, @Sku1, @LinkSku1,
        @CustomerSku1, @Description1, @Color1, @Features1, 70.00, 10.00,
        10.00, 700.00, 100.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'待确认标准行 1', 0, @Now, NULL, '2026-08-20', @Image1, @CreatedBy,
        NULL, NULL, 0, @Product1, @Cp1
    ),
    (
        @DraftLine2, 1, @DraftOrderId, 2, @DraftOrderNo, @Sku2, @LinkSku2,
        @CustomerSku2, @Description2, @Color2, @Features2, 84.00, 12.00,
        5.00, 420.00, 60.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'待确认标准行 2', 0, @Now, NULL, '2026-08-20', @Image2, @CreatedBy,
        NULL, NULL, 0, @Product2, @Cp2
    ),
    (
        @DraftLine3, 1, @DraftOrderId, 3, @DraftOrderNo, @NonStandardSku, NULL,
        N'CUSTOM-NONSTD-001', N'Custom non-standard demo item', N'Custom',
        N'Non-standard sales workspace demo', 140.00, 20.00, 2.00, 280.00,
        40.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'待确认非标行', 0, @Now, NULL, '2026-08-25', NULL, @CreatedBy,
        NULL, NULL, 1, NULL, NULL
    ),
    (
        @ConfirmedLine1, 1, @ConfirmedOrderId, 1, @ConfirmedOrderNo, @Sku1, @LinkSku1,
        @CustomerSku1, @Description1, @Color1, @Features1, 70.00, 10.00,
        6.00, 420.00, 60.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'已确认标准行 1', 0, @Now, NULL, '2026-08-21', @Image1, @CreatedBy,
        NULL, NULL, 0, @Product1, @Cp1
    ),
    (
        @ConfirmedLine2, 1, @ConfirmedOrderId, 2, @ConfirmedOrderNo, @Sku2, @LinkSku2,
        @CustomerSku2, @Description2, @Color2, @Features2, 84.00, 12.00,
        4.00, 336.00, 48.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'已确认标准行 2', 0, @Now, NULL, '2026-08-21', @Image2, @CreatedBy,
        NULL, NULL, 0, @Product2, @Cp2
    ),
    (
        @ClosedLine1, 1, @ClosedOrderId, 1, @ClosedOrderNo, @Sku3, @LinkSku3,
        @CustomerSku3, @Description3, @Color3, @Features3, 98.00, 14.00,
        3.00, 294.00, 42.00, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'已关闭标准行', 0, @Now, NULL, '2026-08-22', @Image3, @CreatedBy,
        @Now, @CreatedBy, 0, @Product3, @Cp3
    );

    /* 八、为已确认和已关闭订单建立活动毛需求 */
    INSERT INTO dbo.IndependentDemand (
        Uid, Version, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, IsDeleted,
        ApprovalStatus, OrderStatus, ConfirmationStatus, SourceType, SourceUid,
        SourceNo, SourceLineId, SourceSku, Sku, Description, ColorName, Features,
        OriginalQty, FilledQty, ShippedQty, IsShipped, LastShippedDate,
        KittingRate, HopefulDate, Remark, IsDecomposed, DecomposedTime,
        IsNonStandard
    )
    VALUES
    (
        NEWID(), 1, @Now, @CreatedBy, NULL, NULL, 0, 0, 1, 1, N'SO',
        @ConfirmedOrderId, @ConfirmedOrderNo, @ConfirmedLine1, @CustomerSku1,
        @Sku1, @Description1, @Color1, @Features1, 6.0000, 0, 0, 0, NULL,
        0, '2026-08-21', N'已确认演示需求 1', 0, NULL, 0
    ),
    (
        NEWID(), 1, @Now, @CreatedBy, NULL, NULL, 0, 0, 1, 1, N'SO',
        @ConfirmedOrderId, @ConfirmedOrderNo, @ConfirmedLine2, @CustomerSku2,
        @Sku2, @Description2, @Color2, @Features2, 4.0000, 0, 0, 0, NULL,
        0, '2026-08-21', N'已确认演示需求 2', 0, NULL, 0
    ),
    (
        NEWID(), 1, @Now, @CreatedBy, @Now, @CreatedBy, 0, 0, 1, 1, N'SO',
        @ClosedOrderId, @ClosedOrderNo, @ClosedLine1, @CustomerSku3,
        @Sku3, @Description3, @Color3, @Features3, 3.0000, 0, 0, 0, NULL,
        0, '2026-08-22', N'已关闭演示需求', 0, NULL, 0
    );

    /* 九、提交前断言 */
    IF (SELECT COUNT(*) FROM dbo.SalesOrderHeader) <> 3
        THROW 51007, N'销售订单头种子数量不是 3，迁移回滚。', 1;

    IF (SELECT COUNT(*) FROM dbo.SalesOrderLine) <> 6
        THROW 51008, N'销售订单行种子数量不是 6，迁移回滚。', 1;

    IF (
        SELECT COUNT(*)
        FROM dbo.IndependentDemand
        WHERE SourceType = N'SO' AND IsDeleted = 0
    ) <> 3
        THROW 51009, N'活动销售毛需求种子数量不是 3，迁移回滚。', 1;

    IF EXISTS (
        SELECT SourceLineId
        FROM dbo.IndependentDemand
        WHERE SourceType = N'SO' AND IsDeleted = 0
        GROUP BY SourceLineId
        HAVING COUNT(*) > 1
    )
        THROW 51010, N'仍存在重复活动销售毛需求，迁移回滚。', 1;

    IF COL_LENGTH(N'dbo.SalesOrderHeader', N'OrderStatus') IS NOT NULL
       OR COL_LENGTH(N'dbo.SalesOrderHeader', N'ApprovalStatus') IS NOT NULL
       OR COL_LENGTH(N'dbo.SalesOrderHeader', N'VersionNo') IS NOT NULL
       OR COL_LENGTH(N'dbo.SalesOrderHeader', N'IsLocked') IS NOT NULL
        THROW 51011, N'销售订单头旧状态列未完全删除，迁移回滚。', 1;

    COMMIT TRANSACTION;

    SELECT
        DB_NAME() AS DatabaseName,
        (SELECT COUNT(*) FROM dbo.SalesOrderHeader) AS SalesOrderHeaderCount,
        (SELECT COUNT(*) FROM dbo.SalesOrderLine) AS SalesOrderLineCount,
        (SELECT COUNT(*) FROM dbo.IndependentDemand WHERE SourceType = N'SO' AND IsDeleted = 0) AS ActiveSalesDemandCount,
        @DraftOrderId AS DraftOrderUid,
        @ConfirmedOrderId AS ConfirmedOrderUid,
        @ClosedOrderId AS ClosedOrderUid;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
