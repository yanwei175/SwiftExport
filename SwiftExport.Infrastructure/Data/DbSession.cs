// SwiftExport.Infrastructure/Data/DbSession.cs
using SwiftExport.Core.Interfaces.Data;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>管理 scoped 连接及共享事务；通过工厂创建连接，保留会话创建时即打开的行为。</summary>
public class DbSession : IDbSession, IDisposable
{
    private bool _disposed;
    /// <summary>所有仓储和应用用例共享的连接。</summary>
    public IDbConnection Connection { get; }
    /// <summary>最外层业务用例拥有的当前事务。</summary>
    public IDbTransaction? Transaction { get; set; }
    private readonly IDatabaseExceptionClassifier? _errors;
    /// <summary>连接工厂提供的分类器；兼容既有无构造测试替身的SQL Server默认值。</summary>
    public IDatabaseExceptionClassifier Errors => _errors ?? SqlServerExceptionClassifier.Instance;

    /// <summary>保留现有工具和测试的配置构造入口，连接创建统一委托工厂。</summary>
    public DbSession(IConfiguration configuration)
        : this(new SqlConnectionFactory(configuration.GetConnectionString("Default")!))
    {
    }

    /// <summary>打开工厂连接；打开失败时释放已创建资源并保留原始异常。</summary>
    public DbSession(IDbConnectionFactory factory)
    {
        _errors = factory.Errors;
        Connection = factory.CreateConnection();
        try { Connection.Open(); }
        catch
        {
            try { Connection.Dispose(); } catch { /* 不能用释放异常覆盖连接失败。 */ }
            throw;
        }
    }

    /// <summary>释放残留事务和连接；即使事务释放失败也必须释放连接。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Transaction?.Dispose(); }
        finally
        {
            Transaction = null;
            Connection?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
