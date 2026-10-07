using System.Data;
using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Auth.Development;

/// <summary>在既有 scoped PostgreSQL 连接中仅读取开发用户标识；缓存、claims 和启用条件由调用方负责。</summary>
public sealed class PostgreSqlDevUserQuery(DbSession session)
{
    private static readonly EntityMetadata Metadata = EntityMetadataCache.GetMetadata(typeof(SysUser));
    private static readonly string Sql = $"""
        SELECT "uid"
        FROM {PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}
        WHERE rtrim("user_name", ' ') COLLATE public.erp_dictionary_ci
            = rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci
          AND "is_active" = TRUE
          AND COALESCE("is_deleted", FALSE) = FALSE
        LIMIT 1;
        """;

    /// <summary>按用户名原值查询活动且未删账号，缺失返回 null；不传事务或令牌、不创建事务，数据库错误原样传播。</summary>
    public Task<Guid?> FindUidAsync(string userName)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserName", userName, DbType.String);
        // 保留原开发查询不借用 session.Transaction 的调用形式；PG快照读取不能模拟 NOLOCK 脏读。
        return session.Connection.ExecuteScalarAsync<Guid?>(Sql, parameters);
    }

}

