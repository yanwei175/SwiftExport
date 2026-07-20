using Microsoft.Data.SqlClient;
using System;

var connectionString = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("缺少环境变量 SWIFTEXPORT_DBSEED_CONNECTION，DbSeed 不允许使用内置连接信息。");

var userName = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_USERNAME");
var passwordHash = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_PASSWORD_HASH");
var nickName = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_NICKNAME") ?? userName;
if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(passwordHash))
    throw new InvalidOperationException("必须同时设置 SWIFTEXPORT_DBSEED_USERNAME 和 SWIFTEXPORT_DBSEED_PASSWORD_HASH。");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

const string schemaCheckSql = """
    SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SysUser',N'U') IS NOT NULL
                          AND COL_LENGTH(N'dbo.SysUser',N'Uid') IS NOT NULL
                          AND COL_LENGTH(N'dbo.SysUser',N'Version') IS NOT NULL
                          AND COL_LENGTH(N'dbo.SysUser',N'UserName') IS NOT NULL
                          AND COL_LENGTH(N'dbo.SysUser',N'PasswordHash') IS NOT NULL
                     THEN 1 ELSE 0 END AS bit);
    """;
await using (var schemaCommand = new SqlCommand(schemaCheckSql, connection))
{
    if (!(bool)(await schemaCommand.ExecuteScalarAsync() ?? false))
        throw new InvalidOperationException("dbo.SysUser 不存在或不是当前 Uid/Version 基线；DbSeed 不负责创建例外表。");
}

const string seedSql = """
    IF NOT EXISTS (SELECT 1 FROM dbo.SysUser WHERE UserName=@UserName AND ISNULL(IsDeleted,0)=0)
    BEGIN
        INSERT dbo.SysUser
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,UserName,NickName,PasswordHash,IsActive)
        VALUES
            (NEWID(),1,0,SYSUTCDATETIME(),N'DbSeed',SYSUTCDATETIME(),N'DbSeed',@UserName,@NickName,@PasswordHash,1);
    END;
    """;
await using (var seedCommand = new SqlCommand(seedSql, connection))
{
    seedCommand.Parameters.AddWithValue("@UserName", userName);
    seedCommand.Parameters.AddWithValue("@NickName", (object?)nickName ?? DBNull.Value);
    seedCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
    var rows = await seedCommand.ExecuteNonQueryAsync();
    Console.WriteLine(rows > 0 ? $"用户 {userName} 已写入。" : $"用户 {userName} 已存在，未修改。");
}
