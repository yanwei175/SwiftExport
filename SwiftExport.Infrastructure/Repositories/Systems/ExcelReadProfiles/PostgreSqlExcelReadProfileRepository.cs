using Dapper;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;

/// <summary>在共享 PostgreSQL 会话中持久化地址配置聚合；事务及主配置锁由应用层持有，不替换 SQL Server 注册。</summary>
public sealed class PostgreSqlExcelReadProfileRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ExcelReadProfile>(context), IExcelReadProfileRepository
{
    private static readonly EntityMetadata FieldMetadata = EntityMetadataCache.GetMetadata(typeof(ExcelReadField));
    private const string ProfileColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "group_code" AS "GroupCode",
        "config_name" AS "ConfigName",
        "end_mode" AS "EndMode",
        "last_row_column" AS "LastRowColumn",
        "summary_cell_address" AS "SummaryCellAddress",
        "remark" AS "Remark"
    """;
    private const string FieldColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "profile_uid" AS "ProfileUid",
        "target_field_key" AS "TargetFieldKey",
        "worksheet_name" AS "WorksheetName",
        "cell_address" AS "CellAddress",
        "is_downward" AS "IsDownward"
    """;
    private const string ProfileInsertColumns = """
        "uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "group_code",
        "config_name",
        "end_mode",
        "last_row_column",
        "summary_cell_address",
        "remark"
    """;
    private const string ProfileInsertParameters = """
        @Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @GroupCode,
        @ConfigName,
        @EndMode,
        @LastRowColumn,
        @SummaryCellAddress,
        @Remark
    """;
    private const string FieldInsertColumns = """
        "uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "profile_uid",
        "target_field_key",
        "worksheet_name",
        "cell_address",
        "is_downward"
    """;
    private const string FieldInsertParameters = """
        @Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @ProfileUid,
        @TargetFieldKey,
        @WorksheetName,
        @CellAddress,
        @IsDownward
    """;

    /// <summary>读取分组内未删除配置并按名称、Uid 排序；名称比较沿用目标列规则，等价性由实库验收确认。</summary>
    public async Task<IReadOnlyList<ExcelReadProfile>> ListAsync(string groupCode, CancellationToken cancellationToken)
        => (await Connection.QueryAsync<ExcelReadProfile>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM public.excel_read_profile WHERE group_code=@GroupCode AND is_deleted=FALSE ORDER BY config_name, uid",
            new { GroupCode = groupCode }, Transaction, cancellationToken: cancellationToken))).AsList();

    /// <summary>读取活动主配置；写路径只锁定现存行并要求外层事务，不提供缺失行范围锁。</summary>
    public Task<ExcelReadProfile?> FindAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate) RequireTransaction();
        var lockClause = forUpdate ? " FOR UPDATE" : string.Empty;
        return Connection.QuerySingleOrDefaultAsync<ExcelReadProfile>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM public.excel_read_profile WHERE uid=@Uid AND is_deleted=FALSE{lockClause}",
            new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>读取指定主配置的全部活动字段并按目标键、Uid 排序，不创建独立字段事务。</summary>
    public async Task<IReadOnlyList<ExcelReadField>> GetFieldsAsync(Guid uid, CancellationToken cancellationToken)
        => (await Connection.QueryAsync<ExcelReadField>(new CommandDefinition(
            $"SELECT {FieldColumns} FROM public.excel_read_field WHERE profile_uid=@Uid AND is_deleted=FALSE ORDER BY target_field_key, uid",
            new { Uid = uid }, Transaction, cancellationToken: cancellationToken))).AsList();

    /// <summary>在外层事务中插入主配置并打审计戳；保留传入 Uid、版本及配置值。</summary>
    public async Task InsertAsync(ExcelReadProfile profile, CancellationToken cancellationToken)
    {
        RequireTransaction();
        AuditStamp.StampForInsert(profile);
        await Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO public.excel_read_profile ({ProfileInsertColumns}) VALUES ({ProfileInsertParameters})",
            PostgreSqlParameters.Entity(profile, Metadata), Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>按主配置预期版本更新或软删除，成功才回填新版本；分组身份保持不变。</summary>
    public async Task<bool> UpdateAsync(ExcelReadProfile profile, int expectedVersion, CancellationToken cancellationToken)
    {
        RequireTransaction();
        AuditStamp.StampForUpdate(profile);
        var parameters = PostgreSqlParameters.Entity(profile, Metadata);
        parameters.Add("ExpectedVersion", expectedVersion);
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            @"UPDATE public.excel_read_profile SET config_name=@ConfigName,end_mode=@EndMode,last_row_column=@LastRowColumn,
                summary_cell_address=@SummaryCellAddress,remark=@Remark,is_deleted=@IsDeleted,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
              WHERE uid=@Uid AND version=@ExpectedVersion AND is_deleted=FALSE RETURNING version",
            parameters, Transaction, cancellationToken: cancellationToken));
        if (version is null) return false;
        profile.Version = version.Value;
        return true;
    }

    /// <summary>在主配置事务中保存字段差异；既有字段保持所属配置和目标键，异常行数由调用方回滚整份配置。</summary>
    public async Task SaveFieldAsync(ExcelReadField field, bool isNew, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (isNew) AuditStamp.StampForInsert(field);
        else AuditStamp.StampForUpdate(field);
        var sql = isNew
            ? $"INSERT INTO public.excel_read_field ({FieldInsertColumns}) VALUES ({FieldInsertParameters})"
            : @"UPDATE public.excel_read_field SET worksheet_name=@WorksheetName,cell_address=@CellAddress,
                is_downward=@IsDownward,is_deleted=@IsDeleted,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
                WHERE uid=@Uid AND profile_uid=@ProfileUid AND is_deleted=FALSE";
        // PostgreSQL 直接返回本条命令的影响行数；字段不引入独立版本协议，由已锁定的主版本覆盖。
        var changed = await Connection.ExecuteAsync(new CommandDefinition(sql,
            PostgreSqlParameters.Entity(field, FieldMetadata), Transaction, cancellationToken: cancellationToken));
        if (changed != 1) throw new InvalidOperationException("字段记录写入数量异常，必须回滚配置事务。");
    }

    /// <summary>阻止头行写入或更新锁脱离应用层事务，避免配置只生效一部分。</summary>
    private void RequireTransaction()
    {
        if (Transaction is null) throw new InvalidOperationException("地址配置写入必须处于应用层事务中。");
    }
}
