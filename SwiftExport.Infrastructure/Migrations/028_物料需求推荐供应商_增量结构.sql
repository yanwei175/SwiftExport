/*
  物料需求推荐采购供应商增量结构。
  该字段只保存建议，不代表采购域最终采购方或送达方；历史数据保持 NULL，不从 Allocation 回填。
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.NetDemand', N'SuggestedPurchaseSupplierId') IS NULL
BEGIN
    ALTER TABLE dbo.NetDemand
        ADD SuggestedPurchaseSupplierId uniqueidentifier NULL;
END;

COMMIT TRANSACTION;

SELECT
    COL_LENGTH(N'dbo.NetDemand', N'SuggestedPurchaseSupplierId') AS SuggestedPurchaseSupplierColumnLength,
    (SELECT COUNT(1)
       FROM dbo.NetDemand
      WHERE SuggestedPurchaseSupplierId IS NOT NULL) AS ExplicitSuggestedSupplierCount;
