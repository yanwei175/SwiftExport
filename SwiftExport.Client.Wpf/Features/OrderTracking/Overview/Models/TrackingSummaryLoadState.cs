namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>摘要请求本身的状态，与业务完成、未知或不适用状态分开。</summary>
public enum TrackingSummaryLoadState { NotLoaded, Loading, Loaded, Failed, Forbidden }
