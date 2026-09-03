using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;
/// <summary>已确认依据专用读取，与草稿当前 BOM 上下文分开。</summary>
public interface IProcurementConfirmationQueryService
{
    /// <summary>取得当前确认批次历史，禁止按最新 BOM 重建。</summary>
    Task<ServiceResult<ProcurementConfirmationSnapshotDto>> GetSnapshotAsync(Guid uid, CancellationToken ct = default);
}
