/*
    净需求工作台 Phase 2：只回填可证明关系，并建立计划分配/幂等/采购行约束。
    必须先运行只读 preflight 和 006；本脚本不会猜测默认供应商或历史执行关系。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 51700,N'Refusing to run net demand migration against a system database.',1;
IF OBJECT_ID(N'dbo.NetDemandAllocation',N'U') IS NULL
 OR OBJECT_ID(N'dbo.NetDemandPlanCommandLog',N'U') IS NULL
 OR COL_LENGTH(N'dbo.Product',N'DefaultSupplierId') IS NULL
 OR COL_LENGTH(N'dbo.DemandExecutionLink',N'NetDemandAllocationId') IS NULL
 OR COL_LENGTH(N'dbo.PurchaseOrderHead',N'DemandSourceUid') IS NULL
    THROW 51701,N'Net demand additive schema is incomplete. Run 006 first.',1;

BEGIN TRY
    BEGIN TRANSACTION;

    /* 仅当活动供应商编码唯一时回填 Product.DefaultSupplierId。 */
    ;WITH UniqueSupplier AS
    (
        SELECT LTRIM(RTRIM(s.SupplierCode)) AS SupplierCode,s.Uid AS SupplierId
        FROM dbo.Supplier s
        WHERE ISNULL(s.IsDeleted,0)=0 AND NULLIF(LTRIM(RTRIM(s.SupplierCode)),N'') IS NOT NULL
          AND 1=(SELECT COUNT_BIG(1) FROM dbo.Supplier other
                 WHERE ISNULL(other.IsDeleted,0)=0
                   AND LTRIM(RTRIM(other.SupplierCode))=LTRIM(RTRIM(s.SupplierCode)))
    )
    UPDATE p
       SET DefaultSupplierId=u.SupplierId
    FROM dbo.Product p
    JOIN UniqueSupplier u ON u.SupplierCode=LTRIM(RTRIM(p.DefaultSupplierCode))
    WHERE ISNULL(p.IsDeleted,0)=0 AND p.DefaultSupplierId IS NULL;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Product p
        LEFT JOIN dbo.Supplier s ON s.Uid=p.DefaultSupplierId AND ISNULL(s.IsDeleted,0)=0
        WHERE ISNULL(p.IsDeleted,0)=0 AND p.DefaultSupplierId IS NOT NULL
          AND (s.Uid IS NULL OR (NULLIF(LTRIM(RTRIM(p.DefaultSupplierCode)),N'') IS NOT NULL
               AND LTRIM(RTRIM(p.DefaultSupplierCode))<>LTRIM(RTRIM(s.SupplierCode))))
    )
        THROW 51702,N'Product contains an invalid or code-conflicting DefaultSupplierId.',1;

    UPDATE p
       SET DefaultSupplierCode=s.SupplierCode
    FROM dbo.Product p
    JOIN dbo.Supplier s ON s.Uid=p.DefaultSupplierId AND ISNULL(s.IsDeleted,0)=0
    WHERE ISNULL(p.IsDeleted,0)=0 AND NULLIF(LTRIM(RTRIM(p.DefaultSupplierCode)),N'') IS NULL;

    IF EXISTS
    (
        SELECT 1 FROM dbo.DependentDemand
        WHERE LifecycleStatus=0 AND IsDeleted=0
          AND (NetQty<=0 OR SupplyType NOT IN (N'采购',N'生产',N'外协'))
    )
        THROW 51703,N'Active NetDemand contains invalid quantity or supply type.',1;

    /* 活动净需求没有计划时建立初始计划；历史回退/作废记录不强行生成。 */
    INSERT INTO dbo.NetDemandAllocation
    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
     NetDemandId,PlanRevision,Sequence,LifecycleStatus,ExecutionType,PlannedQty,
     DemandRequiredDate,PlannedDeliveryDate,PurchaseSupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,
     DeliveryTargetAllocationId,DeliveryTargetSupplierId,DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,
     AssignmentSource,ReplacesAllocationId,ChangeReason,CancelReasonCode,CancelledAt,CancelledBy,CancelledReason)
    SELECT NEWID(),1,0,COALESCE(nd.CreatedDate,SYSDATETIME()),COALESCE(nd.CreatedBy,N'migration'),
           SYSDATETIME(),N'migration',nd.Uid,1,1,0,
           CASE nd.SupplyType WHEN N'采购' THEN 0 WHEN N'生产' THEN 1 WHEN N'外协' THEN 2 END,
           nd.NetQty,nd.RequiredDate,nd.RequiredDate,
           CASE WHEN nd.SupplyType=N'采购' THEN COALESCE(preferred.Uid,productSupplier.Uid) END,
           CASE WHEN nd.SupplyType=N'采购' THEN COALESCE(preferred.SupplierCode,productSupplier.SupplierCode) END,
           CASE WHEN nd.SupplyType=N'采购' THEN COALESCE(preferred.SupplierName,productSupplier.SupplierName) END,
           NULL,NULL,NULL,NULL,
           CASE WHEN nd.SupplyType<>N'采购' THEN 1
                WHEN preferred.Uid IS NOT NULL THEN 2
                WHEN productSupplier.Uid IS NOT NULL THEN 0 ELSE 1 END,
           NULL,NULL,NULL,NULL,NULL,NULL
    FROM dbo.DependentDemand nd
    LEFT JOIN dbo.Product p ON p.Uid=nd.ProductId AND ISNULL(p.IsDeleted,0)=0
    LEFT JOIN dbo.Supplier preferred ON preferred.Uid=nd.PreferredSupplierId AND ISNULL(preferred.IsDeleted,0)=0
    LEFT JOIN dbo.Supplier productSupplier ON productSupplier.Uid=p.DefaultSupplierId AND ISNULL(productSupplier.IsDeleted,0)=0
    WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0
      AND NOT EXISTS(SELECT 1 FROM dbo.NetDemandAllocation a WHERE a.NetDemandId=nd.Uid AND a.IsDeleted=0);

    IF EXISTS
    (
        SELECT nd.Uid
        FROM dbo.DependentDemand nd
        LEFT JOIN dbo.NetDemandAllocation a
          ON a.NetDemandId=nd.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
        WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0
        GROUP BY nd.Uid,nd.NetQty
        HAVING COUNT(a.Uid)<>1 OR COALESCE(SUM(a.PlannedQty),0)<>nd.NetQty
    )
        THROW 51704,N'Active NetDemand does not have one complete active allocation.',1;

    /* B 只根据结构化 DeliveryTargetNetDemandId 解析具体 A Allocation。 */
    UPDATE childAllocation
       SET DeliveryTargetAllocationId=targetAllocation.Uid,
           DeliveryTargetSupplierId=targetAllocation.PurchaseSupplierId,
           DeliverySupplierCodeSnapshot=targetAllocation.SupplierCodeSnapshot,
           DeliverySupplierNameSnapshot=targetAllocation.SupplierNameSnapshot,
           UpdatedDate=SYSDATETIME(),UpdatedBy=N'migration'
    FROM dbo.NetDemandAllocation childAllocation
    JOIN dbo.DependentDemand childDemand ON childDemand.Uid=childAllocation.NetDemandId
    JOIN dbo.NetDemandAllocation targetAllocation
      ON targetAllocation.NetDemandId=childDemand.DeliveryTargetNetDemandId
     AND targetAllocation.LifecycleStatus=0 AND targetAllocation.IsDeleted=0
    WHERE childDemand.LifecycleStatus=0 AND childDemand.IsDeleted=0 AND childDemand.DeliveryMode=1
      AND childAllocation.LifecycleStatus=0 AND childAllocation.IsDeleted=0
      AND (ISNULL(CONVERT(nvarchar(36),childAllocation.DeliveryTargetAllocationId),N'')
             <>ISNULL(CONVERT(nvarchar(36),targetAllocation.Uid),N'')
        OR ISNULL(CONVERT(nvarchar(36),childAllocation.DeliveryTargetSupplierId),N'')
             <>ISNULL(CONVERT(nvarchar(36),targetAllocation.PurchaseSupplierId),N'')
        OR ISNULL(childAllocation.DeliverySupplierCodeSnapshot,N'')
             <>ISNULL(targetAllocation.SupplierCodeSnapshot,N'')
        OR ISNULL(childAllocation.DeliverySupplierNameSnapshot,N'')
             <>ISNULL(targetAllocation.SupplierNameSnapshot,N''));

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DependentDemand nd
        JOIN dbo.NetDemandAllocation a ON a.NetDemandId=nd.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
        LEFT JOIN dbo.NetDemandAllocation target ON target.Uid=a.DeliveryTargetAllocationId
        WHERE nd.LifecycleStatus=0 AND nd.IsDeleted=0 AND nd.DeliveryMode=1
          AND (target.Uid IS NULL OR target.NetDemandId<>nd.DeliveryTargetNetDemandId
               OR ISNULL(CONVERT(nvarchar(36),a.DeliveryTargetSupplierId),N'')
                  <>ISNULL(CONVERT(nvarchar(36),target.PurchaseSupplierId),N''))
    )
        THROW 51705,N'Customer-supplied allocation delivery target is not provable.',1;

    /* 旧活动 Link 必须先证明目标采购行和净需求归属。 */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.DemandExecutionLink l
        LEFT JOIN dbo.PurchaseOrderLine pol
          ON pol.Uid=l.TargetLineId AND ISNULL(pol.IsDeleted,0)=0
        WHERE l.LinkStatus=0 AND l.IsDeleted=0
          AND (l.TargetType<>N'PO' OR pol.Uid IS NULL)
    )
        THROW 51706,N'An active legacy DemandExecutionLink does not target an existing purchase line.',1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DemandExecutionLink l
        LEFT JOIN dbo.NetDemandAllocation existingAllocation ON existingAllocation.Uid=l.NetDemandAllocationId
        WHERE l.LinkStatus=0 AND l.IsDeleted=0 AND l.NetDemandAllocationId IS NOT NULL
          AND (existingAllocation.Uid IS NULL OR existingAllocation.NetDemandId<>l.DependentDemandId)
    )
        THROW 51707,N'An active DemandExecutionLink references an orphaned or wrong-source allocation.',1;

    /* 只有净需求存在唯一活动计划时才回填空 AllocationId。 */
    UPDATE l
       SET NetDemandAllocationId=a.Uid
    FROM dbo.DemandExecutionLink l
    JOIN dbo.NetDemandAllocation a ON a.NetDemandId=l.DependentDemandId AND a.LifecycleStatus=0 AND a.IsDeleted=0
    WHERE l.LinkStatus=0 AND l.IsDeleted=0 AND l.NetDemandAllocationId IS NULL;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DemandExecutionLink l
        LEFT JOIN dbo.NetDemandAllocation a ON a.Uid=l.NetDemandAllocationId
        WHERE l.LinkStatus=0 AND l.IsDeleted=0
          AND (a.Uid IS NULL OR a.NetDemandId<>l.DependentDemandId)
    )
        THROW 51708,N'An active legacy DemandExecutionLink cannot be mapped to its net demand allocation.',1;

    /* 采购数量精度统一为需求链路精度，保留原列可空性。 */
    IF EXISTS
    (
        SELECT 1 FROM sys.columns
        WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'QTY'
          AND (precision<>18 OR scale<>4) AND is_nullable=0
    )
        ALTER TABLE dbo.PurchaseOrderLine ALTER COLUMN QTY decimal(18,4) NOT NULL;
    IF EXISTS
    (
        SELECT 1 FROM sys.columns
        WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'QTY'
          AND (precision<>18 OR scale<>4) AND is_nullable=1
    )
        ALTER TABLE dbo.PurchaseOrderLine ALTER COLUMN QTY decimal(18,4) NULL;

    /* 旧采购行只在 OrderNo 唯一指向活动采购头时回填 PurchaseOrderId。 */
    ;WITH UniquePurchaseHead AS
    (
        SELECT OrderNo,MIN(Uid) AS PurchaseOrderId
        FROM dbo.PurchaseOrderHead
        WHERE ISNULL(IsDeleted,0)=0 AND NULLIF(LTRIM(RTRIM(OrderNo)),N'') IS NOT NULL
        GROUP BY OrderNo
        HAVING COUNT_BIG(1)=1
    )
    UPDATE line
       SET PurchaseOrderId=head.PurchaseOrderId
    FROM dbo.PurchaseOrderLine line
    JOIN UniquePurchaseHead head ON head.OrderNo=line.OrderNo
    WHERE ISNULL(line.IsDeleted,0)=0 AND line.PurchaseOrderId IS NULL;

    /* 可证明采购头下缺失或重复的行号确定性重排；无头遗留行保持空值并排除在新命令之外。 */
    ;WITH RankedLine AS
    (
        SELECT Uid,PurchaseOrderId,LineNumber,
               COUNT_BIG(1) OVER(PARTITION BY PurchaseOrderId,LineNumber) AS SameNumberCount
        FROM dbo.PurchaseOrderLine
        WHERE ISNULL(IsDeleted,0)=0 AND PurchaseOrderId IS NOT NULL
    ),
    HeadMax AS
    (
        SELECT PurchaseOrderId,ISNULL(MAX(LineNumber),0) AS MaxLineNumber
        FROM dbo.PurchaseOrderLine
        WHERE ISNULL(IsDeleted,0)=0 AND PurchaseOrderId IS NOT NULL
        GROUP BY PurchaseOrderId
    ),
    Renumber AS
    (
        SELECT line.Uid,
               head.MaxLineNumber+ROW_NUMBER() OVER
               (PARTITION BY line.PurchaseOrderId ORDER BY line.Uid) AS NewLineNumber
        FROM RankedLine line
        JOIN HeadMax head ON head.PurchaseOrderId=line.PurchaseOrderId
        WHERE line.LineNumber IS NULL OR line.SameNumberCount>1
    )
    UPDATE line
       SET LineNumber=renumber.NewLineNumber
    FROM dbo.PurchaseOrderLine line
    JOIN Renumber renumber ON renumber.Uid=line.Uid;

    /* 删除仅由 (OrderNo,Sku) 组成的旧唯一索引/约束。 */
    DECLARE @LegacyIndex sysname,@LegacyIsConstraint bit,@DropSql nvarchar(1000);
    SELECT TOP(1) @LegacyIndex=i.name,@LegacyIsConstraint=i.is_unique_constraint
    FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND i.is_unique=1
      AND (SELECT COUNT(1) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
      AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                 WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0 AND c.name=N'OrderNo')
      AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                 WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0 AND c.name=N'Sku');
    IF @LegacyIndex IS NOT NULL
    BEGIN
        SET @DropSql=CASE WHEN @LegacyIsConstraint=1
            THEN N'ALTER TABLE dbo.PurchaseOrderLine DROP CONSTRAINT '+QUOTENAME(@LegacyIndex)+N';'
            ELSE N'DROP INDEX '+QUOTENAME(@LegacyIndex)+N' ON dbo.PurchaseOrderLine;' END;
        EXEC sys.sp_executesql @DropSql;
    END;

    IF EXISTS
    (
        SELECT PurchaseOrderId,LineNumber FROM dbo.PurchaseOrderLine
        WHERE ISNULL(IsDeleted,0)=0 AND PurchaseOrderId IS NOT NULL AND LineNumber IS NOT NULL
        GROUP BY PurchaseOrderId,LineNumber HAVING COUNT_BIG(1)>1
    )
        THROW 51709,N'PurchaseOrderLine contains duplicate active (PurchaseOrderId,LineNumber).',1;

    IF OBJECT_ID(N'dbo.CK_NetDemandAllocation_State',N'C') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT CK_NetDemandAllocation_State
        CHECK(LifecycleStatus IN(0,8,9) AND ExecutionType IN(0,1,2) AND AssignmentSource IN(0,1,2,3,4,5));
    IF OBJECT_ID(N'dbo.CK_NetDemandAllocation_Quantity',N'C') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT CK_NetDemandAllocation_Quantity
        CHECK(PlannedQty>0 AND PlanRevision>0 AND Sequence>0);
    IF OBJECT_ID(N'dbo.CK_NetDemandAllocation_DeliveryTarget',N'C') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT CK_NetDemandAllocation_DeliveryTarget
        CHECK(DeliveryTargetSupplierId IS NULL OR DeliveryTargetAllocationId IS NOT NULL);
    IF OBJECT_ID(N'dbo.CK_DemandExecutionLink_NewPlanningFields',N'C') IS NULL
        ALTER TABLE dbo.DemandExecutionLink WITH CHECK ADD CONSTRAINT CK_DemandExecutionLink_NewPlanningFields
        CHECK(CommandItemId IS NULL OR (NetDemandAllocationId IS NOT NULL AND ExecutionRequestId IS NOT NULL));
    IF OBJECT_ID(N'dbo.CK_PurchaseOrderHead_LifecycleStatus',N'C') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead WITH CHECK ADD CONSTRAINT CK_PurchaseOrderHead_LifecycleStatus
        CHECK(LifecycleStatus IS NULL OR LifecycleStatus IN(0,8,9));
    IF OBJECT_ID(N'dbo.CK_PurchaseOrderHead_ConfirmationStatus',N'C') IS NULL
        ALTER TABLE dbo.PurchaseOrderHead WITH CHECK ADD CONSTRAINT CK_PurchaseOrderHead_ConfirmationStatus
        CHECK(ConfirmationStatus IS NULL OR ConfirmationStatus IN(0,1));
    IF OBJECT_ID(N'dbo.CK_NetDemandPlanCommandLog_Outcome',N'C') IS NULL
        ALTER TABLE dbo.NetDemandPlanCommandLog WITH CHECK ADD CONSTRAINT CK_NetDemandPlanCommandLog_Outcome
        CHECK(Outcome IN(0,1) AND NewAllocationVersion>0);

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandAllocation') AND name=N'UX_NetDemandAllocation_RevisionSequence')
        CREATE UNIQUE INDEX UX_NetDemandAllocation_RevisionSequence
            ON dbo.NetDemandAllocation(NetDemandId,PlanRevision,Sequence);
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND name=N'UX_DemandExecutionLink_ActiveTargetLine')
        DROP INDEX UX_DemandExecutionLink_ActiveTargetLine ON dbo.DemandExecutionLink;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND name=N'UX_DemandExecutionLink_CommandItem')
        CREATE UNIQUE INDEX UX_DemandExecutionLink_CommandItem ON dbo.DemandExecutionLink(CommandItemId)
            WHERE CommandItemId IS NOT NULL AND IsDeleted=0;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND name=N'UX_PurchaseOrderLine_HeadLineNumber')
        CREATE UNIQUE INDEX UX_PurchaseOrderLine_HeadLineNumber ON dbo.PurchaseOrderLine(PurchaseOrderId,LineNumber)
            WHERE IsDeleted=0 AND PurchaseOrderId IS NOT NULL AND LineNumber IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PurchaseOrderHead') AND name=N'UX_PurchaseOrderHead_ExecutionRequestSourceSupplier')
        CREATE UNIQUE INDEX UX_PurchaseOrderHead_ExecutionRequestSourceSupplier
            ON dbo.PurchaseOrderHead(ExecutionRequestId,DemandSourceType,DemandSourceUid,SupplierId)
            WHERE ExecutionRequestId IS NOT NULL AND IsDeleted=0;
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandPlanCommandLog') AND name=N'IX_NetDemandPlanCommandLog_CommandAllocation')
        DROP INDEX IX_NetDemandPlanCommandLog_CommandAllocation ON dbo.NetDemandPlanCommandLog;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NetDemandPlanCommandLog') AND name=N'UX_NetDemandPlanCommandLog_CommandAllocation')
        CREATE UNIQUE INDEX UX_NetDemandPlanCommandLog_CommandAllocation
            ON dbo.NetDemandPlanCommandLog(CommandId,OriginalAllocationId)
            WHERE IsDeleted=0;

    IF OBJECT_ID(N'dbo.FK_NetDemandAllocation_NetDemand',N'F') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT FK_NetDemandAllocation_NetDemand
            FOREIGN KEY(NetDemandId) REFERENCES dbo.DependentDemand(Uid);
    IF OBJECT_ID(N'dbo.FK_NetDemandAllocation_DeliveryTarget',N'F') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT FK_NetDemandAllocation_DeliveryTarget
            FOREIGN KEY(DeliveryTargetAllocationId) REFERENCES dbo.NetDemandAllocation(Uid);
    IF OBJECT_ID(N'dbo.FK_NetDemandAllocation_Replaces',N'F') IS NULL
        ALTER TABLE dbo.NetDemandAllocation WITH CHECK ADD CONSTRAINT FK_NetDemandAllocation_Replaces
            FOREIGN KEY(ReplacesAllocationId) REFERENCES dbo.NetDemandAllocation(Uid);
    IF OBJECT_ID(N'dbo.FK_DemandExecutionLink_NetDemandAllocation',N'F') IS NULL
        ALTER TABLE dbo.DemandExecutionLink WITH CHECK ADD CONSTRAINT FK_DemandExecutionLink_NetDemandAllocation
            FOREIGN KEY(NetDemandAllocationId) REFERENCES dbo.NetDemandAllocation(Uid);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
