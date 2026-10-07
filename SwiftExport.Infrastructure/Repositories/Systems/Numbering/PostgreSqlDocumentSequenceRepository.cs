using Dapper;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Entities.Systems.Numbering;
using SwiftExport.Core.Interfaces.Systems.Numbering;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Numbering;

/// <summary>PostgreSQL 可回滚业务取号；依赖活动业务键唯一索引，在调用方事务内原子初始化或递增。</summary>
public sealed class PostgreSqlDocumentSequenceRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<DocumentSequence>(context), IDocumentSequenceRepository
{
    private static readonly string QualifiedTable = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private const string Columns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "doc_type" AS "DocType",
        "year_month" AS "YearMonth",
        "current_no" AS "CurrentNo"
    """;

    /// <summary>分配指定类型及年月的下一个号码并返回持久化行；共享外层事务，无事务时单语句自动提交，不修改既有 Version。</summary>
    public async Task<DocumentSequence> GetNextAsync(string docType, string yearMonth, string? updatedBy)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", GuidSequentialGenerator.NewGuid());
        parameters.Add("DocType", docType);
        parameters.Add("YearMonth", yearMonth);
        parameters.Add("UpdatedBy", updatedBy);
        // 部分唯一索引仲裁新键竞争；更新保留真实 Uid、Version 和创建字段，不使用不可回滚的 nextval。
        // 时间保持 SQL Server 取号的服务器 UTC 墙上时间；NULL 操作者由 CAST 明确文本类型。
        var sql = $@"
            INSERT INTO {QualifiedTable} AS target
                (uid, version, is_deleted, doc_type, year_month, current_no,
                 created_date, created_by, updated_date, updated_by)
            VALUES (@Uid, 1, FALSE, @DocType, @YearMonth, 1,
                clock_timestamp() AT TIME ZONE 'UTC', CAST(@UpdatedBy AS text),
                clock_timestamp() AT TIME ZONE 'UTC', CAST(@UpdatedBy AS text))
            ON CONFLICT (doc_type, year_month) WHERE is_deleted = FALSE
            DO UPDATE SET current_no = target.current_no + 1,
                updated_date = clock_timestamp() AT TIME ZONE 'UTC', updated_by = EXCLUDED.updated_by
            RETURNING {Columns};";
        // 提供者异常原样交给调用方，不在仓储内重试、提交或打印 SQL 错误。
        return await Connection.QuerySingleAsync<DocumentSequence>(sql, parameters, transaction: Transaction);
    }
}
