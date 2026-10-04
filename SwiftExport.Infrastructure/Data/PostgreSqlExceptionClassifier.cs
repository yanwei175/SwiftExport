using Npgsql;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>按SQLSTATE识别已明确的提供者失败；不根据消息文本猜测，不触发自动重试。</summary>
public sealed class PostgreSqlExceptionClassifier : IDatabaseExceptionClassifier
{
    /// <summary>无状态分类器，供工厂和测试共享。</summary>
    public static PostgreSqlExceptionClassifier Instance { get; } = new();

    /// <summary>保留取消、唯一、死锁、连接、结构的区别；57014可能是服务端取消或超时，保持未知。</summary>
    public DatabaseFailureKind Classify(Exception exception) => exception switch
    {
        OperationCanceledException => DatabaseFailureKind.Canceled,
        PostgresException { SqlState: "23505" } => DatabaseFailureKind.UniqueConflict,
        PostgresException { SqlState: "40P01" } => DatabaseFailureKind.Deadlock,
        PostgresException { SqlState: "42P01" or "42703" } => DatabaseFailureKind.SchemaMismatch,
        PostgresException postgres when postgres.SqlState.StartsWith("08", StringComparison.Ordinal) => DatabaseFailureKind.ConnectionFailure,
        // 其他SQLSTATE（含40001、55P03）不能冒充乐观并发、唯一冲突或可重试超时。
        PostgresException => DatabaseFailureKind.Unknown,
        NpgsqlException { InnerException: TimeoutException } => DatabaseFailureKind.Timeout,
        NpgsqlException { InnerException: IOException } => DatabaseFailureKind.ConnectionFailure,
        _ => DatabaseFailureKind.Unknown
    };
}
