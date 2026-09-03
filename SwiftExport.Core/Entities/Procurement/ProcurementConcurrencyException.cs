namespace SwiftExport.Core.Entities.Procurement;

/// <summary>新采购持久化的乐观并发拒绝；Application 将其映射为稳定版本冲突。</summary>
public sealed class ProcurementConcurrencyException(string message) : Exception(message);
