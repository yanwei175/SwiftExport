:on error exit
/*
  031 迁移结构验证夹具。必须用 sqlcmd 模式在名称以 SwiftExportInventoryTest 开头的隔离空库执行：
  cd <仓库根目录>\SwiftExport.Infrastructure\Migrations\Verification
  sqlcmd -S <server> -d SwiftExportInventoryTest -b -i 031_InventoryImmutableLedger_MigrationVerification.sql
  :r 按 sqlcmd 当前工作目录解析，因此必须先进入上述 Verification 目录。
  夹具只在通过库名保护后创建并删除本测试所需的最小表，不连接或修改业务库。
*/
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'SwiftExportInventoryTest%'
    THROW 53200,N'迁移验证只能在 SwiftExportInventoryTest* 隔离库执行。',1;
IF OBJECT_ID(N'dbo.WarehouseSite',N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.Product',N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.InventoryBalance',N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.InventoryLedgerEntry',N'U') IS NOT NULL
    THROW 53203,N'迁移验证要求新建的空隔离库；检测到同名表，已停止且不会删除。',1;
GO

CREATE TABLE dbo.WarehouseSite(Uid uniqueidentifier NOT NULL PRIMARY KEY);
CREATE TABLE dbo.Product(Uid uniqueidentifier NOT NULL PRIMARY KEY);
CREATE TABLE dbo.InventoryBalance
(
    Uid uniqueidentifier NOT NULL PRIMARY KEY,
    WarehouseSiteId uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    OnHandQty decimal(18,4) NOT NULL CONSTRAINT DF_VerifyBalanceQty DEFAULT(0),
    LastLedgerSequence bigint NOT NULL CONSTRAINT DF_VerifyBalanceSequence DEFAULT(0),
    IsDeleted bit NOT NULL CONSTRAINT DF_VerifyBalanceDeleted DEFAULT(0)
);
CREATE TABLE dbo.InventoryLedgerEntry
(
    Uid uniqueidentifier NOT NULL PRIMARY KEY,
    Sequence bigint NOT NULL,
    WarehouseSiteId uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    TransactionType int NOT NULL,
    QuantityDelta decimal(18,4) NOT NULL,
    BalanceAfter decimal(18,4) NOT NULL,
    ReversalOfLedgerId uniqueidentifier NULL,
    CommandId uniqueidentifier NOT NULL,
    CommandItemId uniqueidentifier NOT NULL,
    IsDeleted bit NOT NULL CONSTRAINT DF_VerifyLedgerDeleted DEFAULT(0)
);
GO

-- 无 WarehouseNameSnapshot 基线：首次迁移与紧接着的重复迁移都必须成功。
:r ..\031_InventoryImmutableLedger_AdditiveSchema.sql
GO
:r ..\031_InventoryImmutableLedger_AdditiveSchema.sql
GO
IF NOT EXISTS
(
    SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND c.name=N'WarehouseNameSnapshot'
      AND t.name=N'nvarchar' AND c.max_length=600 AND c.is_nullable=1
)
    THROW 53201,N'无列基线首次/重复迁移后 WarehouseNameSnapshot 不是 nvarchar(300) NULL。',1;
GO

DROP TABLE dbo.InventoryLedgerEntry;
DROP TABLE dbo.InventoryBalance;
DROP TABLE dbo.Product;
DROP TABLE dbo.WarehouseSite;
GO

CREATE TABLE dbo.WarehouseSite(Uid uniqueidentifier NOT NULL PRIMARY KEY);
CREATE TABLE dbo.Product(Uid uniqueidentifier NOT NULL PRIMARY KEY);
CREATE TABLE dbo.InventoryBalance
(
    Uid uniqueidentifier NOT NULL PRIMARY KEY,
    WarehouseSiteId uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    OnHandQty decimal(18,4) NOT NULL CONSTRAINT DF_VerifyBalanceQty2 DEFAULT(0),
    LastLedgerSequence bigint NOT NULL CONSTRAINT DF_VerifyBalanceSequence2 DEFAULT(0),
    IsDeleted bit NOT NULL CONSTRAINT DF_VerifyBalanceDeleted2 DEFAULT(0)
);
CREATE TABLE dbo.InventoryLedgerEntry
(
    Uid uniqueidentifier NOT NULL PRIMARY KEY,
    Sequence bigint NOT NULL,
    WarehouseSiteId uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    TransactionType int NOT NULL,
    QuantityDelta decimal(18,4) NOT NULL,
    BalanceAfter decimal(18,4) NOT NULL,
    ReversalOfLedgerId uniqueidentifier NULL,
    CommandId uniqueidentifier NOT NULL,
    CommandItemId uniqueidentifier NOT NULL,
    WarehouseNameSnapshot nvarchar(200) NULL,
    IsDeleted bit NOT NULL CONSTRAINT DF_VerifyLedgerDeleted2 DEFAULT(0)
);
GO

-- 旧 nvarchar(200) 基线：迁移只扩容元数据，重复执行仍应成功。
:r ..\031_InventoryImmutableLedger_AdditiveSchema.sql
GO
:r ..\031_InventoryImmutableLedger_AdditiveSchema.sql
GO
IF NOT EXISTS
(
    SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') AND c.name=N'WarehouseNameSnapshot'
      AND t.name=N'nvarchar' AND c.max_length=600 AND c.is_nullable=1
)
    THROW 53202,N'nvarchar(200) 基线首次/重复迁移后未安全扩容为 nvarchar(300) NULL。',1;
GO

DROP TABLE dbo.InventoryLedgerEntry;
DROP TABLE dbo.InventoryBalance;
DROP TABLE dbo.Product;
DROP TABLE dbo.WarehouseSite;
GO
