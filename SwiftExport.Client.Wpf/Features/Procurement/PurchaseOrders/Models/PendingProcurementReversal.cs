using SwiftExport.Contracts.SysEnums;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
/// <summary>会话内冻结的反向命令，JSON 固定原负载；曾结果未知的请求不能因后续认证拒绝丢失恢复身份。</summary>
public sealed partial record PendingProcurementReversal(Guid PurchaseOrderId, ProcurementReversalOperation Operation, int ExpectedVersion, string CommandJson, bool WasUnknown = false);
