SET NOCOUNT ON;

SELECT MissingColumn=v.TableName+N'.'+v.ColumnName
FROM (VALUES
 (N'PurchaseOrderHead',N'ConfirmedDate'),(N'PurchaseOrderHead',N'ConfirmedBy'),
 (N'FollowUpLog',N'RefLineType'),(N'FollowUpLog',N'Sequence'),(N'FollowUpLog',N'ArrangementStatus'),
 (N'FollowUpLog',N'ReadyQty'),(N'FollowUpLog',N'ExpectedReadyDate'),(N'FollowUpLog',N'SupplierReportedDate'),
 (N'FollowUpLog',N'IsCorrection'),(N'FollowUpLog',N'CorrectionReason'),(N'FollowUpLog',N'CommandId')
)v(TableName,ColumnName)
WHERE COL_LENGTH(N'dbo.'+v.TableName,v.ColumnName) IS NULL;

SELECT MissingObject=N'dbo.BusinessOperationLog' WHERE OBJECT_ID(N'dbo.BusinessOperationLog',N'U') IS NULL;

SELECT BrokenProgressRow=Uid
FROM dbo.FollowUpLog
WHERE IsDeleted=0 AND RefLineType=N'PurchaseOrderLine'
  AND (RefLineId IS NULL OR Sequence IS NULL OR Sequence<=0 OR ArrangementStatus NOT IN(0,1,2,8)
       OR ReadyQty IS NULL OR ReadyQty<0 OR SupplierReportedDate IS NULL OR CommandId IS NULL);

SELECT DuplicateSequence=CONCAT(CONVERT(nvarchar(36),RefLineId),N'/',Sequence),COUNT_BIG(1) AS DuplicateCount
FROM dbo.FollowUpLog WHERE IsDeleted=0 AND RefLineType=N'PurchaseOrderLine'
GROUP BY RefLineId,Sequence HAVING COUNT_BIG(1)>1;

SELECT DuplicateBusinessCommand=CONCAT(Module,N'/',OperationType,N'/',CONVERT(nvarchar(36),CommandId)),COUNT_BIG(1) AS DuplicateCount
FROM dbo.BusinessOperationLog WHERE IsDeleted=0
GROUP BY Module,OperationType,CommandId HAVING COUNT_BIG(1)>1;
