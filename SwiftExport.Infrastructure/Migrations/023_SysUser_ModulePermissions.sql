SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 默认只预览历史采购授权；审核结果后显式改为 1 并再次执行。
DECLARE @ApplyLegacyProcurementMigration bit = 0;

IF OBJECT_ID(N'dbo.SysUser',N'U') IS NULL
   OR COL_LENGTH(N'dbo.SysUser',N'Uid') IS NULL
   OR OBJECT_ID(N'dbo.SysRoles',N'U') IS NULL
   OR OBJECT_ID(N'dbo.SysUser_Roles',N'U') IS NULL
    THROW 50001,N'账号或角色基线不存在，不能建立用户模块权限。',1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SysUser_ModulePermissions',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SysUser_ModulePermissions
    (
        Uid uniqueidentifier NOT NULL,
        Version int NOT NULL CONSTRAINT DF_SysUser_ModulePermissions_Version DEFAULT(1),
        IsDeleted bit NOT NULL CONSTRAINT DF_SysUser_ModulePermissions_IsDeleted DEFAULT(0),
        CreatedDate datetime2(7) NULL,
        CreatedBy nvarchar(256) NULL,
        UpdatedDate datetime2(7) NULL,
        UpdatedBy nvarchar(256) NULL,
        UserId uniqueidentifier NOT NULL,
        PermissionKey nvarchar(128) NOT NULL,
        CONSTRAINT PK_SysUser_ModulePermissions PRIMARY KEY(Uid),
        CONSTRAINT CK_SysUser_ModulePermissions_Version CHECK(Version>=1),
        CONSTRAINT CK_SysUser_ModulePermissions_Key CHECK(PermissionKey IN
        (
            N'Module.Products.Access',N'Module.Customers.Access',N'Module.Suppliers.Access',
            N'Module.Sales.Access',N'Module.DemandPlanning.Access',N'Module.Procurement.Access',
            N'Module.Inventory.Access',N'Module.Shipping.Access'
        ))
    );
END;

IF COL_LENGTH(N'dbo.SysUser_ModulePermissions',N'Uid') IS NULL
   OR COL_LENGTH(N'dbo.SysUser_ModulePermissions',N'UserId') IS NULL
   OR COL_LENGTH(N'dbo.SysUser_ModulePermissions',N'PermissionKey') IS NULL
   OR COL_LENGTH(N'dbo.SysUser_ModulePermissions',N'Version') IS NULL
   OR COL_LENGTH(N'dbo.SysUser_ModulePermissions',N'IsDeleted') IS NULL
    THROW 50002,N'现有用户模块权限表不符合 Uid、关系键、版本及软删除基线。',1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes
              WHERE object_id=OBJECT_ID(N'dbo.SysUser_ModulePermissions')
                AND name=N'UX_SysUser_ModulePermissions_ActiveUserKey')
    CREATE UNIQUE INDEX UX_SysUser_ModulePermissions_ActiveUserKey
        ON dbo.SysUser_ModulePermissions(UserId,PermissionKey)
        WHERE IsDeleted=0;

IF NOT EXISTS(SELECT 1 FROM sys.indexes
              WHERE object_id=OBJECT_ID(N'dbo.SysUser_ModulePermissions')
                AND name=N'IX_SysUser_ModulePermissions_ActivePermissionKey')
    CREATE INDEX IX_SysUser_ModulePermissions_ActivePermissionKey
        ON dbo.SysUser_ModulePermissions(PermissionKey,UserId)
        WHERE IsDeleted=0;

-- 只信任 RoleId 指向的有效角色主表；普通用户拥有任一历史采购细权限即进入预览。
SELECT DISTINCT u.Uid AS UserId,u.UserName,r.RoleName AS LegacyPermission,
       CAST(CASE WHEN p.Uid IS NULL THEN 0 ELSE 1 END AS bit) AS AlreadyMigrated
INTO #LegacyProcurementPreview
FROM dbo.SysUser u
JOIN dbo.SysUser_Roles ur ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
LEFT JOIN dbo.SysUser_ModulePermissions p
  ON p.UserId=u.Uid AND p.PermissionKey=N'Module.Procurement.Access' AND p.IsDeleted=0
WHERE u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
  AND r.RoleName IN(N'Procurement.Order.Read',N'Procurement.Order.Create',
                    N'Procurement.Order.Edit',N'Procurement.Order.Confirm')
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.SysUser_Roles aur
      JOIN dbo.SysRoles ar ON ar.Uid=aur.RoleId AND ISNULL(ar.IsDeleted,0)=0
      WHERE aur.UserId=u.Uid AND ISNULL(aur.IsDeleted,0)=0 AND ar.RoleName=N'Admin'
  );

SELECT UserId,UserName,LegacyPermission,AlreadyMigrated
FROM #LegacyProcurementPreview
ORDER BY UserName,UserId,LegacyPermission;

-- 单独列出孤儿关联及冗余名称冲突，脚本不会据此猜测角色或提升管理员。
SELECT ur.UserId,ur.RoleId,ur.UserName AS SnapshotUserName,ur.RoleName AS SnapshotRoleName,
       r.RoleName AS ActualRoleName,
       CASE WHEN r.Uid IS NULL THEN N'OrphanRoleId' ELSE N'RoleNameMismatch' END AS Issue
FROM dbo.SysUser_Roles ur
LEFT JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
WHERE ISNULL(ur.IsDeleted,0)=0
  AND (r.Uid IS NULL OR ISNULL(ur.RoleName,N'')<>ISNULL(r.RoleName,N''))
ORDER BY ur.UserId,ur.RoleId;

DECLARE @InsertedCount int = 0;
IF @ApplyLegacyProcurementMigration=1
BEGIN
    INSERT dbo.SysUser_ModulePermissions
        (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,UserId,PermissionKey)
    SELECT NEWID(),1,0,SYSUTCDATETIME(),N'Migration023',SYSUTCDATETIME(),N'Migration023',
           preview.UserId,N'Module.Procurement.Access'
    FROM (SELECT DISTINCT UserId FROM #LegacyProcurementPreview WHERE AlreadyMigrated=0) preview
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.SysUser_ModulePermissions currentPermission WITH(UPDLOCK,HOLDLOCK)
        WHERE currentPermission.UserId=preview.UserId
          AND currentPermission.PermissionKey=N'Module.Procurement.Access'
          AND currentPermission.IsDeleted=0
    );
    SET @InsertedCount=@@ROWCOUNT;
END;

SELECT @ApplyLegacyProcurementMigration AS Applied,@InsertedCount AS InsertedCount,
       N'Migration023' AS AuditActor;

DROP TABLE #LegacyProcurementPreview;
COMMIT TRANSACTION;
