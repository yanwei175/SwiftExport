using System.Globalization;
using SwiftExportClient.Features.OrderTracking.Overview.Policies;
namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>只格式化摘要与读取状态，不计算交付、采购或整行收货业务结论。</summary>
public static class TrackingSummaryText
{
    /// <summary>读取失败、无权和未知绝不显示为已完成或零。</summary>
    public static string Read(TrackingSummaryLoadState state, bool allowed, string? availability, Func<string> readable)
    {
        if (!allowed || state == TrackingSummaryLoadState.Forbidden) return "无权";
        return state switch
        {
            TrackingSummaryLoadState.Loading => "加载中…", TrackingSummaryLoadState.Failed => "加载失败",
            TrackingSummaryLoadState.NotLoaded => "未加载", _ => availability switch
            { "Forbidden" => "无权", "Failed" => "加载失败", "Readable" => readable(), _ => "未知" }
        };
    }
    /// <summary>保留可信零与未知计数的区别。</summary>
    public static string Count(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "待核实";
    /// <summary>数量只在单条需求内显示，不跨行求和。</summary>
    public static string Qty(decimal? value) => value?.ToString("N2", CultureInfo.CurrentCulture) ?? "待核实";
    /// <summary>关注标签仅显示当前有权查看的码，不把空集合解释为全部正常。</summary>
    public static string Attention(TrackingSummaryLoadState state, IEnumerable<string>? codes, TrackingSummaryAccess access)
    {
        if (state != TrackingSummaryLoadState.Loaded) return state switch { TrackingSummaryLoadState.Failed => "加载失败", TrackingSummaryLoadState.Forbidden => "无权", TrackingSummaryLoadState.Loading => "加载中…", _ => "未加载" };
        var labels = (codes ?? []).Where(access.AttentionCodes.Contains).Distinct().Select(OrderTrackingAttentionPolicy.Label).ToArray();
        return labels.Length == 0 ? "无可见关注项" : string.Join("；", labels);
    }
    /// <summary>短关注标签保留首项与总项数，完整标签放在行提示。</summary>
    public static string BriefAttention(string text)
    { var labels = text.Split('；'); return labels.Length <= 1 ? text : $"{labels[0]} 等 {labels.Length} 项"; }
    /// <summary>正常零值不重复占位；未知或异常核实条数仍在短摘要显式出现。</summary>
    public static string ReviewSuffix(int? count) => count == 0 ? string.Empty : $" · 核实 {Count(count)}";
    /// <summary>保留响应时间与时区，明确它只是本次摘要读取时间。</summary>
    public static string Stamp(DateTime? date, DateTimeOffset? readAt) => readAt is null ? "尚无摘要读取时间"
        : $"业务日 {date:yyyy-MM-dd}；摘要读取 {readAt:yyyy-MM-dd HH:mm:ss zzz}（与基础查询独立）";
}
