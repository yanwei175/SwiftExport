using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Application.Services.QualityControl.AfterSales.Policies;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.QualityControl;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Internal;

/// <summary>售后切片共用的最小状态校验、错误和结果构造。</summary>
internal static class AfterSalesRules
{
    /// <summary>在业务锁后检查原头版本。</summary>
    internal static void RequireVersion(AfterSalesHeader head, int version)
    {
        if (head.Version != version) Fail("VERSION_CONFLICT", "售后单已被其他操作更新，请刷新。");
    }

    /// <summary>只允许活动单据继续执行要求开放状态的动作。</summary>
    internal static void RequireOpen(AfterSalesHeader head)
    {
        if (head.DocumentStatus != DocumentStatusEnum.Open) Fail("BUSINESS_BLOCKED", "售后单已关闭或作废。");
    }

    /// <summary>维持原售后操作原因的必填与长度规则。</summary>
    internal static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            Fail("VALIDATION", "必须填写不超过2000字的操作原因。", "Reason");
    }

    /// <summary>将原业务拒绝转换为售后稳定错误码。</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void Fail(string code, string message, string? field = null)
        => throw new AfterSalesException(code, message, field);

    /// <summary>返回首次业务事实；回执重放标记只在响应边界设置。</summary>
    internal static AfterSalesCommandResult Result(AfterSalesHeader head, bool positive)
        => new() { Uid = head.Uid, AfterSalesNo = head.AfterSalesNo, Version = head.Version,
            DocumentStatus = head.DocumentStatus, ConfirmationStatus = head.ConfirmationStatus,
            Capabilities = AfterSalesPolicy.Capabilities(head, positive) };
}
