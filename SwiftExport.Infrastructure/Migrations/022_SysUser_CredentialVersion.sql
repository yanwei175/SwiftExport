SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.SysUser', N'U') IS NULL
        THROW 51000, N'dbo.SysUser 不存在，不能应用凭据版本迁移。', 1;

    IF COL_LENGTH(N'dbo.SysUser', N'Uid') IS NULL
       OR COL_LENGTH(N'dbo.SysUser', N'Version') IS NULL
       OR COL_LENGTH(N'dbo.SysUser', N'PasswordHash') IS NULL
        THROW 51000, N'dbo.SysUser 不是当前 Uid/Version 基线，请先完成数据库基线迁移。', 1;

    IF COL_LENGTH(N'dbo.SysUser', N'CredentialVersion') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.SysUser ADD CredentialVersion int NULL;';
    END;

    -- 新列必须在独立动态批次中引用，避免 SQL Server 在条件 ALTER TABLE 执行前解析列名。
    EXEC sys.sp_executesql N'
        UPDATE dbo.SysUser
        SET CredentialVersion=1
        WHERE CredentialVersion IS NULL OR CredentialVersion<1;';

    IF EXISTS(
        SELECT 1
        FROM sys.columns
        WHERE object_id=OBJECT_ID(N'dbo.SysUser')
          AND name=N'CredentialVersion'
          AND is_nullable=1)
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.SysUser ALTER COLUMN CredentialVersion int NOT NULL;';
    END;

    IF NOT EXISTS(
        SELECT 1
        FROM sys.default_constraints dc
        JOIN sys.columns c
          ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
        WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SysUser')
          AND c.name=N'CredentialVersion')
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.SysUser
                ADD CONSTRAINT DF_SysUser_CredentialVersion DEFAULT(1) FOR CredentialVersion;';
    END;

    IF OBJECT_ID(N'dbo.CK_SysUser_CredentialVersion_Positive', N'C') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.SysUser WITH CHECK
                ADD CONSTRAINT CK_SysUser_CredentialVersion_Positive CHECK(CredentialVersion>=1);';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
