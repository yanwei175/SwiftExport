SET XACT_ABORT ON;
SET NOCOUNT ON;

IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 52900,N'拒绝在系统数据库执行采购 Allocation 依赖清理。',1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.DemandExecutionLink',N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.DemandExecutionLink',N'NetDemandAllocationId') IS NOT NULL
    BEGIN
        DECLARE @sql nvarchar(max)=N'';

        /* 删除 Link 历史 Allocation 列涉及的外键；历史列和值继续保留。 */
        SELECT @sql=@sql+N'ALTER TABLE dbo.DemandExecutionLink DROP CONSTRAINT '+QUOTENAME(fk.name)+N';'
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.DemandExecutionLink')
          AND fkc.parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.DemandExecutionLink'),N'NetDemandAllocationId',N'ColumnId');
        IF @sql<>N'' EXEC sys.sp_executesql @sql;

        SET @sql=N'';
        /* 删除仍把历史 Allocation 列作为键或包含列的索引。 */
        SELECT @sql=@sql+N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.DemandExecutionLink;'
        FROM sys.indexes i
        WHERE i.object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND i.is_primary_key=0 AND i.is_unique_constraint=0
          AND EXISTS
          (
              SELECT 1 FROM sys.index_columns ic
              WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                AND ic.column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.DemandExecutionLink'),N'NetDemandAllocationId',N'ColumnId')
          );
        IF @sql<>N'' EXEC sys.sp_executesql @sql;

        SET @sql=N'';
        /* 旧检查约束要求每个命令项必须绑定 Allocation，统一移除后改按 NetDemand 校验。 */
        SELECT @sql=@sql+N'ALTER TABLE dbo.DemandExecutionLink DROP CONSTRAINT '+QUOTENAME(cc.name)+N';'
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id=OBJECT_ID(N'dbo.DemandExecutionLink')
          AND cc.definition LIKE N'%NetDemandAllocationId%';
        IF @sql<>N'' EXEC sys.sp_executesql @sql;

        IF EXISTS
        (
            SELECT 1 FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND name=N'NetDemandAllocationId' AND is_nullable=0
        )
            ALTER TABLE dbo.DemandExecutionLink ALTER COLUMN NetDemandAllocationId uniqueidentifier NULL;

        IF OBJECT_ID(N'dbo.CK_DemandExecutionLink_NewPlanningFields',N'C') IS NULL
            ALTER TABLE dbo.DemandExecutionLink WITH CHECK ADD CONSTRAINT CK_DemandExecutionLink_NewPlanningFields
                CHECK(CommandItemId IS NULL OR (NetDemandId IS NOT NULL AND ExecutionRequestId IS NOT NULL));

        IF NOT EXISTS
        (
            SELECT 1 FROM sys.indexes
            WHERE object_id=OBJECT_ID(N'dbo.DemandExecutionLink') AND name=N'IX_DemandExecutionLink_NetDemandStatus'
        )
            CREATE INDEX IX_DemandExecutionLink_NetDemandStatus
                ON dbo.DemandExecutionLink(NetDemandId,LinkStatus,IsDeleted)
                INCLUDE(TargetHeadId,TargetLineId,AllocatedQty,ReleasedQty,PlannedDeliveryDate);
    END;

    IF OBJECT_ID(N'dbo.PurchaseOrderLine',N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PurchaseOrderLine',N'DeliveryTargetAllocationId') IS NOT NULL
    BEGIN
        DECLARE @purchaseSql nvarchar(max)=N'';

        /* 采购行不再以 Allocation 约束交付目标；列和值保留作历史兼容。 */
        SELECT @purchaseSql=@purchaseSql+N'ALTER TABLE dbo.PurchaseOrderLine DROP CONSTRAINT '+QUOTENAME(fk.name)+N';'
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.PurchaseOrderLine')
          AND fkc.parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.PurchaseOrderLine'),N'DeliveryTargetAllocationId',N'ColumnId');
        IF @purchaseSql<>N'' EXEC sys.sp_executesql @purchaseSql;

        SET @purchaseSql=N'';
        SELECT @purchaseSql=@purchaseSql+N'ALTER TABLE dbo.PurchaseOrderLine DROP CONSTRAINT '+QUOTENAME(cc.name)+N';'
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id=OBJECT_ID(N'dbo.PurchaseOrderLine')
          AND cc.definition LIKE N'%DeliveryTargetAllocationId%';
        IF @purchaseSql<>N'' EXEC sys.sp_executesql @purchaseSql;

        SET @purchaseSql=N'';
        SELECT @purchaseSql=@purchaseSql+N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.PurchaseOrderLine;'
        FROM sys.indexes i
        WHERE i.object_id=OBJECT_ID(N'dbo.PurchaseOrderLine') AND i.is_primary_key=0 AND i.is_unique_constraint=0
          AND EXISTS
          (
              SELECT 1 FROM sys.index_columns ic
              WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                AND ic.column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.PurchaseOrderLine'),N'DeliveryTargetAllocationId',N'ColumnId')
          );
        IF @purchaseSql<>N'' EXEC sys.sp_executesql @purchaseSql;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
