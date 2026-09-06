// SwiftExport.Infrastructure/Data/DbSession.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace SwiftExport.Infrastructure.Data;

public class DbSession : IDisposable
{
    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; set; }

    public DbSession(IConfiguration configuration)
    {
        // 从配置读取连接字符串
        Connection = new SqlConnection(configuration.GetConnectionString("Default"));
        Connection.Open(); // 在 Session 创建时就打开连接
    }

    public void Dispose()
    {
        // 1. 先清理事务 (如果还没提交，这里会自动回滚)
        Transaction?.Dispose();

        // 2. 再清理连接
        Connection?.Dispose();

        // 3. 告诉 GC 不要再调用终结器 (解决你的报错)
        GC.SuppressFinalize(this);
    }
}
