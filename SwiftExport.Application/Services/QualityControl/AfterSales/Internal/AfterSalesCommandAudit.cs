using System.Text.Json;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Internal;

/// <summary>只读取售后审计快照并写业务审计，机器结果由公共命令回执保存。</summary>
internal sealed class AfterSalesCommandAudit(IAfterSalesDocumentRepository documents,
    IBusinessOperationLogRepository logs, ICurrentUser user)
{
    /// <summary>在调用方事务内取得头行前后事实，不自行管理事务。</summary>
    internal async Task<AfterSalesDetailDto?> SnapshotAsync(Guid uid)
    {
        var head = await documents.GetHeaderAsync(uid, true);
        if (head is null) return null;
        return new AfterSalesDetailDto
        {
            Header = head.Adapt<AfterSalesHeaderDto>(),
            Lines = (await documents.GetLinesAsync(uid, true)).Where(x => !x.IsDeleted)
                .Adapt<List<AfterSalesLineDto>>()
        };
    }

    /// <summary>与单据事实及回执在同一事务记录操作者、原因和前后状态。</summary>
    internal async Task WriteAsync(Guid uid, Guid commandId, string operation, string? reason,
        AfterSalesCommandResult result, AfterSalesDetailDto? before, AfterSalesDetailDto? after)
    {
        var affected = await logs.InsertAsync(new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId, Module = "AfterSales",
            AggregateType = "AfterSales", AggregateUid = uid, AggregateNoSnapshot = result.AfterSalesNo,
            OperationType = "Command", OperationName = operation, Reason = reason, Summary = "售后" + operation,
            ChangedFieldsJson = JsonSerializer.Serialize(new { Before = before, After = after }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { AfterSalesUid = uid }),
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
        });
        if (affected != 1) throw new InvalidOperationException("售后日志写入失败。");
    }
}
