-- ============================================================
-- 迁移：创建 SysUser 表 + 插入 dev 开发用户
-- 执行时间：2025
-- 注意：密码为 BCrypt hash，明文是 dev123456
-- ============================================================

-- 1. 创建 SysUser 表（若已存在则跳过）
IF NOT EXISTS (
    SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SysUser]') AND type = N'U'
)
BEGIN
    CREATE TABLE [dbo].[SysUser] (
        [Id]           INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
        [UserName]     NVARCHAR(50)        NOT NULL,
        [NickName]     NVARCHAR(50)        NULL,
        [PasswordHash] NVARCHAR(200)       NOT NULL,
        [Email]        NVARCHAR(100)       NULL,
        [PhoneNumber]  NVARCHAR(30)        NULL,
        [IsActive]     BIT                 NOT NULL DEFAULT 1,
        [IsDeleted]    BIT                 NOT NULL DEFAULT 0,
        [CreatedDate]  DATETIME2           NULL,
        [CreatedBy]    NVARCHAR(50)        NULL,
        [UpdatedDate]  DATETIME2           NULL,
        [UpdatedBy]    NVARCHAR(50)        NULL,

        CONSTRAINT [UQ_SysUser_UserName] UNIQUE ([UserName])
    );

    PRINT 'SysUser 表创建成功';
END
ELSE
BEGIN
    PRINT 'SysUser 表已存在，跳过创建';
END
GO

-- 2. Seed：插入 dev 开发用户（幂等，已存在则跳过）
IF NOT EXISTS (
    SELECT 1 FROM [dbo].[SysUser] WHERE [UserName] = 'dev'
)
BEGIN
    INSERT INTO [dbo].[SysUser]
        ([UserName], [NickName], [PasswordHash], [IsActive], [IsDeleted], [CreatedDate], [CreatedBy])
    VALUES
        (
            'dev',
            N'开发用户',
            -- dev123456 的 BCrypt hash（WorkFactor=11），用 CHAR(36) 拼接避免 sqlcmd 变量替换
            CHAR(36)+'2a'+CHAR(36)+'11'+CHAR(36)+'qZVGHh67AXMUiUtS5OU64O/iBhUVJ2zOgOAuHg3VEKKczOTQQsJdS',
            1,
            0,
            GETDATE(),
            'system'
        );

    PRINT 'dev 用户插入成功';
END
ELSE
BEGIN
    PRINT 'dev 用户已存在，跳过插入';
END
GO
