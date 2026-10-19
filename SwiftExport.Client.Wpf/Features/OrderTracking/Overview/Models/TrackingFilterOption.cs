namespace SwiftExportClient.Features.OrderTracking.Overview.Models;

/// <summary>保存总览筛选的稳定值与中文显示名称。</summary>
public sealed record TrackingFilterOption<T>(T Value, string DisplayText);
