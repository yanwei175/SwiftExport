namespace SwiftExportClient.Features.OrderTracking.DemandDetail.Models;
/// <summary>按需求身份打开并定位区域；文档身份始终只使用成品需求ID。</summary>
public sealed partial record DemandTrackingOpenRequest(Guid GrossDemandId, DemandTrackingSection Section = DemandTrackingSection.Basic);
