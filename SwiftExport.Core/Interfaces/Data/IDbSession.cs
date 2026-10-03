using System.Data;

namespace SwiftExport.Core.Interfaces.Data;

/// <summary>请求级数据库会话契约；应用层拥有事务，仓储共享连接与事务，内部服务不得提交外层事务。</summary>
public interface IDbSession
{
    /// <summary>当前请求共享的连接；调用方不得另外创建或释放连接。</summary>
    IDbConnection Connection { get; }
    /// <summary>由最外层用例设置与清理的事务，保持既有单一事务机制。</summary>
    IDbTransaction? Transaction { get; set; }
    /// <summary>识别提供者错误，业务层仍决定各用例的错误码和是否允许重试。</summary>
    IDatabaseExceptionClassifier Errors { get; }
}

/// <summary>只识别持久化失败原因，不改变异常、不重试、不决定 HTTP 或业务消息。</summary>
public interface IDatabaseExceptionClassifier
{
    /// <summary>分类原异常；未知异常不能猜测为瞬时故障或业务校验。</summary>
    DatabaseFailureKind Classify(Exception exception);
}

/// <summary>内部持久化错误类别，不作为客户端 API 协议。</summary>
public enum DatabaseFailureKind
{
    /// <summary>未识别原因，不能推定可重试。</summary>
    Unknown,
    /// <summary>数据库唯一约束冲突，具体业务含义由用例决定。</summary>
    UniqueConflict,
    /// <summary>命令或锁等待超时，不等于调用方取消。</summary>
    Timeout,
    /// <summary>数据库因死锁终止事务。</summary>
    Deadlock,
    /// <summary>建立或维持数据库连接失败。</summary>
    ConnectionFailure,
    /// <summary>预期表或列不存在。</summary>
    SchemaMismatch,
    /// <summary>现有入库来源或反向历史未核实，无法计算可靠收货量。</summary>
    UnverifiedReceiptHistory,
    /// <summary>操作已取消，仍需结合调用方令牌处理。</summary>
    Canceled
}
