/* 仅限一次性测试库。默认回滚；将 @CommitAcceptanceData 改为 1 才保留验收数据。 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @AllowAcceptanceSeed bit=0;
DECLARE @CommitAcceptanceData bit=0;
DECLARE @ExpectedDatabaseName sysname=N'';
IF @AllowAcceptanceSeed<>1 THROW 52030,N'验收数据门禁未开启。',1;
IF NULLIF(@ExpectedDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedDatabaseName THROW 52031,N'数据库名与验收脚本声明不一致。',1;

BEGIN TRAN;
DECLARE @now datetime2(7)=SYSUTCDATETIME(),@user nvarchar(100)=N'AcceptanceSeed';
DECLARE @supplierId uniqueidentifier=(SELECT TOP(1) Uid FROM dbo.Supplier WHERE ISNULL(IsDeleted,0)=0 ORDER BY Uid);
DECLARE @productId uniqueidentifier=(SELECT TOP(1) Uid FROM dbo.Product WHERE ISNULL(IsDeleted,0)=0 ORDER BY Uid);
IF @supplierId IS NULL OR @productId IS NULL THROW 52032,N'验收数据要求至少存在一个有效 Supplier 和 Product。',1;

DECLARE @warehouseId uniqueidentifier=NEWID();
INSERT dbo.WarehouseSite(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,IsActive)
VALUES(@warehouseId,1,0,@now,@user,@now,@user,N'ACC-WH',N'验收虚拟仓',0,@supplierId,1);

DECLARE @balanceId uniqueidentifier=NEWID();
INSERT dbo.InventoryBalance(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WarehouseSiteId,ProductId,OnHandQty,LastLedgerSequence)
VALUES(@balanceId,1,0,@now,@user,@now,@user,@warehouseId,@productId,0,0);

SELECT N'验收基线已写入当前事务' Result,@warehouseId WarehouseSiteId,@balanceId InventoryBalanceId;
IF @CommitAcceptanceData=1 COMMIT; ELSE BEGIN ROLLBACK; SELECT N'默认已回滚；未保留验收数据' Result; END;
