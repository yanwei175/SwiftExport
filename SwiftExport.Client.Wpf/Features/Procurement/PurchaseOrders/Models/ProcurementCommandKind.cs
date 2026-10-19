namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>区分需要冻结原负载的采购正向写入，不包含已有独立反向协议。</summary>
public enum ProcurementCommandKind { Confirm, Save, Create }
