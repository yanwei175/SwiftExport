-- PackingList 草稿接口只读预检：不修改数据或结构。
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.PackingListHead',N'U') IS NULL OR OBJECT_ID(N'dbo.PackingListLine',N'U') IS NULL
    THROW 51000,N'PackingListHead/Line 新表尚未部署。',1;
IF OBJECT_ID(N'dbo.PackingListDocument',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PackingListContributionLine',N'U') IS NOT NULL
    THROW 51001,N'旧 PackingList 表仍存在，拒绝形成双写或双读结构。',1;
DECLARE @RequiredColumns TABLE(TableName sysname NOT NULL,ColumnName sysname NOT NULL);
INSERT @RequiredColumns(TableName,ColumnName) VALUES
(N'PackingListHead',N'Uid'),(N'PackingListHead',N'Version'),(N'PackingListHead',N'IsDeleted'),
(N'PackingListHead',N'CreatedDate'),(N'PackingListHead',N'CreatedBy'),(N'PackingListHead',N'UpdatedDate'),
(N'PackingListHead',N'UpdatedBy'),(N'PackingListHead',N'PackingListNo'),(N'PackingListHead',N'CustomerId'),
(N'PackingListHead',N'CustomerNameSnapshot'),(N'PackingListHead',N'CustomerAddressSnapshot'),
(N'PackingListHead',N'AttentionSnapshot'),(N'PackingListHead',N'InvoiceDate'),(N'PackingListHead',N'ContainerNo'),
(N'PackingListHead',N'SealNo'),(N'PackingListHead',N'ShippingMark'),(N'PackingListHead',N'Note'),
(N'PackingListHead',N'Etd'),(N'PackingListHead',N'Eta'),(N'PackingListHead',N'LifecycleStatus'),
(N'PackingListHead',N'PostingStatus'),(N'PackingListHead',N'PostingDate'),(N'PackingListHead',N'PostedDate'),
(N'PackingListHead',N'PostedBy'),(N'PackingListHead',N'ReversedDate'),(N'PackingListHead',N'ReversedBy'),
(N'PackingListHead',N'ReverseReason'),(N'PackingListHead',N'CreateCommandId'),(N'PackingListHead',N'PostCommandId'),
(N'PackingListHead',N'ReverseCommandId'),
(N'PackingListLine',N'Uid'),(N'PackingListLine',N'Version'),(N'PackingListLine',N'IsDeleted'),
(N'PackingListLine',N'CreatedDate'),(N'PackingListLine',N'CreatedBy'),(N'PackingListLine',N'UpdatedDate'),
(N'PackingListLine',N'UpdatedBy'),(N'PackingListLine',N'PackingListHeadId'),(N'PackingListLine',N'LineNumber'),
(N'PackingListLine',N'GrossDemandId'),(N'PackingListLine',N'ProductId'),(N'PackingListLine',N'SkuSnapshot'),
(N'PackingListLine',N'LinkSkuSnapshot'),(N'PackingListLine',N'CustomerSkuSnapshot'),
(N'PackingListLine',N'DescriptionEnSnapshot'),(N'PackingListLine',N'ColorNameEnSnapshot'),
(N'PackingListLine',N'UnitSnapshot'),(N'PackingListLine',N'Qty'),(N'PackingListLine',N'PcsPerPackage'),
(N'PackingListLine',N'PackageCount'),(N'PackingListLine',N'NwPerPackage'),(N'PackingListLine',N'GwPerPackage'),
(N'PackingListLine',N'CbmPerPackage'),(N'PackingListLine',N'SubTotalNw'),(N'PackingListLine',N'SubTotalGw'),
(N'PackingListLine',N'SubTotalCbm'),(N'PackingListLine',N'CustomerPoNo'),(N'PackingListLine',N'InvoiceNo'),
(N'PackingListLine',N'Marks'),(N'PackingListLine',N'Note'),(N'PackingListLine',N'WarehouseSiteId'),
(N'PackingListLine',N'WarehouseCodeSnapshot'),(N'PackingListLine',N'WarehouseNameSnapshot');
IF EXISTS(
    SELECT 1 FROM @RequiredColumns r
    WHERE COL_LENGTH(QUOTENAME(N'dbo')+N'.'+QUOTENAME(r.TableName),r.ColumnName) IS NULL)
    THROW 51002,N'PackingList 新表缺少草稿接口必需列。',1;
IF EXISTS(SELECT 1 FROM dbo.PackingListLine WHERE Qty<=0)
    THROW 51003,N'存在非正交付数量，必须先核实数据后再收紧约束。',1;
IF EXISTS(SELECT CreateCommandId FROM dbo.PackingListHead GROUP BY CreateCommandId HAVING COUNT_BIG(*)>1)
    THROW 51004,N'CreateCommandId 存在重复，不能建立幂等唯一索引。',1;

SELECT N'OK' Result,N'PackingList 草稿接口增量结构可以部署。' Message;
