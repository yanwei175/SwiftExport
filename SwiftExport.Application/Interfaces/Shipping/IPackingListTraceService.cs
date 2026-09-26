using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>只读查询箱单已持久化的执行证据，不调用库存端口。</summary>
public interface IPackingListTraceService
{
    /// <summary>按出货模块权限返回完整正反向执行记录；不具备证据的旧箱单明确提示。</summary>
    Task<ServiceResult<PackingListTraceDto>> GetAsync(Guid uid, CancellationToken token);
}
