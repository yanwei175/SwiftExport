namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>保存采购写入的不可变负载；未知结果只能重发相同序列化请求。</summary>
public sealed partial record PendingProcurementCommand(Guid OrderId, ProcurementCommandKind Kind, string CommandJson, bool WasUnknown = false);
