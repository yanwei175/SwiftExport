SET NOCOUNT ON;
SELECT PackingListNo,COUNT(*) AS DuplicateCount FROM dbo.PackingListHead WHERE Uid IS NOT NULL AND ISNULL(IsDeleted,0)=0 GROUP BY PackingListNo HAVING COUNT(*)>1;
SELECT PackingListDocumentId,NetDemandAllocationId,COUNT(*) AS DuplicateCount FROM dbo.PackingListLine WHERE PackingListDocumentId IS NOT NULL AND ISNULL(IsDeleted,0)=0 GROUP BY PackingListDocumentId,NetDemandAllocationId HAVING COUNT(*)>1;
SELECT CommandItemId,COUNT(*) AS DuplicateCount FROM dbo.PackingListLine WHERE CommandItemId IS NOT NULL GROUP BY CommandItemId HAVING COUNT(*)>1;
