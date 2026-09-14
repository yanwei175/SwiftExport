/* 人工审核后使用：仅对现有实体同名表增量调整；绝不执行共享库迁移或回填猜测来源。
   使用前需当次授权、备份及 033 Preflight 审核。遇未知约束整体回滚，不擅自放宽。
   被清理的四个来源列保留历史，只允许新单不填写。不修改已执行迁移。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb') THROW 53300,N'禁止在系统数据库执行。',1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.OutboundDocument',N'U') IS NULL OR OBJECT_ID(N'dbo.OutboundDocumentLine',N'U') IS NULL
        THROW 53301,N'缺少当前出库事实表。禁止根据旧表名推测迁移，请先核对物理结构。',1;
    IF COL_LENGTH(N'dbo.OutboundDocument',N'DocumentNo') IS NULL OR COL_LENGTH(N'dbo.OutboundDocumentLine',N'SkuSnapshot') IS NULL
        OR COL_LENGTH(N'dbo.OutboundDocumentLine',N'CommandItemId') IS NULL
        THROW 53302,N'出库基础字段未对齐，先完成 Preflight。',1;
    IF COL_LENGTH(N'dbo.OutboundDocument',N'SourcePackingListId') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NOT NULL
            THROW 53303,N'两个触发字段同时存在，禁止自动合并历史值。',1;
        EXEC sys.sp_rename N'dbo.OutboundDocument.SourcePackingListId',N'TriggerPackingListId',N'COLUMN';
    END;
    IF COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NULL
        ALTER TABLE dbo.OutboundDocument ADD TriggerPackingListId uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.OutboundDocument',N'Remark') IS NULL
        ALTER TABLE dbo.OutboundDocument ADD Remark nvarchar(500) NULL;

    -- 原值完整保留，仅解除新单不再承担的来源必填性；类型异常不自动转换。
    DECLARE @column sysname,@sql nvarchar(max);
    DECLARE legacy_columns CURSOR LOCAL FAST_FORWARD FOR
        SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine')
            AND name IN(N'PackingListContributionId',N'GrossDemandId',N'NetDemandAllocationId') AND is_nullable=0;
    OPEN legacy_columns; FETCH NEXT FROM legacy_columns INTO @column;
    WHILE @@FETCH_STATUS=0
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=@column AND TYPE_NAME(user_type_id)=N'uniqueidentifier')
            THROW 53304,N'历史来源列类型异常，需人工核对。',1;
        SET @sql=N'ALTER TABLE dbo.OutboundDocumentLine ALTER COLUMN '+QUOTENAME(@column)+N' uniqueidentifier NULL;';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM legacy_columns INTO @column;
    END;
    CLOSE legacy_columns; DEALLOCATE legacy_columns;
    IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'SalesOrderNoSnapshot' AND is_nullable=0)
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'SalesOrderNoSnapshot' AND TYPE_NAME(user_type_id)=N'nvarchar' AND max_length=200)
            THROW 53305,N'历史销售编号快照类型异常，需人工核对。',1;
        ALTER TABLE dbo.OutboundDocumentLine ALTER COLUMN SalesOrderNoSnapshot nvarchar(100) NULL;
    END;

    -- 仅识别单列旧枚举 CHECK。复杂表达式不能自动拆解，否则可能意外放宽其他规则。
    DECLARE @table sysname,@constraint sysname,@definition nvarchar(max),@normalized nvarchar(max),@replacement nvarchar(max);
    DECLARE old_checks CURSOR LOCAL FAST_FORWARD FOR
        SELECT OBJECT_NAME(parent_object_id),name,definition FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.OutboundDocument')
            AND name NOT IN(N'CK_Outbound_ManualStockIssue',N'CK_Outbound_Remark')
            AND (definition LIKE N'%DocumentType%' OR definition LIKE N'%PostingStatus%');
    OPEN old_checks; FETCH NEXT FROM old_checks INTO @table,@constraint,@definition;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @normalized=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@definition,N'[',N''),N']',N''),N' ',N''),N'(',N''),N')',N'');
        SET @replacement=CASE
            WHEN @normalized IN(N'DocumentType=20ORDocumentType=21',N'DocumentType=21ORDocumentType=20',N'DocumentTypeIN20,21',N'DocumentType=22ORDocumentType=21ORDocumentType=20',N'DocumentType=20ORDocumentType=21ORDocumentType=22',N'DocumentTypeIN20,21,22') THEN N'DocumentType IN(20,21,22)'
            WHEN @normalized IN(N'PostingStatus=1ORPostingStatus=8',N'PostingStatus=8ORPostingStatus=1',N'PostingStatusIN1,8',N'PostingStatus=8ORPostingStatus=1ORPostingStatus=0',N'PostingStatus=0ORPostingStatus=1ORPostingStatus=8',N'PostingStatusIN0,1,8') THEN N'PostingStatus IN(0,1,8)'
            ELSE NULL END;
        IF @replacement IS NULL
            THROW 53306,N'发现未识别的类型/状态 CHECK，先按 Preflight 人工审核增量脚本，不自动删除约束。',1;
        SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' DROP CONSTRAINT '+QUOTENAME(@constraint)+N'; ALTER TABLE dbo.'+QUOTENAME(@table)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@constraint)+N' CHECK('+@replacement+N');';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM old_checks INTO @table,@constraint,@definition;
    END;
    CLOSE old_checks; DEALLOCATE old_checks;
    -- 仅兼容目标库已人工确认的单列白名单；保留所有旧值，只加入普通库存出库 22。
    -- 去除括号和空白后仍按完整表达式比对，额外 AND/OR、额外类型或同名复杂约束均拒绝。
    DECLARE @ledgerTypeDefinition nvarchar(max),@ledgerTypeNormalized nvarchar(max);
    SELECT @ledgerTypeDefinition=definition FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND name=N'CK_InventoryLedger_Type';
    IF @ledgerTypeDefinition IS NOT NULL
    BEGIN
        SET @ledgerTypeNormalized=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            @ledgerTypeDefinition,N'[',N''),N']',N''),N' ',N''),N'(',N''),N')',N''),NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N''));
        IF @ledgerTypeNormalized NOT IN(
            N'TRANSACTIONTYPE=900ORTRANSACTIONTYPE=100ORTRANSACTIONTYPE=90ORTRANSACTIONTYPE=21ORTRANSACTIONTYPE=20ORTRANSACTIONTYPE=12ORTRANSACTIONTYPE=11ORTRANSACTIONTYPE=10',
            N'TRANSACTIONTYPE=900ORTRANSACTIONTYPE=100ORTRANSACTIONTYPE=90ORTRANSACTIONTYPE=22ORTRANSACTIONTYPE=21ORTRANSACTIONTYPE=20ORTRANSACTIONTYPE=12ORTRANSACTIONTYPE=11ORTRANSACTIONTYPE=10')
            THROW 53307,N'CK_InventoryLedger_Type 不是已审核白名单，禁止自动替换；请先人工核对完整定义。',1;
        IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry')
            AND name=N'CK_InventoryLedger_Type' AND (is_disabled=1 OR is_not_trusted=1))
            THROW 53307,N'库存类型白名单已禁用或未受信任，需先人工核对历史数据。',1;
        IF @ledgerTypeNormalized=N'TRANSACTIONTYPE=900ORTRANSACTIONTYPE=100ORTRANSACTIONTYPE=90ORTRANSACTIONTYPE=21ORTRANSACTIONTYPE=20ORTRANSACTIONTYPE=12ORTRANSACTIONTYPE=11ORTRANSACTIONTYPE=10'
        BEGIN
            ALTER TABLE dbo.InventoryLedgerEntry DROP CONSTRAINT CK_InventoryLedger_Type;
            ALTER TABLE dbo.InventoryLedgerEntry WITH CHECK ADD CONSTRAINT CK_InventoryLedger_Type CHECK(
                TransactionType=900 OR TransactionType=100 OR TransactionType=90 OR TransactionType=22
                OR TransactionType=21 OR TransactionType=20 OR TransactionType=12 OR TransactionType=11 OR TransactionType=10);
        END;
    END;
    -- Relationships 完整保留；其他未知交易类型或复合符号约束继续阻断，不普遍删除保护。
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry')
        AND definition LIKE N'%TransactionType%' AND name NOT IN(N'CK_InventoryLedger_Relationships',N'CK_InventoryLedger_StockIssueSign',N'CK_InventoryLedger_Type'))
        THROW 53307,N'库存存在额外交易类型约束，需人工加入类型22负向语义后再部署。',1;

    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'Quantity' AND TYPE_NAME(user_type_id)=N'decimal' AND precision=18 AND scale=4 AND is_nullable=0)
        THROW 53308,N'出库数量必须为非空 decimal(18,4)，禁止自动转换历史数量。',1;
    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocument') AND name=N'Remark' AND TYPE_NAME(user_type_id)=N'nvarchar' AND max_length=1000 AND is_nullable=1)
        THROW 53309,N'Remark 必须是可空 nvarchar(500)，禁止自动截断。',1;
    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OutboundDocument') AND name=N'TriggerPackingListId' AND TYPE_NAME(user_type_id)=N'uniqueidentifier' AND is_nullable=1)
        THROW 53310,N'触发装箱单字段必须是可空 uniqueidentifier。',1;
    IF OBJECT_ID(N'dbo.CK_Outbound_ManualStockIssue',N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.OutboundDocument WITH CHECK ADD CONSTRAINT CK_Outbound_ManualStockIssue CHECK(DocumentType<>22 OR (TriggerPackingListId IS NULL AND PostingStatus IN(0,1,8)));');
    IF OBJECT_ID(N'dbo.CK_Outbound_Remark',N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.OutboundDocument WITH CHECK ADD CONSTRAINT CK_Outbound_Remark CHECK(Remark IS NULL OR DATALENGTH(Remark)<=1000);');
    IF OBJECT_ID(N'dbo.CK_InventoryLedger_StockIssueSign',N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.InventoryLedgerEntry WITH CHECK ADD CONSTRAINT CK_InventoryLedger_StockIssueSign CHECK(TransactionType<>22 OR QuantityDelta<0);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundDocument') AND name=N'UX_OutboundManager_DocumentNo')
        CREATE UNIQUE INDEX UX_OutboundManager_DocumentNo ON dbo.OutboundDocument(DocumentNo);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'UX_OutboundManager_CommandItem')
        CREATE UNIQUE INDEX UX_OutboundManager_CommandItem ON dbo.OutboundDocumentLine(CommandItemId) WHERE CommandItemId IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND name=N'IX_OutboundManager_Header')
        CREATE INDEX IX_OutboundManager_Header ON dbo.OutboundDocumentLine(OutboundDocumentId,IsDeleted) INCLUDE(ProductId,Quantity,CommandItemId);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
