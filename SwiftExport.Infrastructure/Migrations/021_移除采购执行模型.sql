SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 这些旧对象直接引用待删除列，必须先移除。
IF OBJECT_ID(N'dbo.TR_PurchaseOrder_ExecutionIdentity',N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_PurchaseOrder_ExecutionIdentity;
IF OBJECT_ID(N'dbo.TR_PurchaseOrderLine_IndependentIdentity',N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_PurchaseOrderLine_IndependentIdentity;
IF OBJECT_ID(N'dbo.TR_DemandExecutionLink_IndependentOwnership',N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_DemandExecutionLink_IndependentOwnership;
IF OBJECT_ID(N'dbo.CK_PO_NewExecutionIdentity',N'C') IS NOT NULL
    ALTER TABLE dbo.PurchaseOrderHead DROP CONSTRAINT CK_PO_NewExecutionIdentity;

-- 默认约束名称可能由 SQL Server 自动生成，按列元数据解析后删除。
DECLARE @DefaultConstraint sysname;
SELECT @DefaultConstraint=dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
WHERE dc.parent_object_id=OBJECT_ID(N'dbo.PurchaseOrderHead') AND c.name=N'ExecutionModel';

IF @DefaultConstraint IS NOT NULL
BEGIN
    DECLARE @DropDefaultSql nvarchar(max);
    SET @DropDefaultSql=N'ALTER TABLE dbo.PurchaseOrderHead DROP CONSTRAINT '
        + QUOTENAME(@DefaultConstraint) + N';';
    EXEC sys.sp_executesql @DropDefaultSql;
END;

IF COL_LENGTH(N'dbo.PurchaseOrderHead',N'ExecutionModel') IS NOT NULL
    ALTER TABLE dbo.PurchaseOrderHead DROP COLUMN ExecutionModel;

COMMIT TRANSACTION;
