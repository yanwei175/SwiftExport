using Dapper;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public class SalesOrderLineRepository(RepositoryBaseContext context)
    : DapperRepository<SalesOrderLine>(context), ISalesOrderLineRepository
{
    public async Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $@"SELECT * FROM SalesOrderLine {lockHint}
                     WHERE SalesOrderId = @OrderId AND IsDeleted = 0
                     ORDER BY LineNumber, Uid;";
        return (await Connection.QueryAsync<SalesOrderLine>(sql, new { OrderId = orderId }, transaction: Transaction)).ToList();
    }

    public override async Task<RepositoryWriteResult> AddAsync(SalesOrderLine entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false;
        if (entity.Uid == Guid.Empty) entity.Uid = GuidSequentialGenerator.NewGuid();
        if (entity.Version <= 0) entity.Version = 1;

        var columns = string.Join(", ", InsertProperties.Select(property => $"[{property}]"));
        var values = string.Join(", ", InsertProperties.Select(property => $"@{property}"));
        var rows = await Connection.ExecuteAsync(
            $"INSERT INTO SalesOrderLine ({columns}) VALUES ({values});",
            entity,
            transaction: Transaction);
        return rows == 1
            ? RepositoryWriteResult.Ok(entity.Uid, null, entity.Version, "新增销售订单行成功。")
            : RepositoryWriteResult.Fail(entity.Uid, null, "INSERT_FAILED", "新增销售订单行失败。");
    }

    public Task<int> StageLineNumbersAsync(Guid orderId, int finalLineCount)
        => Connection.ExecuteAsync(@"
            DECLARE @CurrentMax int =
                ISNULL((
                    SELECT MAX(LineNumber)
                    FROM dbo.SalesOrderLine
                    WHERE SalesOrderId = @OrderId AND IsDeleted = 0
                ), 0);
            DECLARE @Offset bigint =
                CONVERT(bigint, @CurrentMax) + CONVERT(bigint, @FinalLineCount) + 1;

            IF @Offset + CONVERT(bigint, @CurrentMax) > 2147483647
                THROW 52720, N'销售订单行号暂存超出 int 范围。', 1;

            UPDATE dbo.SalesOrderLine
            SET LineNumber = LineNumber + CONVERT(int, @Offset)
            WHERE SalesOrderId = @OrderId AND IsDeleted = 0;",
            new { OrderId = orderId, FinalLineCount = finalLineCount }, transaction: Transaction);

    public async Task<IReadOnlyList<RepositoryWriteResult>> AddWorkspaceLinesAsync(IEnumerable<SalesOrderLine> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines) results.Add(await AddAsync(line));
        return results;
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> UpdateWorkspaceLinesAsync(IEnumerable<SalesOrderLine> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines) results.Add(await UpdateAsync(line));
        return results;
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> DeleteWorkspaceLinesAsync(IEnumerable<SalesOrderDeletedLineDto> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines)
        {
            var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
                UPDATE SalesOrderLine
                SET IsDeleted = 1,
                    UpdatedDate = @Now,
                    UpdatedBy = @UserName,
                    Version = Version + 1
                OUTPUT INSERTED.Version
                WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0;",
                new
                {
                    line.Uid,
                    line.Version,
                    Now = DateTime.Now,
                    UserName = CurrentUser.UserName
                }, transaction: Transaction);
            results.Add(newVersion.HasValue
                ? RepositoryWriteResult.Ok(line.Uid, line.Version, newVersion.Value, "删除销售订单行成功。")
                : RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "删除销售订单行失败，数据已被其他用户修改。"));
        }
        return results;
    }

    public Task<int> DeleteByOrderIdAsync(Guid orderId)
        => Connection.ExecuteAsync(@"
            UPDATE SalesOrderLine
            SET IsDeleted = 1,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            WHERE SalesOrderId = @OrderId AND IsDeleted = 0;",
            new
            {
                OrderId = orderId,
                Now = DateTime.Now,
                UserName = CurrentUser.UserName
            }, transaction: Transaction);
}
