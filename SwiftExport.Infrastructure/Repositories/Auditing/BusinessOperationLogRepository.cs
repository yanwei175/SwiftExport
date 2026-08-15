using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Auditing;

public sealed class BusinessOperationLogRepository(RepositoryBaseContext context) : IBusinessOperationLogRepository
{
    private DbSession Session => context.Session;
    public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId)
        => Session.Connection.QuerySingleOrDefaultAsync<BusinessOperationLog>($"SELECT {Columns} FROM dbo.BusinessOperationLog b WITH(UPDLOCK,HOLDLOCK) WHERE b.Module=@Module AND b.OperationType=@OperationType AND b.CommandId=@CommandId AND ISNULL(b.IsDeleted,0)=0;", new { Module = module, OperationType = operationType, CommandId = commandId }, Session.Transaction);

    public async Task<int> InsertAsync(BusinessOperationLog log)
    {
        context.AuditStamp.StampForInsert(log);
        return await Session.Connection.ExecuteAsync("""
            INSERT dbo.BusinessOperationLog(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
              CorrelationId,CommandId,Module,AggregateType,AggregateUid,AggregateNoSnapshot,OperationType,OperationName,
              Reason,Summary,ChangedFieldsJson,RelatedObjectsJson,UserUid,UserNameSnapshot,OccurredDate)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
              @CorrelationId,@CommandId,@Module,@AggregateType,@AggregateUid,@AggregateNoSnapshot,@OperationType,@OperationName,
              @Reason,@Summary,@ChangedFieldsJson,@RelatedObjectsJson,@UserUid,@UserNameSnapshot,@OccurredDate);
            """, log, Session.Transaction);
    }

    public async Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Uid,OperationType,OperationName,Reason,Summary,ChangedFieldsJson,RelatedObjectsJson,
                   UserNameSnapshot AS UserName,OccurredDate,CorrelationId
            FROM dbo.BusinessOperationLog
            WHERE Module=@Module AND AggregateUid=@AggregateUid AND ISNULL(IsDeleted,0)=0
            ORDER BY OccurredDate,Uid;
            """;
        var rows = await Session.Connection.QueryAsync<BusinessOperationTraceItemDto>(new CommandDefinition(sql, new { Module = module, AggregateUid = aggregateUid }, Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }
    private const string Columns = "b.Uid,b.Version,b.IsDeleted,b.CreatedDate,b.CreatedBy,b.UpdatedDate,b.UpdatedBy,b.CorrelationId,b.CommandId,b.Module,b.AggregateType,b.AggregateUid,b.AggregateNoSnapshot,b.OperationType,b.OperationName,b.Reason,b.Summary,b.ChangedFieldsJson,b.RelatedObjectsJson,b.UserUid,b.UserNameSnapshot,b.OccurredDate";
}
