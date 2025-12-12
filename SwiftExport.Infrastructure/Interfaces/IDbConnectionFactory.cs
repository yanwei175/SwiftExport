using System.Data;

namespace SwiftExport.Infrastructure.Interfaces
{
    /// <summary>
    /// 抽象：用于创建短生命周期的数据库连接
    /// </summary>
    public interface IDbConnectionFactory  
    {
        IDbConnection CreateConnection();
    }
}