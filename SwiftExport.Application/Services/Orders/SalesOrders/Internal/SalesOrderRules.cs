using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Services.Orders.SalesOrders.Internal;

/// <summary>销售命令共享的最小身份、状态和写入结果校验。</summary>
internal static class SalesOrderRules
{
    /// <summary>只允许活动且待确认的订单继续编辑或作为草稿删除。</summary>
    internal static void EnsureEditable(SalesOrderHeader header)
    {
        if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
            throw new SalesOrderValidationException("已关闭的销售订单不能编辑或删除。", ServiceErrorCodes.BusinessBlocked);
        if (header.ConfirmationStatus != ConfirmationStatusEnum.待确认)
            throw new SalesOrderValidationException("已确认的销售订单不能编辑或删除。", ServiceErrorCodes.BusinessBlocked);
    }

    /// <summary>禁止路由身份与请求目标分离。</summary>
    internal static void ValidateCommandIdentity(Guid routeUid, Guid bodyUid)
    {
        if (routeUid == Guid.Empty || bodyUid == Guid.Empty || routeUid != bodyUid)
            throw new SalesOrderValidationException("路由 Uid 与请求体 Uid 不一致。");
    }

    /// <summary>使用仓储结构化错误码映射写入失败，不解析中文消息。</summary>
    internal static void EnsureWriteSucceeded(RepositoryWriteResult result)
    {
        if (result.Success) return;
        var code = result.ErrorCode switch
        {
            "CONCURRENCY_CONFLICT" => ServiceErrorCodes.VersionConflict,
            ServiceErrorCodes.VersionConflict or ServiceErrorCodes.NotFound
                or ServiceErrorCodes.Validation or ServiceErrorCodes.BusinessBlocked => result.ErrorCode,
            _ => ServiceErrorCodes.BusinessBlocked
        };
        throw new SalesOrderValidationException(result.Message ?? "数据库写入失败。", code);
    }

    /// <summary>批量写入只要有一项失败，就让外层执行器回滚整单。</summary>
    internal static void EnsureAllWritesSucceeded(IEnumerable<RepositoryWriteResult> results)
    {
        var failure = results.FirstOrDefault(result => !result.Success);
        if (failure is not null) EnsureWriteSucceeded(failure);
    }
}

/// <summary>销售业务拒绝，携带稳定错误码交由命令边界返回。</summary>
internal sealed class SalesOrderValidationException(string message,
    string code = ServiceErrorCodes.Validation) : Exception(message)
{
    /// <summary>业务拒绝的稳定错误码。</summary>
    internal string Code { get; } = code;
}
