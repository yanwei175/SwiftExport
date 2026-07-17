SET NOCOUNT ON;
SELECT v.TableName,v.ColumnName,
       CASE WHEN COL_LENGTH(N'dbo.'+v.TableName,v.ColumnName) IS NULL THEN N'MISSING' ELSE N'EXISTS' END AS ColumnState
FROM (VALUES
 (N'PurchaseOrderHead',N'BoNo'),(N'PurchaseOrderHead',N'SalesOrderNo'),(N'PurchaseOrderHead',N'CustomerPoNo'),
 (N'PurchaseOrderHead',N'CustomerCode'),(N'PurchaseOrderHead',N'SoPerson'),(N'PurchaseOrderHead',N'PoPerson'),
 (N'PurchaseOrderHead',N'InletrHoseType'),(N'PurchaseOrderHead',N'AeratorType'),(N'PurchaseOrderHead',N'PalletType'),
 (N'PurchaseOrderHead',N'BoxType'),(N'PurchaseOrderHead',N'LabelType'),(N'PurchaseOrderHead',N'IMType'),
 (N'PurchaseOrderHead',N'CartonType'),(N'PurchaseOrderHead',N'LogoType'),(N'PurchaseOrderHead',N'ReviewedBy'),
 (N'PurchaseOrderHead',N'PoFolder'),(N'PurchaseOrderHead',N'ZheKouBeiZhu'),(N'PurchaseOrderHead',N'ReplyDate'),
 (N'PurchaseOrderLine',N'CustomerSku'),(N'PurchaseOrderLine',N'LinkSku'),(N'PurchaseOrderLine',N'SupplierSku'),
 (N'PurchaseOrderLine',N'DescriptionZh'),(N'PurchaseOrderLine',N'FinishZh'),(N'PurchaseOrderLine',N'FeaturesZh'),
 (N'PurchaseOrderLine',N'MaterialType'),(N'PurchaseOrderLine',N'SupplyType'),(N'PurchaseOrderLine',N'RecQty'),
 (N'PurchaseOrderLine',N'LastRecDate'))v(TableName,ColumnName)
ORDER BY v.TableName,v.ColumnName;
