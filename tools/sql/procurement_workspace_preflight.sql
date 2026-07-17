SET NOCOUNT ON;

SELECT RequiredObject=v.Name, ExistsFlag=CASE WHEN OBJECT_ID(v.Name,N'U') IS NULL THEN 0 ELSE 1 END
FROM (VALUES(N'dbo.PurchaseOrderHead'),(N'dbo.PurchaseOrderLine'),(N'dbo.DemandExecutionLink'),(N'dbo.FollowUpLog'))v(Name);

SELECT TableName=t.name, ColumnName=c.name, TypeName=ty.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name IN(N'PurchaseOrderHead',N'PurchaseOrderLine',N'FollowUpLog')
ORDER BY t.name,c.column_id;

SELECT LegacyOperabilityIssue,
       COUNT_BIG(1) AS AffectedRows
FROM
(
    SELECT CASE
      WHEN SupplierId IS NULL THEN N'MISSING_SUPPLIER_UID'
      WHEN DemandSourceType IS NULL OR DemandSourceUid IS NULL OR DemandSourceNo IS NULL THEN N'MISSING_DEMAND_SOURCE'
      WHEN LifecycleStatus IS NULL THEN N'MISSING_LIFECYCLE'
      WHEN ExecutionRequestId IS NULL THEN N'MISSING_EXECUTION_REQUEST'
      ELSE N'OPERABLE_CANDIDATE' END AS LegacyOperabilityIssue
    FROM dbo.PurchaseOrderHead WHERE IsDeleted=0
)q GROUP BY LegacyOperabilityIssue ORDER BY LegacyOperabilityIssue;

SELECT DuplicateCommandId=CommandId, COUNT_BIG(1) AS DuplicateCount
FROM dbo.FollowUpLog WHERE IsDeleted=0 AND CommandId IS NOT NULL
GROUP BY CommandId HAVING COUNT_BIG(1)>1;
