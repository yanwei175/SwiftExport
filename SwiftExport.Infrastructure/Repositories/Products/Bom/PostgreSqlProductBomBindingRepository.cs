using Dapper;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.BomManagement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.BomManagement;

/// <summary>只更新 BOM 绑定及系统字段，保留产品主型号、启停和全部其他资料。</summary>
public sealed class PostgreSqlProductBomBindingRepository(RepositoryBaseContext context) : IProductBomBindingRepository
{
    /// <inheritdoc />
    public async Task<RepositoryWriteResult> BindAsync(Product candidate, int expectedVersion, CancellationToken cancellationToken)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("产品 BOM 绑定必须在业务事务内更新。");
        context.AuditStamp.StampForUpdate(candidate);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", candidate.Uid);
        parameters.Add("Version", expectedVersion);
        parameters.Add("BomNo", candidate.BomNo);
        parameters.Add("UpdatedBy", candidate.UpdatedBy);
        parameters.AddTimestamp("UpdatedDate", candidate.UpdatedDate);
        const string sql = """
            UPDATE public.product SET bom_no=@BomNo,bom_version=1,version=version+1,
                updated_by=@UpdatedBy,updated_date=CAST(@UpdatedDate AS timestamp without time zone)
            WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE
                AND NOT COALESCE((bom_version>0 AND bom_no IS NOT NULL AND btrim(bom_no,' ')<>''),FALSE)
            RETURNING version
            """;
        var version = await context.Session.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return version.HasValue
            ? RepositoryWriteResult.Ok(candidate.Uid, expectedVersion, version.Value, "BOM 绑定已保存。")
            : RepositoryWriteResult.Fail(candidate.Uid, expectedVersion, ServiceErrorCodes.VersionConflict, "产品已变化或已绑定 BOM，请刷新后重试。");
    }
}
