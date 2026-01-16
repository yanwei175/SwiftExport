using Microsoft.Data.SqlClient;
using System.Data;

namespace SwiftExport.Infrastructure.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    private readonly string _connectionString = connectionString;

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}