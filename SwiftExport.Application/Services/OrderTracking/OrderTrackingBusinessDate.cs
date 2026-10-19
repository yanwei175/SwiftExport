namespace SwiftExport.Application.Services.OrderTracking;
/// <summary>每个外层查询取得一次中国标准时间业务日，避免数据库或客户端时区影响。</summary>
internal static class OrderTrackingBusinessDate
{
    /// <summary>返回无时区业务日，供本次全部查询参数复用。</summary>
    internal static DateTime Today()=>TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"China Standard Time").Date;
}
