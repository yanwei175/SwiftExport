// SwiftExport.Infrastructure/Data/DbSession.cs
using SwiftExport.Core.Interfaces.Data;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.Common;

namespace SwiftExport.Infrastructure.Data;

/// <summary>管理 scoped 连接及共享事务；通过工厂创建连接，保留会话创建时即打开的行为。</summary>
public class DbSession : IDbSession, IDisposable
{
    private bool _disposed;
    private bool _unusable;
    /// <summary>锁清理异常后的会话不再允许取得或借用命令锁。</summary>
    internal bool IsUnusable => _unusable;
    /// <summary>记录连接开闭周期，防止事务前租约被后续重新打开的连接借用。</summary>
    internal long ConnectionEpoch { get; private set; }
    /// <summary>当前 scope 唯一的命令租约，包括尚在等待获取的租约。</summary>
    internal PostgreSqlCommandLock.Lease? CommandLease { get; set; }
    /// <summary>事务前来源多资源租约的独立占位；与机器命令租约不能互相替代。</summary>
    internal PostgreSqlSourceDemandLock.Lease? SourceDemandLease { get; set; }
    /// <summary>所有仓储和应用用例共享的连接。</summary>
    public IDbConnection Connection { get; }
    /// <summary>最外层业务用例拥有的当前事务。</summary>
    public IDbTransaction? Transaction { get; set; }
    private readonly IDatabaseExceptionClassifier? _errors;
    /// <summary>使用连接工厂分类器；无构造测试会话也采用 PostgreSQL 默认分类器。</summary>
    public IDatabaseExceptionClassifier Errors => _errors ?? PostgreSqlExceptionClassifier.Instance;

    /// <summary>从 Default 配置创建 PostgreSQL 会话，连接由会话打开和释放。</summary>
    public DbSession(IConfiguration configuration)
        : this(new PostgreSqlConnectionFactory(configuration.GetConnectionString("Default")!))
    {
    }

    /// <summary>打开工厂连接；打开失败时释放已创建资源并保留原始异常。</summary>
    public DbSession(IDbConnectionFactory factory)
    {
        _errors = factory.Errors;
        Connection = factory.CreateConnection();
        try
        {
            Connection.Open();
            if (Connection is DbConnection connection) connection.StateChange += ConnectionStateChanged;
        }
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
        // 遗漏显式租约释放时必须丢弃物理连接，不能依赖连接池延迟 reset 来释放会话锁。
        SourceDemandLease?.Abandon();
        CommandLease?.Abandon();
        try { Transaction?.Dispose(); }
        finally
        {
            Transaction = null;
            if (Connection is DbConnection connection) connection.StateChange -= ConnectionStateChanged;
            Connection?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>禁止 scope 后续获取或借用命令锁；实际连接关闭及连接池隔离由租约负责。</summary>
    internal void InvalidateConnection() => _unusable = true;

    /// <summary>连接状态变化使既有租约失效，即使逻辑连接对象没有变化也不能继续使用。</summary>
    private void ConnectionStateChanged(object? sender, StateChangeEventArgs args) => ConnectionEpoch++;
}
