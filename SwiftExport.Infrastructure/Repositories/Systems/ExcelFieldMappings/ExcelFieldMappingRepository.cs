// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using Dapper;
using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.ExcelFieldMappings;

/// <summary>通过同一 DbSession 读取映射，单行写入复用带版本和审计的仓储实现。</summary>
public sealed class ExcelFieldMappingRepository(RepositoryBaseContext context)
    : DapperRepository<ExcelFieldMapping>(context), IExcelFieldMappingRepository
{
    private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, GroupCode, ExcelHeader, NormalizedHeader, TargetFieldKey, IsEnabled, Remark";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken)
    {
        var rows = await Connection.QueryAsync<ExcelFieldMapping>(new CommandDefinition(
            $@"SELECT {Columns} FROM dbo.ExcelFieldMapping
               WHERE GroupCode = @GroupCode AND IsDeleted = 0 AND (@IncludeDisabled = 1 OR IsEnabled = 1)
               ORDER BY NormalizedHeader, Uid",
            new { GroupCode = groupCode, IncludeDisabled = includeDisabled },
            Transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupForUpdateAsync(string groupCode, CancellationToken cancellationToken)
    {
        var rows = await Connection.QueryAsync<ExcelFieldMapping>(new CommandDefinition(
            $@"SELECT {Columns} FROM dbo.ExcelFieldMapping WITH (UPDLOCK, HOLDLOCK)
               WHERE GroupCode = @GroupCode AND IsDeleted = 0
               ORDER BY Uid",
            new { GroupCode = groupCode }, Transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseHeadersAsync(IReadOnlyList<Guid> uids, CancellationToken cancellationToken)
    {
        if (uids.Count == 0) return true;
        // 唯一索引排除空表头；事务提交前必须由应用服务写回最终表头。
        var changed = await Connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE dbo.ExcelFieldMapping SET NormalizedHeader = N''
              WHERE Uid IN @Uids AND IsDeleted = 0",
            new { Uids = uids }, Transaction, cancellationToken: cancellationToken));
        return changed == uids.Count;
    }

    /// <inheritdoc />
    public Task<ExcelFieldMapping?> GetByNormalizedHeaderAsync(string groupCode, string normalizedHeader, CancellationToken cancellationToken)
        => Connection.QuerySingleOrDefaultAsync<ExcelFieldMapping>(new CommandDefinition(
            $@"SELECT {Columns} FROM dbo.ExcelFieldMapping
               WHERE GroupCode = @GroupCode AND NormalizedHeader = @NormalizedHeader AND IsDeleted = 0",
            new { GroupCode = groupCode, NormalizedHeader = normalizedHeader },
            Transaction, cancellationToken: cancellationToken));
}
