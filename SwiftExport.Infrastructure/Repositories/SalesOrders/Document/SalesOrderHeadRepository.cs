using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

using SwiftExport.Core.Interfaces.SalesOrders.Document;
namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Document;

/// <summary>在调用方事务中持久化销售订单头及状态。</summary>
public sealed class SalesOrderHeadRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderHeader>(context), ISalesOrderHeadRepository
{
    public Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status)
        => UpdateStatusAsync(uid, version, "ConfirmationStatus = @Status", new { Status = status });

    /// <summary>按乐观并发版本关闭销售订单并保存关闭原因。</summary>
    public async Task<RepositoryWriteResult> CloseAsync(Guid uid, int version, string reason)
    {
        var now = DateTime.Now;
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
            UPDATE SalesOrderHeader
            SET DocumentStatus = @ClosedStatus,
                CloseReason = @Reason,
                ClosedDate = @Now,
                ClosedBy = @UserName,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0 AND DocumentStatus = @OpenStatus;",
            new
            {
                Uid = uid,
                Version = version,
                Reason = reason,
                Now = now,
                UserName = CurrentUser.UserName,
                OpenStatus = SalesOrderDocumentStatusEnum.Open,
                ClosedStatus = SalesOrderDocumentStatusEnum.Closed
            }, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "关闭销售订单");
    }

    public async Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
            UPDATE SalesOrderHeader
            SET IsDeleted = 1,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0
              AND DocumentStatus = @OpenStatus AND ConfirmationStatus = @PendingStatus;",
            new
            {
                Uid = uid,
                Version = version,
                Now = DateTime.Now,
                UserName = CurrentUser.UserName,
                OpenStatus = SalesOrderDocumentStatusEnum.Open,
                PendingStatus = ConfirmationStatusEnum.待确认
            }, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "删除销售订单草稿");
    }

    private async Task<RepositoryWriteResult> UpdateStatusAsync(Guid uid, int version, string statusSet, object statusParameter)
    {
        var parameters = new DynamicParameters(statusParameter);
        parameters.Add("Uid", uid);
        parameters.Add("Version", version);
        parameters.Add("Now", DateTime.Now);
        parameters.Add("UserName", CurrentUser.UserName);
        var sql = $@"
            UPDATE SalesOrderHeader
            SET {statusSet}, UpdatedDate = @Now, UpdatedBy = @UserName, Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0;";
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "更新销售订单状态");
    }

    private static RepositoryWriteResult ToWriteResult(Guid uid, int version, int? newVersion, string operation)
        => newVersion.HasValue
            ? RepositoryWriteResult.Ok(uid, version, newVersion.Value, $"{operation}成功。")
            : RepositoryWriteResult.Fail(uid, version, "CONCURRENCY_CONFLICT", $"{operation}失败，订单已被其他用户修改，请刷新后重试。");

}
