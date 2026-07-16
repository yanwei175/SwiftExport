using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed class NetDemandPlanCommandRepository(RepositoryBaseContext context) : INetDemandPlanCommandRepository
{
    public Task<NetDemandPlanCommandRecord?> GetForUpdateAsync(Guid commandId, Guid originalAllocationId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<NetDemandPlanCommandRecord>(@"
SELECT [Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
       [CommandId],[OriginalAllocationId],[CommandType],[RequestFingerprint],[Outcome],
       [NewAllocationId],[NewAllocationVersion],[ResultMessage],[AffectedAllocationIds]
FROM [dbo].[NetDemandPlanCommandLog] WITH (UPDLOCK,HOLDLOCK)
WHERE [CommandId]=@CommandId AND [OriginalAllocationId]=@OriginalAllocationId AND [IsDeleted]=0;",
            new { CommandId = commandId, OriginalAllocationId = originalAllocationId },
            context.Session.Transaction);

    public async Task<RepositoryWriteResult> AddAsync(NetDemandPlanCommandRecord record)
    {
        context.AuditStamp.StampForInsert(record);
        if (record.Uid == Guid.Empty)
            return RepositoryWriteResult.Fail(Guid.Empty, null, "UID_REQUIRED", "命令日志必须预生成 Uid。");
        if (record.Version <= 0) record.Version = 1;
        const string sql = @"
INSERT INTO [dbo].[NetDemandPlanCommandLog]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [CommandId],[OriginalAllocationId],[CommandType],[RequestFingerprint],[Outcome],
 [NewAllocationId],[NewAllocationVersion],[ResultMessage],[AffectedAllocationIds])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @CommandId,@OriginalAllocationId,@CommandType,@RequestFingerprint,@Outcome,
 @NewAllocationId,@NewAllocationVersion,@ResultMessage,@AffectedAllocationIds);";
        var rows = await context.Session.Connection.ExecuteAsync(sql, record, context.Session.Transaction);
        return rows == 1
            ? RepositoryWriteResult.Ok(record.Uid, null, record.Version, "命令幂等结果已记录。")
            : RepositoryWriteResult.Fail(record.Uid, null, "INSERT_FAILED", "命令幂等结果写入失败。");
    }
}
