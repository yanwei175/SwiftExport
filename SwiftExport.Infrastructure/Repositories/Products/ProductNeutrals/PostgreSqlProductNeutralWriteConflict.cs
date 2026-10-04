using Npgsql;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

/// <summary>仅供两个中性 PostgreSQL 仓储共用的唯一冲突白名单，不参与全局异常分类或事务处理。</summary>
internal static class PostgreSqlProductNeutralWriteConflict
{
    /// <summary>只接受 public.product_neutral 的两个精确唯一索引，其他 SQLSTATE 或作用域不得转换。</summary>
    internal static bool IsKnown(PostgresException exception) => exception.SqlState == "23505"
        && exception.SchemaName == "public" && exception.TableName == "product_neutral"
        && exception.ConstraintName is "ux_product_neutral_product_code" or "ux_product_neutral_master_color";

    /// <summary>已命中白名单的异常只产生固定文案及零影响行回执，不暴露数据库消息或约束明细。</summary>
    internal static RepositoryWriteResult CreateResult(Guid uid, int? oldVersion, PostgresException exception)
        => exception.ConstraintName == "ux_product_neutral_product_code"
            ? RepositoryWriteResult.Fail(uid, oldVersion, "PRODUCT_NEUTRAL_CODE_OCCUPIED", "中性产品编码已被占用")
            : RepositoryWriteResult.Fail(uid, oldVersion, "PRODUCT_NEUTRAL_MASTER_COLOR_OCCUPIED", "该主型号下颜色已被占用");
}
