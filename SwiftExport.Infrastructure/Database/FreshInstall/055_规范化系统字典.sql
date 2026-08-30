/*
  在新目标数据库中规范化通用系统字典。
  本脚本只修改目标库中已经由 050 复制的 SysDictType/SysDictData。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowTargetDictionaryNormalization bit=1;
DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next';

IF @AllowTargetDictionaryNormalization<>1 THROW 52550,N'系统字典规范化门禁未开启。',1;
IF NULLIF(@ExpectedTargetDatabaseName,N'') IS NULL OR DB_NAME()<>@ExpectedTargetDatabaseName
    THROW 52551,N'当前数据库不是指定目标数据库。',1;
IF OBJECT_ID(N'dbo.SysDictType',N'U') IS NULL OR OBJECT_ID(N'dbo.SysDictData',N'U') IS NULL
    THROW 52552,N'目标库缺少 SysDictType 或 SysDictData。',1;

BEGIN TRY
    BEGIN TRAN;

    DELETE FROM dbo.SysDictData WHERE DictCode IN (N'ProductCategory',N'ProductColor');
    DELETE FROM dbo.SysDictType WHERE DictCode IN (N'ProductCategory',N'ProductColor');

    DECLARE @Types TABLE(DictCode nvarchar(100) NOT NULL PRIMARY KEY,DictName nvarchar(200) NOT NULL,SortOrder int NOT NULL);
    INSERT @Types VALUES
     (N'Incoterm',N'国际贸易术语',20),(N'PaymentMethod',N'付款方式',30),(N'ShippedBy',N'运输方式',40);

    ;WITH ranked_types AS
    (
        SELECT target.Uid,
               ROW_NUMBER() OVER(PARTITION BY target.DictCode ORDER BY ISNULL(target.IsDeleted,0),target.Uid) RowNo
        FROM dbo.SysDictType target JOIN @Types source ON source.DictCode=target.DictCode
    )
    DELETE target
    FROM dbo.SysDictType target JOIN ranked_types ranked ON ranked.Uid=target.Uid
    WHERE ranked.RowNo>1;

    UPDATE target
       SET target.DictName=source.DictName,target.IsSystem=1,target.SortOrder=source.SortOrder,
           target.GroupCode=N'Sales',target.GroupName=N'销售',target.IsDeleted=0,
           target.Version=CASE WHEN target.Version IS NULL OR target.Version<1 THEN 1 ELSE target.Version END,
           target.UpdatedDate=SYSUTCDATETIME()
    FROM dbo.SysDictType target JOIN @Types source ON source.DictCode=target.DictCode;

    INSERT dbo.SysDictType
    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,DictCode,DictName,IsSystem,Remark,ParentId,SortOrder,TargetViewKey,GroupCode,GroupName)
    SELECT NEWID(),1,0,SYSUTCDATETIME(),N'fresh-install-055',NULL,NULL,
           source.DictCode,source.DictName,1,N'系统字典类型；字典值允许维护。',NULL,source.SortOrder,NULL,N'Sales',N'销售'
    FROM @Types source
    WHERE NOT EXISTS(SELECT 1 FROM dbo.SysDictType target WHERE target.DictCode=source.DictCode);

    DECLARE @Values TABLE
    (DictCode nvarchar(100) NOT NULL,DataNameEn nvarchar(200) NOT NULL,DataNameZh nvarchar(200) NOT NULL,SortOrder int NOT NULL,
     PRIMARY KEY(DictCode,DataNameEn));
    INSERT @Values VALUES
     (N'Incoterm',N'EXW',N'工厂交货',10),(N'Incoterm',N'FOB',N'船上交货',20),
     (N'Incoterm',N'CIF',N'成本、保险加运费',30),(N'Incoterm',N'DAP',N'目的地交货',40),
     (N'Incoterm',N'DDP',N'完税后交货',50),(N'Incoterm',N'CPT',N'运费付至',60),
     (N'Incoterm',N'CIP',N'运费和保险费付至',70),
     (N'PaymentMethod',N'TT',N'电汇',10),(N'PaymentMethod',N'LC',N'信用证',20),
     (N'PaymentMethod',N'OA',N'赊账',30),(N'PaymentMethod',N'DP',N'付款交单',40),
     (N'PaymentMethod',N'DA',N'承兑交单',50),
     (N'ShippedBy',N'SEA',N'海运',10),(N'ShippedBy',N'AIR',N'空运',20),
     (N'ShippedBy',N'COURIER',N'快递',30);

    INSERT dbo.SysDictData
    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,DictCode,DataNameEn,DataNameZh,Remark,SortOrder,CssClass,IsSystem,IsEnabled)
    SELECT NEWID(),1,0,SYSUTCDATETIME(),N'fresh-install-055',NULL,NULL,
           source.DictCode,source.DataNameEn,source.DataNameZh,NULL,source.SortOrder,NULL,0,1
    FROM @Values source
    WHERE NOT EXISTS
    (SELECT 1 FROM dbo.SysDictData target
     WHERE target.DictCode=source.DictCode AND target.DataNameEn=source.DataNameEn AND ISNULL(target.IsDeleted,0)=0);

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;

IF EXISTS(SELECT 1 FROM dbo.SysDictType WHERE DictCode IN(N'ProductCategory',N'ProductColor'))
    THROW 52553,N'独立主数据的通用字典遗留类型清理失败。',1;
IF EXISTS(SELECT 1 FROM dbo.SysDictData WHERE DictCode IN(N'ProductCategory',N'ProductColor'))
    THROW 52554,N'独立主数据的通用字典遗留值清理失败。',1;
