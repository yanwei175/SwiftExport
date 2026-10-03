using Microsoft.Data.SqlClient;
using System.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>创建尚未打开的连接；会话负责打开和释放，提供者选择留在基础设施层。</summary>
public interface IDbConnectionFactory
{
    /// <summary>创建独立连接，所有权移交给调用会话。</summary>
    IDbConnection CreateConnection();
}

/// <summary>按宿主提供的配置创建 SQL Server 连接，不改变现有数据库运行行为。</summary>
public class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    private readonly string _connectionString = connectionString;

    /// <summary>返回未打开的 SqlClient 连接，不记录连接字符串。</summary>
    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
