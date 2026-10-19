using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Application.Services.OrderTracking;
/// <summary>两个分页用例共用来源、生命周期及按日日期校验。</summary>
internal static class OrderTrackingSearchValidation
{
    /// <summary>返回字段和原因，不修改请求或访问仓储。</summary>
    internal static (string Field,string Message)? Validate(IEnumerable<string>? sources,IEnumerable<GrossDemandLifecycleStatusEnum>? statuses,DateTime? from,DateTime? to)
    {
        if(sources?.Any(type=>type is not ("SO" or "CR" or "MD"))==true) return ("SourceTypes","来源类型仅支持 SO、CR、MD。");
        if(statuses?.Any(status=>!Enum.IsDefined(status))==true) return ("LifecycleStatuses","需求生命周期仅支持活动、已关闭和已作废。");
        if(from?.Date>to?.Date) return ("RequiredDateTo","开始交期不能晚于结束交期。");
        return null;
    }
}
