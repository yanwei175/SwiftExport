/*
  库存不可变流水升级只读预检。仅输出阻断项，不修改任何数据。
  先在隔离库执行；存在结果时必须人工清理，不猜测合并或回填。
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.WarehouseSite',N'U') IS NULL
   OR OBJECT_ID(N'dbo.InventoryBalance',N'U') IS NULL
   OR OBJECT_ID(N'dbo.InventoryLedgerEntry',N'U') IS NULL
    THROW 53100,N'库存预检阻断：目标库存表尚未完整建立。',1;

SELECT N'余额唯一键重复' AS IssueType,WarehouseSiteId,ProductId,COUNT_BIG(*) AS [RowCount]
FROM dbo.InventoryBalance
GROUP BY WarehouseSiteId,ProductId HAVING COUNT_BIG(*)>1;

SELECT N'流水幂等键重复' AS IssueType,CommandItemId,COUNT_BIG(*) AS [RowCount]
FROM dbo.InventoryLedgerEntry
GROUP BY CommandItemId HAVING COUNT_BIG(*)>1;

SELECT N'流水序号重复' AS IssueType,Sequence,COUNT_BIG(*) AS [RowCount]
FROM dbo.InventoryLedgerEntry
GROUP BY Sequence HAVING COUNT_BIG(*)>1;

SELECT N'流水已被软删除' AS IssueType,Uid,CommandItemId
FROM dbo.InventoryLedgerEntry WHERE IsDeleted<>0;

SELECT N'余额投影已被软删除' AS IssueType,Uid,WarehouseSiteId,ProductId,OnHandQty
FROM dbo.InventoryBalance WHERE IsDeleted<>0;

SELECT N'反向关系不完整' AS IssueType,Uid,TransactionType,ReversalOfLedgerId
FROM dbo.InventoryLedgerEntry
WHERE (TransactionType=900 AND ReversalOfLedgerId IS NULL)
   OR (TransactionType<>900 AND ReversalOfLedgerId IS NOT NULL);

SELECT N'同一原流水存在多个反向' AS IssueType,ReversalOfLedgerId,COUNT_BIG(*) AS [RowCount]
FROM dbo.InventoryLedgerEntry WHERE ReversalOfLedgerId IS NOT NULL
GROUP BY ReversalOfLedgerId HAVING COUNT_BIG(*)>1;

SELECT N'流水引用不存在的原流水' AS IssueType,l.Uid,l.ReversalOfLedgerId
FROM dbo.InventoryLedgerEntry l
LEFT JOIN dbo.InventoryLedgerEntry original ON original.Uid=l.ReversalOfLedgerId
WHERE l.ReversalOfLedgerId IS NOT NULL AND original.Uid IS NULL;

SELECT N'库存身份引用失效' AS IssueType,x.EntityType,x.Uid,x.WarehouseSiteId,x.ProductId
FROM (
    SELECT N'Balance' AS EntityType,b.Uid,b.WarehouseSiteId,b.ProductId FROM dbo.InventoryBalance b
    UNION ALL
    SELECT N'Ledger',l.Uid,l.WarehouseSiteId,l.ProductId FROM dbo.InventoryLedgerEntry l
) x
LEFT JOIN dbo.WarehouseSite w ON w.Uid=x.WarehouseSiteId
LEFT JOIN dbo.Product p ON p.Uid=x.ProductId
WHERE w.Uid IS NULL OR p.Uid IS NULL;

SELECT N'存在流水但缺少余额投影' AS IssueType,l.WarehouseSiteId,l.ProductId,COUNT_BIG(*) AS LedgerCount
FROM dbo.InventoryLedgerEntry l
LEFT JOIN dbo.InventoryBalance b ON b.WarehouseSiteId=l.WarehouseSiteId AND b.ProductId=l.ProductId
WHERE b.Uid IS NULL
GROUP BY l.WarehouseSiteId,l.ProductId;

WITH LedgerRunning AS
(
    SELECT Uid,WarehouseSiteId,ProductId,Sequence,BalanceAfter,
           SUM(QuantityDelta) OVER(PARTITION BY WarehouseSiteId,ProductId ORDER BY Sequence,Uid ROWS UNBOUNDED PRECEDING) AS ExpectedBalanceAfter
    FROM dbo.InventoryLedgerEntry
)
SELECT N'逐流水余额不连续' AS IssueType,Uid,WarehouseSiteId,ProductId,Sequence,BalanceAfter,ExpectedBalanceAfter
FROM LedgerRunning WHERE BalanceAfter<>ExpectedBalanceAfter;

SELECT N'余额与流水净额不一致' AS IssueType,b.Uid,b.WarehouseSiteId,b.ProductId,b.OnHandQty,
       COALESCE(SUM(l.QuantityDelta),0) AS LedgerNetQty,b.LastLedgerSequence,COALESCE(MAX(l.Sequence),0) AS LedgerLastSequence
FROM dbo.InventoryBalance b
LEFT JOIN dbo.InventoryLedgerEntry l ON l.WarehouseSiteId=b.WarehouseSiteId AND l.ProductId=b.ProductId
GROUP BY b.Uid,b.WarehouseSiteId,b.ProductId,b.OnHandQty,b.LastLedgerSequence
HAVING b.OnHandQty<>COALESCE(SUM(l.QuantityDelta),0) OR b.LastLedgerSequence<>COALESCE(MAX(l.Sequence),0);
