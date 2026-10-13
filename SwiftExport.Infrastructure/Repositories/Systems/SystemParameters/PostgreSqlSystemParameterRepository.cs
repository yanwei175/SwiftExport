using Dapper;
using SwiftExport.Core.Entities.Systems.SystemParameters;
using SwiftExport.Core.Interfaces.Systems.SystemParameters;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.SystemParameters;

/// <summary>共享 PostgreSQL 会话读写系统参数，不开启事务、不暴露通用删除入口。</summary>
public sealed class PostgreSqlSystemParameterRepository(RepositoryBaseContext context) : ISystemParameterRepository
{
    private static readonly EntityMetadata Metadata = EntityMetadataCache.GetMetadata(typeof(SystemParameter));

    /// <inheritdoc />
    public Task<SystemParameter?> FindAsync(string key, CancellationToken cancellationToken = default)
        => context.Session.Connection.QuerySingleOrDefaultAsync<SystemParameter>(new CommandDefinition("""
            SELECT uid AS "Uid", version AS "Version", is_deleted AS "IsDeleted",
                   created_date AS "CreatedDate", created_by AS "CreatedBy",
                   updated_date AS "UpdatedDate", updated_by AS "UpdatedBy",
                   parameter_key AS "ParameterKey", parameter_value AS "ParameterValue"
            FROM public.system_parameter WHERE parameter_key=@Key AND is_deleted=FALSE
            """, new { Key = key }, context.Session.Transaction, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public async Task<bool> InsertAsync(SystemParameter parameter, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        context.AuditStamp.StampForInsert(parameter);
        // 首次保存竞争只返回冲突，不把失败事务继续用于后续查询。
        return await context.Session.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.system_parameter
                (uid, version, is_deleted, created_date, created_by, updated_date, updated_by, parameter_key, parameter_value)
            VALUES (@Uid, @Version, @IsDeleted, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy, @ParameterKey, @ParameterValue)
            ON CONFLICT (parameter_key) DO NOTHING
            """, PostgreSqlParameters.Entity(parameter, Metadata), context.Session.Transaction, cancellationToken: cancellationToken)) == 1;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SystemParameter parameter, int expectedVersion, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        context.AuditStamp.StampForUpdate(parameter);
        var parameters = PostgreSqlParameters.Entity(parameter, Metadata);
        parameters.Add("ExpectedVersion", expectedVersion);
        var version = await context.Session.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            UPDATE public.system_parameter SET parameter_value=@ParameterValue,
                updated_date=@UpdatedDate, updated_by=@UpdatedBy, version=version+1
            WHERE parameter_key=@ParameterKey AND version=@ExpectedVersion AND is_deleted=FALSE RETURNING version
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        if (version is null) return false;
        parameter.Version = version.Value;
        return true;
    }

    /// <summary>只有拥有外层事务的参数保存用例可以调用写操作。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("参数写入必须位于应用层事务中。");
    }
}
