using System;
using System.Data;
using Microsoft.Data.SqlClient;
using SwiftExport.Infrastructure.Interfaces;

namespace SwiftExport.Infrastructure.Factory
{
    /// <summary>
    /// 简化的连接工厂：仅支持 SQL Server（Microsoft.Data.SqlClient）。
    /// 每次 CreateConnection 返回新的 SqlConnection（短连接模式）。
    /// </summary>
    public class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public SqlConnectionFactory (string connString)
        {
            _connectionString = connString ?? throw new ArgumentNullException("配置文件里没有配置数据库字符串!");
        }

        public IDbConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}
