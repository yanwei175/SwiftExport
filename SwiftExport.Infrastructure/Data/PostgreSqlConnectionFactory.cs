using System.Data;
using Npgsql;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>创建未打开的PostgreSQL连接；作为宿主默认工厂，由DbSession统一打开、共享事务并释放。</summary>
public sealed class PostgreSqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    /// <summary>返回与Npgsql连接匹配的分类器，不决定业务错误或重试。</summary>
    public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;

    /// <summary>使用调用者配置创建连接，不访问数据库或记录连接密钥。</summary>
    public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}
