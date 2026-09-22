-- 在01预检和02安全清理完成后手工执行；本脚本不清理任何数据。
-- 不迁移/合并旧投诉，不新增外键或触发器。后端与客户端必须配套发布。
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 THROW 51000,N'请在无外部事务的连接独立执行。',1;
IF OBJECT_ID(N'dbo.AfterSalesHeader',N'U') IS NULL OR OBJECT_ID(N'dbo.AfterSalesLine',N'U') IS NULL
    THROW 51000,N'售后头行表不存在。',1;
IF COL_LENGTH(N'dbo.AfterSalesHeader',N'IssueDescription') IS NOT NULL
BEGIN
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AfterSalesHeader') AND name IN(N'OriginalSalesOrderLineId',N'CustomerComplaintNo',N'ResponsibleSupplierId',N'IssueProductId',N'IssueSkuSnapshot',N'IssueCustomerSkuSnapshot',N'IssueLinkSkuSnapshot',N'IssueDescription',N'QualityCategoryId',N'CauseAnalysis',N'RequiredDate',N'ServiceAmountCny',N'ReturnedReceivedDate',N'TreatmentDescription'))<>14
       OR EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AfterSalesLine') AND name IN(N'OriginalSalesOrderLineId',N'CustomerComplaintNo',N'ResponsibleSupplierId',N'IssueProductId',N'IssueSkuSnapshot',N'IssueCustomerSkuSnapshot',N'IssueLinkSkuSnapshot',N'IssueDescription',N'QualityCategoryId',N'CauseAnalysis',N'RequiredDate',N'ServiceAmountCny',N'ReturnedReceivedDate',N'TreatmentDescription',N'IssueColorId',N'IssueColorSnapshot',N'IssueProductDescription'))
        THROW 51000,N'检测到混合结构，请人工核对，未作修改。',1;
    PRINT N'已是单投诉结构，不再执行破坏性调整。';
    RETURN;
END;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM dbo.AfterSalesHeader WITH(TABLOCKX,HOLDLOCK))
       OR EXISTS(SELECT 1 FROM dbo.AfterSalesLine WITH(TABLOCKX,HOLDLOCK))
        THROW 51000,N'售后测试数据尚未安全清理，禁止调整结构。',1;

    CREATE TABLE #RemovedColumns(Name sysname PRIMARY KEY);
    INSERT #RemovedColumns VALUES
      (N'CustomerComplaintNo'),(N'ResponsibleSupplierId'),(N'IssueProductId'),
      (N'IssueSkuSnapshot'),(N'IssueCustomerSkuSnapshot'),(N'IssueLinkSkuSnapshot'),
      (N'IssueDescription'),(N'QualityCategoryId'),(N'CauseAnalysis'),
      (N'RequiredDate'),(N'ReturnedReceivedDate'),(N'OriginalSalesOrderLineId'),
      (N'TreatmentDescription'),(N'ServiceAmountCny'),
      (N'IssueColorId'),(N'IssueColorSnapshot'),(N'IssueProductDescription');
    DECLARE @object int=OBJECT_ID(N'dbo.AfterSalesLine'),@sql nvarchar(max)=N'';
    -- 只删除依赖被移除列的默认值/CHECK/索引；数量CHECK同时改为允许零。
    SELECT @sql+=N'ALTER TABLE dbo.AfterSalesLine DROP CONSTRAINT '+QUOTENAME(d.name)+N';'
    FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
    WHERE d.parent_object_id=@object AND c.name IN(SELECT Name FROM #RemovedColumns);
    SELECT @sql+=N'ALTER TABLE dbo.AfterSalesLine DROP CONSTRAINT '+QUOTENAME(k.name)+N';'
    FROM sys.check_constraints k WHERE k.parent_object_id=@object AND
      (EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.columns c
        ON c.object_id=d.referenced_id AND c.column_id=d.referenced_minor_id
        WHERE d.referencing_id=k.object_id AND (c.name IN(SELECT Name FROM #RemovedColumns) OR c.name=N'Quantity'))
       OR k.name=N'CK_AfterSalesLine_Quantity');
    SELECT @sql+=CASE WHEN i.is_unique_constraint=1 THEN N'ALTER TABLE dbo.AfterSalesLine DROP CONSTRAINT '+QUOTENAME(i.name)
                     ELSE N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.AfterSalesLine' END+N';'
    FROM sys.indexes i WHERE i.object_id=@object AND i.is_primary_key=0 AND EXISTS(
        SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND c.name IN(SELECT Name FROM #RemovedColumns));
    EXEC sys.sp_executesql @sql;
    SET @sql=N'';
    SELECT @sql+=N'ALTER TABLE dbo.AfterSalesLine DROP COLUMN '+QUOTENAME(c.name)+N';'
    FROM sys.columns c WHERE c.object_id=@object AND c.name IN(SELECT Name FROM #RemovedColumns);
    EXEC sys.sp_executesql @sql;
    ALTER TABLE dbo.AfterSalesLine ADD CONSTRAINT CK_AfterSalesLine_Quantity CHECK(Quantity IS NULL OR Quantity>=0);

    ALTER TABLE dbo.AfterSalesHeader ADD
      OriginalSalesOrderLineId uniqueidentifier NULL,
      CustomerComplaintNo nvarchar(100) NULL,
      ResponsibleSupplierId uniqueidentifier NULL,
      IssueProductId uniqueidentifier NULL,
      IssueSkuSnapshot nvarchar(100) NULL,
      IssueCustomerSkuSnapshot nvarchar(100) NULL,
      IssueLinkSkuSnapshot nvarchar(100) NULL,
      IssueDescription nvarchar(2000) NOT NULL,
      QualityCategoryId uniqueidentifier NULL,
      CauseAnalysis nvarchar(2000) NULL,
      RequiredDate date NULL,
      ServiceAmountCny decimal(18,2) NULL,
      ReturnedReceivedDate date NULL,
      TreatmentDescription nvarchar(2000) NULL;
    EXEC(N'ALTER TABLE dbo.AfterSalesHeader ADD CONSTRAINT CK_AfterSalesHeader_Amount CHECK(ServiceAmountCny IS NULL OR ServiceAmountCny>=0);');
    EXEC(N'CREATE INDEX IX_AfterSalesHeader_Supplier ON dbo.AfterSalesHeader(ResponsibleSupplierId) WHERE IsDeleted=0;');
    -- 原状态列沿用数据库已完成的tinyint调整，不再次修改状态列或其索引。
    DECLARE @column sysname,@description nvarchar(1000);
    DECLARE comments CURSOR LOCAL FAST_FORWARD FOR
    SELECT * FROM (VALUES
      (N'OriginalSalesOrderLineId',N'投诉对应原销售行，可空，必须属于单头客户。'),
      (N'CustomerComplaintNo',N'客户侧投诉单号，可空、允许重复，空白转NULL，最多100字。'),
      (N'ResponsibleSupplierId',N'整单责任供应商，逻辑关联Supplier。'),
      (N'IssueProductId',N'问题成品身份，可空。'),
      (N'IssueSkuSnapshot',N'问题成品物料编号历史快照。'),
      (N'IssueCustomerSkuSnapshot',N'问题成品客户型号历史快照。'),
      (N'IssueLinkSkuSnapshot',N'问题AQ型号字符串快照，不关联ProductNeutral。'),
      (N'IssueDescription',N'单头投诉描述，业务保存必填。'),
      (N'QualityCategoryId',N'预留品质分类身份，可空，功能待开发。'),
      (N'CauseAnalysis',N'整单原因分析，确认后允许补充。'),
      (N'RequiredDate',N'整单补发要求交期，交给每条正数量来源需求。'),
      (N'ServiceAmountCny',N'整单人民币售后成本：NULL未定、0免费、正数付费，不乘数量。'),
      (N'ReturnedReceivedDate',N'客户退回品收到日期，可空，不改变库存。'),
      (N'TreatmentDescription',N'整单处理说明，确认后允许补充。')
    ) q(ColumnName,Description);
    OPEN comments; FETCH NEXT FROM comments INTO @column,@description;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=@description,
            @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'AfterSalesHeader',
            @level2type=N'COLUMN',@level2name=@column;
        FETCH NEXT FROM comments INTO @column,@description;
    END;
    CLOSE comments; DEALLOCATE comments;
    IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=@object AND minor_id=0 AND name=N'MS_Description')
        EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'售后实际补件事实，投诉信息在单头；可零行，空或零数量不进入来源需求。',
            @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'AfterSalesLine';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'售后实际补件事实，投诉信息在单头；可零行，空或零数量不进入来源需求。',
            @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'AfterSalesLine';
    DROP TABLE #RemovedColumns;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
