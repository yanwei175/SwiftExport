using SwiftExport.Contracts.SysEnums;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
/// <summary>会话内冻结的反向命令，JSON 固定原负载，跨页面重试不能改原因或行集合。</summary>
public sealed record PendingProcurementReversal(Guid PurchaseOrderId, ProcurementReversalOperation Operation, int ExpectedVersion, string CommandJson);
