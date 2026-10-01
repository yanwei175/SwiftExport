using Dapper;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;

/// <summary>使用共享 DbSession 持久化地址配置；不创建事务，不提供独立字段 API。</summary>
public sealed class ExcelReadProfileRepository(RepositoryBaseContext context)
    : DapperRepository<ExcelReadProfile>(context), IExcelReadProfileRepository
{
    private const string ProfileColumns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, GroupCode, ConfigName, EndMode, LastRowColumn, SummaryCellAddress, Remark";
    private const string FieldColumns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, ProfileUid, TargetFieldKey, WorksheetName, CellAddress, IsDownward";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExcelReadProfile>> ListAsync(string groupCode, CancellationToken cancellationToken)
        => (await Connection.QueryAsync<ExcelReadProfile>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM dbo.ExcelReadProfile WHERE GroupCode=@GroupCode AND IsDeleted=0 ORDER BY ConfigName, Uid",
            new { GroupCode = groupCode }, Transaction, cancellationToken: cancellationToken))).AsList();

    /// <inheritdoc />
    public Task<ExcelReadProfile?> FindAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate) RequireTransaction();
        var hint = forUpdate ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return Connection.QuerySingleOrDefaultAsync<ExcelReadProfile>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM dbo.ExcelReadProfile {hint} WHERE Uid=@Uid AND IsDeleted=0",
            new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExcelReadField>> GetFieldsAsync(Guid uid, CancellationToken cancellationToken)
        => (await Connection.QueryAsync<ExcelReadField>(new CommandDefinition(
            $"SELECT {FieldColumns} FROM dbo.ExcelReadField WHERE ProfileUid=@Uid AND IsDeleted=0 ORDER BY TargetFieldKey, Uid",
            new { Uid = uid }, Transaction, cancellationToken: cancellationToken))).AsList();

    /// <inheritdoc />
    public async Task InsertAsync(ExcelReadProfile profile, CancellationToken cancellationToken)
    {
        RequireTransaction();
        AuditStamp.StampForInsert(profile);
        await Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO dbo.ExcelReadProfile ({ProfileColumns}) VALUES (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@GroupCode,@ConfigName,@EndMode,@LastRowColumn,@SummaryCellAddress,@Remark)",
            profile, Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(ExcelReadProfile profile, int expectedVersion, CancellationToken cancellationToken)
    {
        RequireTransaction();
        AuditStamp.StampForUpdate(profile);
        var parameters = new DynamicParameters(profile);
        parameters.Add("ExpectedVersion", expectedVersion);
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            @"UPDATE dbo.ExcelReadProfile SET ConfigName=@ConfigName,EndMode=@EndMode,LastRowColumn=@LastRowColumn,
                SummaryCellAddress=@SummaryCellAddress,Remark=@Remark,IsDeleted=@IsDeleted,
                UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
              OUTPUT INSERTED.Version WHERE Uid=@Uid AND Version=@ExpectedVersion AND IsDeleted=0",
            parameters, Transaction, cancellationToken: cancellationToken));
        if (version is null) return false;
        profile.Version = version.Value;
        return true;
    }

    /// <inheritdoc />
    public async Task SaveFieldAsync(ExcelReadField field, bool isNew, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (isNew) AuditStamp.StampForInsert(field);
        else AuditStamp.StampForUpdate(field);
        var sql = isNew
            ? $"INSERT INTO dbo.ExcelReadField ({FieldColumns}) VALUES (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@ProfileUid,@TargetFieldKey,@WorksheetName,@CellAddress,@IsDownward)"
            : @"UPDATE dbo.ExcelReadField SET WorksheetName=@WorksheetName,CellAddress=@CellAddress,
                IsDownward=@IsDownward,IsDeleted=@IsDeleted,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
                WHERE Uid=@Uid AND ProfileUid=@ProfileUid AND IsDeleted=0";
        // 显式取 @@ROWCOUNT，避免连接启用 NOCOUNT 时 ExecuteAsync 返回 -1 而误判写入失败。
        var changed = await Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql + "; SELECT @@ROWCOUNT;", field, Transaction, cancellationToken: cancellationToken));
        if (changed != 1) throw new InvalidOperationException("字段记录写入数量异常，必须回滚配置事务。");
    }

    /// <summary>阻止头行写入脱离应用层事务，避免一半配置已经生效。</summary>
    private void RequireTransaction()
    {
        if (Transaction is null) throw new InvalidOperationException("地址配置写入必须处于应用层事务中。");
    }
}
