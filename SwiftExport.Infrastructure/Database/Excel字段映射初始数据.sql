-- 用于已建有 dbo.ExcelFieldMapping 的数据库；先执行结构部署，再单独执行本脚本。
-- DetailDto 是查询快照，种子只覆盖当前 ExcelFieldCatalog 允许导入的写入字段。
-- 空表头不是有效别名：种子行保持停用，配置真实表头后再通过维护 API 启用。
-- SQLCMD 的默认会话选项可能不满足筛选索引的创建条件。
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.ExcelFieldMapping', N'U') IS NULL
    THROW 53001, N'缺少 dbo.ExcelFieldMapping，请先部署字段映射表结构。', 1;

BEGIN TRANSACTION;

-- 原索引把同组所有空表头视为一个别名；改为只约束非空的有效别名。
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.ExcelFieldMapping')
             AND name = N'UX_ExcelFieldMapping_Group_Header')
    DROP INDEX [UX_ExcelFieldMapping_Group_Header] ON [dbo].[ExcelFieldMapping];

CREATE UNIQUE INDEX [UX_ExcelFieldMapping_Group_Header]
    ON [dbo].[ExcelFieldMapping] ([GroupCode], [NormalizedHeader])
    WHERE [IsDeleted] = 0 AND [NormalizedHeader] <> N'';

DECLARE @Fields TABLE
(
    GroupCode nvarchar(40) NOT NULL,
    TargetFieldKey nvarchar(100) NOT NULL,
    PRIMARY KEY (GroupCode, TargetFieldKey)
);

-- 采购：依据独立创建命令与既有草稿更新命令的受控输入字段。
INSERT INTO @Fields (GroupCode, TargetFieldKey) VALUES
    (N'PurchaseOrder', N'Header.OrderDate'),
    (N'PurchaseOrder', N'Header.Remark'),
    (N'PurchaseOrder', N'Header.PackagingRequirements'),
    (N'PurchaseOrder', N'Header.DiscountRemark'),
    (N'PurchaseOrder', N'Line.Quantity'),
    (N'PurchaseOrder', N'Line.SupplierSku'),
    (N'PurchaseOrder', N'Line.DescriptionZh'),
    (N'PurchaseOrder', N'Line.UnitPrice'),
    (N'PurchaseOrder', N'Line.TaxPrice'),
    (N'PurchaseOrder', N'Line.PlannedDeliveryDate'),
    (N'PurchaseOrder', N'Line.SubNote');

-- 销售：依据详情保存使用的 SalesOrderEditHeaderDto / SalesOrderEditLineDto。
INSERT INTO @Fields (GroupCode, TargetFieldKey) VALUES
    (N'SalesOrder', N'Header.InvoiceNo'),
    (N'SalesOrder', N'Header.CustomerPoNo'),
    (N'SalesOrder', N'Header.ShippedBy'),
    (N'SalesOrder', N'Header.Currency'),
    (N'SalesOrder', N'Header.ExchangeRate'),
    (N'SalesOrder', N'Header.ShipToName'),
    (N'SalesOrder', N'Header.ShipToAddress'),
    (N'SalesOrder', N'Header.ShipToPhone'),
    (N'SalesOrder', N'Header.Incoterm'),
    (N'SalesOrder', N'Header.IncotermLocation'),
    (N'SalesOrder', N'Header.Remark'),
    (N'SalesOrder', N'Header.OrderDate'),
    (N'SalesOrder', N'Header.DeliveryDate'),
    (N'SalesOrder', N'Header.Etd'),
    (N'SalesOrder', N'Header.Eta'),
    (N'SalesOrder', N'Line.CustomerSku'),
    (N'SalesOrder', N'Line.DescriptionEn'),
    (N'SalesOrder', N'Line.ColorNameEn'),
    (N'SalesOrder', N'Line.FeaturesEn'),
    (N'SalesOrder', N'Line.Qty'),
    (N'SalesOrder', N'Line.CnyPrice'),
    (N'SalesOrder', N'Line.UsdPrice'),
    (N'SalesOrder', N'Line.PcsOfCarton'),
    (N'SalesOrder', N'Line.NwOfCarton'),
    (N'SalesOrder', N'Line.GwOfCarton'),
    (N'SalesOrder', N'Line.CbmOfCarton'),
    (N'SalesOrder', N'Line.ExpectedDeliveryDate'),
    (N'SalesOrder', N'Line.SubNote');

-- 已有实际别名或待配置行的目标不重复插入；多个真实别名仍可指向同一目标。
INSERT INTO dbo.ExcelFieldMapping
    (Uid, Version, IsDeleted, CreatedDate, CreatedBy,
     GroupCode, ExcelHeader, NormalizedHeader, TargetFieldKey, IsEnabled, Remark)
SELECT NEWID(), 1, 0, SYSDATETIME(), N'字段映射初始化',
       f.GroupCode, N'', N'', f.TargetFieldKey, 0, N'待配置 Excel 表头'
FROM @Fields AS f
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.ExcelFieldMapping AS existing WITH (UPDLOCK, HOLDLOCK)
    WHERE existing.IsDeleted = 0
      AND existing.GroupCode = f.GroupCode
      AND existing.TargetFieldKey = f.TargetFieldKey
);

COMMIT TRANSACTION;

-- 本脚本不会把未配置行暴露给普通读取 API；维护页面需使用 includeDisabled=true。
SELECT GroupCode, COUNT(*) AS UnconfiguredCount
FROM dbo.ExcelFieldMapping
WHERE IsDeleted = 0 AND IsEnabled = 0 AND NormalizedHeader = N''
  AND GroupCode IN (N'PurchaseOrder', N'SalesOrder')
GROUP BY GroupCode;
