using Microsoft.Data.SqlClient;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>集中识别 SQL Server 失败原因；不按消息文本猜测、不自动重试，也不统一不同业务错误。</summary>
public sealed class SqlServerExceptionClassifier : IDatabaseExceptionClassifier
{
    /// <summary>无状态分类器，供当前 SQL Server 会话共享。</summary>
    public static SqlServerExceptionClassifier Instance { get; } = new();

    /// <summary>取消只按异常类型识别；请求令牌已取消时由用例先处理，不把超时当取消。</summary>
    public DatabaseFailureKind Classify(Exception exception)
    {
        if (exception is OperationCanceledException) return DatabaseFailureKind.Canceled;
        if (exception is not SqlException sql) return DatabaseFailureKind.Unknown;
        return sql.Number switch
        {
            2601 or 2627 => DatabaseFailureKind.UniqueConflict,
            -2 or 1222 => DatabaseFailureKind.Timeout,
            1205 => DatabaseFailureKind.Deadlock,
            2 or 20 or 53 or 64 or 233 or 10053 or 10054 or 10060 => DatabaseFailureKind.ConnectionFailure,
            207 or 208 => DatabaseFailureKind.SchemaMismatch,
            // 这是现有入库历史完整性 SQL 门禁的业务信号，不能合并到普通结构错误。
            51001 => DatabaseFailureKind.UnverifiedReceiptHistory,
            _ => DatabaseFailureKind.Unknown
        };
    }
}
