using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>在写命令事务内锁定最新反馈并追加业务报告；公共回执另行保存。</summary>
public interface ISupplierProgressReportRepository
{
    /// <summary>锁定采购行最新一条业务反馈，供累计值和序号校验。</summary>
    Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId);
    /// <summary>在调用方事务内追加供应商反馈，不覆盖历史。</summary>
    Task<int> InsertAsync(SupplierProgressReport report);
}
