namespace SwiftExport.Core.Entities.Procurement;

/// <summary>采购版本、状态或引用集合已变化时的并发拒绝；Application 将其转换为版本冲突结果。</summary>
public sealed class ProcurementConcurrencyException(string message) : Exception(message);
