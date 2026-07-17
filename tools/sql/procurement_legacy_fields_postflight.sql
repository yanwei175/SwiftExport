SET NOCOUNT ON;
IF EXISTS
(
    SELECT 1 FROM (VALUES
     (N'PurchaseOrderHead',N'BoNo'),(N'PurchaseOrderHead',N'CustomerPoNo'),(N'PurchaseOrderHead',N'ReplyDate'),
     (N'PurchaseOrderHead',N'InletrHoseType'),(N'PurchaseOrderHead',N'IMType'),(N'PurchaseOrderHead',N'ZheKouBeiZhu'),
     (N'PurchaseOrderLine',N'CustomerSku'),(N'PurchaseOrderLine',N'LinkSku'),(N'PurchaseOrderLine',N'SupplierSku'),
     (N'PurchaseOrderLine',N'FinishZh'),(N'PurchaseOrderLine',N'FeaturesZh'),(N'PurchaseOrderLine',N'RecQty'))v(TableName,ColumnName)
    WHERE COL_LENGTH(N'dbo.'+v.TableName,v.ColumnName) IS NULL
)
    THROW 51011,N'采购旧页面字段覆盖 postflight 失败：仍有必需列缺失。',1;

SELECT N'PROCUREMENT_LEGACY_FIELD_COVERAGE_OK' AS PostflightResult;
