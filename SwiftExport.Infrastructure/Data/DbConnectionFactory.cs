using System.Data;

namespace SwiftExport.Infrastructure.Data;

/// <summary>创建尚未打开的PG连接；会话负责打开和释放，连接配置由基础设施接收。</summary>
public interface IDbConnectionFactory
{
    /// <summary>默认使用当前PG异常分类器，供会话保持既有失败分类边界。</summary>
    SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;

    /// <summary>创建独立连接，所有权移交给调用会话。</summary>
    IDbConnection CreateConnection();
}
