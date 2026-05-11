using Microsoft.Data.SqlClient;

// 尝试 sa 连接，失败则用 Windows 认证
SqlConnection conn;
try
{
    conn = new SqlConnection("Server=localhost;Database=dv;User Id=sa;Password=yw313131;TrustServerCertificate=True;");
    conn.Open();
    System.Console.WriteLine("Connected (sa)");
}
catch
{
    conn = new SqlConnection("Server=localhost;Database=dv;Integrated Security=True;TrustServerCertificate=True;");
    conn.Open();
    System.Console.WriteLine("Connected (Windows Auth)");
}

// 1. 建表
var createTable = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SysUser]') AND type = N'U')
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
END";

using (var cmd = new SqlCommand(createTable, conn))
    cmd.ExecuteNonQuery();
System.Console.WriteLine("SysUser 表 OK");

// 2. Seed dev 用户
var seedSql = @"
IF NOT EXISTS (SELECT 1 FROM [dbo].[SysUser] WHERE [UserName] = 'dev')
INSERT INTO [dbo].[SysUser] ([UserName],[NickName],[PasswordHash],[IsActive],[IsDeleted],[CreatedDate],[CreatedBy])
VALUES ('dev', N'开发用户', @hash, 1, 0, GETDATE(), 'system')";

using (var cmd = new SqlCommand(seedSql, conn))
{
    cmd.Parameters.AddWithValue("@hash", "$2a$11$qZVGHh67AXMUiUtS5OU64O/iBhUVJ2zOgOAuHg3VEKKczOTQQsJdS");
    var rows = cmd.ExecuteNonQuery();
    System.Console.WriteLine(rows > 0 ? "dev 用户插入成功" : "dev 用户已存在，跳过");
}

conn.Dispose();
