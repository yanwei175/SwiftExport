/* 006/007 执行后的只读断言；任一结果集返回记录均需人工审查。 */
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT N'ACTIVE_NET_WITHOUT_ONE_ALLOCATION' AS AuditSection,nd.Uid,nd.NetQty,
       COUNT(a.Uid) AS ActiveAllocationCount,COALESCE(SUM(a.PlannedQty),0) AS ActivePlannedQty
FROM dbo.DependentDemand nd
LEFT JOIN dbo.NetDemandAllocation a ON a.NetDemandId=nd.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0
GROUP BY nd.Uid,nd.NetQty
HAVING COUNT(a.Uid)<>1 OR COALESCE(SUM(a.PlannedQty),0)<>nd.NetQty;

SELECT N'INVALID_B_DELIVERY_TARGET' AS AuditSection,nd.Uid,a.Uid AS AllocationId,
       a.DeliveryTargetAllocationId,a.DeliveryTargetSupplierId,target.PurchaseSupplierId
FROM dbo.DependentDemand nd
JOIN dbo.NetDemandAllocation a ON a.NetDemandId=nd.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
LEFT JOIN dbo.NetDemandAllocation target ON target.Uid=a.DeliveryTargetAllocationId
WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0 AND nd.DeliveryMode=1
  AND (target.Uid IS NULL OR target.NetDemandId<>nd.DeliveryTargetNetDemandId
       OR ISNULL(CONVERT(nvarchar(36),a.DeliveryTargetSupplierId),N'')
          <>ISNULL(CONVERT(nvarchar(36),target.PurchaseSupplierId),N''));

SELECT N'ACTIVE_LINK_RELATION_ANOMALY' AS AuditSection,l.Uid,l.DependentDemandId,l.NetDemandAllocationId,
       l.TargetType,l.TargetNo,l.TargetLineId,
       CASE WHEN a.Uid IS NULL THEN N'ALLOCATION_MISSING'
            WHEN a.NetDemandId<>l.DependentDemandId THEN N'ALLOCATION_WRONG_SOURCE'
            WHEN l.TargetType<>N'PO' OR pol.Uid IS NULL THEN N'PURCHASE_LINE_MISSING'
       END AS Anomaly
FROM dbo.DemandExecutionLink l
LEFT JOIN dbo.NetDemandAllocation a ON a.Uid=l.NetDemandAllocationId
LEFT JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND ISNULL(pol.IsDeleted,0)=0
WHERE l.LinkStatus=0 AND l.IsDeleted=0
  AND (a.Uid IS NULL OR a.NetDemandId<>l.DependentDemandId OR l.TargetType<>N'PO' OR pol.Uid IS NULL);

SELECT N'DUPLICATE_PURCHASE_LINE_NUMBER' AS AuditSection,PurchaseOrderId,LineNumber,COUNT_BIG(1) AS DuplicateCount
FROM dbo.PurchaseOrderLine WHERE IsDeleted=0
GROUP BY PurchaseOrderId,LineNumber HAVING COUNT_BIG(1)>1;

SELECT N'DUPLICATE_PURCHASE_EXECUTION_REQUEST' AS AuditSection,
       ExecutionRequestId,DemandSourceType,DemandSourceUid,SupplierId,COUNT_BIG(1) AS DuplicateCount
FROM dbo.PurchaseOrderHead
WHERE ExecutionRequestId IS NOT NULL AND IsDeleted=0
GROUP BY ExecutionRequestId,DemandSourceType,DemandSourceUid,SupplierId
HAVING COUNT_BIG(1)>1;

SELECT N'DUPLICATE_PLAN_COMMAND_KEY' AS AuditSection,CommandId,OriginalAllocationId,COUNT_BIG(1) AS DuplicateCount
FROM dbo.NetDemandPlanCommandLog WHERE IsDeleted=0
GROUP BY CommandId,OriginalAllocationId HAVING COUNT_BIG(1)>1;

SELECT N'PLAN_COMMAND_RESULT_ANOMALY' AS AuditSection,log.Uid,log.CommandId,log.OriginalAllocationId,
       log.NewAllocationId,log.NewAllocationVersion
FROM dbo.NetDemandPlanCommandLog log
LEFT JOIN dbo.NetDemandAllocation oldAllocation ON oldAllocation.Uid=log.OriginalAllocationId
LEFT JOIN dbo.NetDemandAllocation newAllocation ON newAllocation.Uid=log.NewAllocationId
WHERE log.IsDeleted=0
  AND (oldAllocation.Uid IS NULL OR newAllocation.Uid IS NULL
       OR newAllocation.Version<>log.NewAllocationVersion);

SELECT N'POSTFLIGHT_COUNTS' AS AuditSection,
       (SELECT COUNT_BIG(1) FROM dbo.DependentDemand WHERE LifecycleStatus=0 AND IsDeleted=0) AS ActiveNetDemandCount,
       (SELECT COUNT_BIG(1) FROM dbo.NetDemandAllocation WHERE LifecycleStatus=0 AND IsDeleted=0) AS ActiveAllocationCount,
       (SELECT COUNT_BIG(1) FROM dbo.DemandExecutionLink WHERE LinkStatus=0 AND IsDeleted=0) AS ActiveLinkCount;
