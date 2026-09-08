using Dapper;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;
/// <summary>按来源身份锁定结案所需事实；不更新来源或交付记录。</summary>
public sealed class SourceDemandClosureRepository(RepositoryBaseContext context) : ISourceDemandClosureRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<GrossDemand>> GetForUpdateAsync(string sourceType,Guid sourceUid)
        => (await context.Session.Connection.QueryAsync<GrossDemand>("""
            SELECT Uid,Version,SourceType,SourceUid,SourceLineId,SourceVersion,DemandQty,LifecycleStatus,IsDeleted
            FROM dbo.GrossDemand WITH(UPDLOCK,HOLDLOCK)
            WHERE SourceType=@sourceType AND SourceUid=@sourceUid AND IsDeleted=0 ORDER BY Uid
            """,new {sourceType,sourceUid},context.Session.Transaction)).ToList();
}
