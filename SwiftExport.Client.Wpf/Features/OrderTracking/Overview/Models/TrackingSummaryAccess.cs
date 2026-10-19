namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>当前会话允许展示的摘要维度与关注码，不改变明细原有读取权限。</summary>
public sealed partial record TrackingSummaryAccess(bool Delivery, bool Material, bool Purchase, bool Receipt, IReadOnlyList<string> AttentionCodes)
{
    /// <summary>尚未授权或已失效的摘要视图不显示任何敏感维度。</summary>
    public static TrackingSummaryAccess None { get; } = new(false, false, false, false, []);
}
