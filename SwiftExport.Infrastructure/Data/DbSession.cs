// SwiftExport.Infrastructure/Data/DbSession.cs
using Microsoft.Data.SqlClient;
using System.Data;

public class DbSession : IDisposable
{
    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; set; }

    public DbSession(IConfiguration configuration)
    {
        // 从配置读取连接字符串
        Connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        Connection.Open(); // 在 Session 创建时就打开连接
    }

    public void Dispose()
    {
        Connection?.Dispose();
        Transaction?.Dispose();
    }
}