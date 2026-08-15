/*
  在新目标数据库中执行。
  仅对已迁入的白名单表补目标关系字段、索引和必要外键；不删除、不猜测、不覆盖原始编码。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowTargetEnrichment bit=1;
DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next';

IF @AllowTargetEnrichment<>1 THROW 52600,N'白名单目标结构补充门禁未开启。',1;
IF NULLIF(@ExpectedTargetDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedTargetDatabaseName
    THROW 52601,N'当前数据库不是指定目标数据库。',1;
IF @@TRANCOUNT<>0 THROW 52602,N'当前连接存在未结束事务。',1;

DECLARE @Required TABLE(TableName sysname NOT NULL PRIMARY KEY);
INSERT @Required(TableName) VALUES
 (N'CustomerProduct'),(N'CustomerPoConfig'),(N'Customer'),(N'Product'),(N'ProductCategory'),
 (N'ProductColor'),(N'ProductDetails'),(N'Supplier'),(N'SysDicCurrency'),(N'SysDictData'),
 (N'SysDictType'),(N'SysRoles'),(N'SysUser'),(N'SysUser_Roles'),(N'SysViewRoute');
IF EXISTS
(
    SELECT 1 FROM @Required r
    WHERE OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(r.TableName),N'U') IS NULL
)
    THROW 52603,N'目标数据库的白名单表尚未全部迁入。',1;

BEGIN TRY
    BEGIN TRAN;

    IF COL_LENGTH(N'dbo.ProductDetails',N'ProductId') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ProductDetails ADD ProductId uniqueidentifier NULL;';
    IF COL_LENGTH(N'dbo.CustomerPoConfig',N'CustomerId') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerPoConfig ADD CustomerId uniqueidentifier NULL;';
    IF COL_LENGTH(N'dbo.CustomerPoConfig',N'SupplierId') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerPoConfig ADD SupplierId uniqueidentifier NULL;';
    IF COL_LENGTH(N'dbo.Product',N'Unit') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Product ADD Unit nvarchar(100) NULL;';
    ELSE
    BEGIN
        IF EXISTS(SELECT 1 FROM dbo.Product WHERE LEN(Unit)>100)
            THROW 52604,N'Product.Unit 存在超过 100 个字符的数据，不能规范列长度。',1;
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Product ALTER COLUMN Unit nvarchar(100) NULL;';
    END;
    IF COL_LENGTH(N'dbo.CustomerProduct',N'CustomerSkuUnit') IS NOT NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerProduct DROP COLUMN CustomerSkuUnit;';
    IF COL_LENGTH(N'dbo.CustomerProduct',N'Currency') IS NOT NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerProduct DROP COLUMN Currency;';

    EXEC sys.sp_executesql N'
;WITH UniqueProduct AS
(
    SELECT Sku,Uid,COUNT_BIG(*) OVER(PARTITION BY Sku) MatchCount
    FROM dbo.Product
    WHERE ISNULL(IsDeleted,0)=0
)
UPDATE d SET ProductId=p.Uid
FROM dbo.ProductDetails d
JOIN UniqueProduct p ON p.Sku=d.ProductSKU AND p.MatchCount=1
WHERE d.ProductId IS NULL;';

    EXEC sys.sp_executesql N'
;WITH UniqueCustomer AS
(
    SELECT CustomerCode,Uid,COUNT_BIG(*) OVER(PARTITION BY CustomerCode) MatchCount
    FROM dbo.Customer
    WHERE ISNULL(IsDeleted,0)=0
)
UPDATE c SET CustomerId=x.Uid
FROM dbo.CustomerPoConfig c
JOIN UniqueCustomer x ON x.CustomerCode=c.CustomerCode AND x.MatchCount=1
WHERE c.CustomerId IS NULL;';

    EXEC sys.sp_executesql N'
;WITH UniqueSupplier AS
(
    SELECT SupplierCode,Uid,COUNT_BIG(*) OVER(PARTITION BY SupplierCode) MatchCount
    FROM dbo.Supplier
    WHERE ISNULL(IsDeleted,0)=0
)
UPDATE c SET SupplierId=x.Uid
FROM dbo.CustomerPoConfig c
JOIN UniqueSupplier x ON x.SupplierCode=c.SupplierCode AND x.MatchCount=1
WHERE c.SupplierId IS NULL;';

    CREATE UNIQUE INDEX UX_Product_Sku ON dbo.Product(Sku) WHERE IsDeleted=0;
    CREATE INDEX IX_Product_DefaultSupplierId ON dbo.Product(DefaultSupplierId) WHERE IsDeleted=0 AND DefaultSupplierId IS NOT NULL;
    CREATE UNIQUE INDEX UX_Customer_CustomerCode ON dbo.Customer(CustomerCode) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_Supplier_SupplierCode ON dbo.Supplier(SupplierCode) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_ProductCategory_CategoryCode ON dbo.ProductCategory(CategoryCode) WHERE IsDeleted=0 AND CategoryCode IS NOT NULL;
    CREATE UNIQUE INDEX UX_ProductColor_ColorCodeUnique ON dbo.ProductColor(ColorCodeUnique) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_CustomerProduct_Customer_CustomerSku ON dbo.CustomerProduct(CustomerId,CustomerSku) WHERE IsDeleted=0;
    CREATE INDEX IX_CustomerProduct_SkuId ON dbo.CustomerProduct(SkuId) WHERE IsDeleted=0 AND SkuId IS NOT NULL;
    CREATE INDEX IX_ProductDetails_ProductSKU ON dbo.ProductDetails(ProductSKU) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_CustomerPoConfig_Codes ON dbo.CustomerPoConfig(CustomerCode,SupplierCode) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysDicCurrency_CurrencyCode ON dbo.SysDicCurrency(CurrencyCode) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysDictType_DictCode ON dbo.SysDictType(DictCode) WHERE IsDeleted=0;
    CREATE INDEX IX_SysDictData_DictCode ON dbo.SysDictData(DictCode) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysRoles_RoleName ON dbo.SysRoles(RoleName) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysUser_UserName ON dbo.SysUser(UserName) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysUserRoles_User_Role ON dbo.SysUser_Roles(UserId,RoleId) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_SysViewRoute_TargetViewKey ON dbo.SysViewRoute(TargetViewKey) WHERE IsDeleted=0;

    EXEC sys.sp_executesql N'CREATE INDEX IX_ProductDetails_ProductId ON dbo.ProductDetails(ProductId) WHERE IsDeleted=0 AND ProductId IS NOT NULL;';
    EXEC sys.sp_executesql N'CREATE INDEX IX_CustomerPoConfig_CustomerId_SupplierId ON dbo.CustomerPoConfig(CustomerId,SupplierId) WHERE IsDeleted=0;';
    EXEC sys.sp_executesql N'ALTER TABLE dbo.ProductDetails WITH CHECK ADD CONSTRAINT FK_ProductDetails_Product FOREIGN KEY(ProductId) REFERENCES dbo.Product(Uid);';
    EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerPoConfig WITH CHECK ADD CONSTRAINT FK_CustomerPoConfig_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customer(Uid);';
    EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerPoConfig WITH CHECK ADD CONSTRAINT FK_CustomerPoConfig_Supplier FOREIGN KEY(SupplierId) REFERENCES dbo.Supplier(Uid);';

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;

EXEC sys.sp_executesql N'
SELECT N''WARNING'' Result,N''ProductDetails.ProductId 未回填'' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.ProductDetails WHERE ProductId IS NULL HAVING COUNT_BIG(*)>0;
SELECT N''WARNING'' Result,N''CustomerPoConfig.CustomerId 未回填'' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig
WHERE NULLIF(LTRIM(RTRIM(CustomerCode)),N'''') IS NOT NULL AND CustomerId IS NULL
HAVING COUNT_BIG(*)>0;
SELECT N''WARNING'' Result,N''CustomerPoConfig.SupplierId 未回填'' Problem,COUNT_BIG(*) ProblemCount
FROM dbo.CustomerPoConfig
WHERE NULLIF(LTRIM(RTRIM(SupplierCode)),N'''') IS NOT NULL AND SupplierId IS NULL
HAVING COUNT_BIG(*)>0;';
