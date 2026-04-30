using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 采购订单应用服务接口。
    /// </summary>
    public interface IPurchaseOrderService : IBaseService<PurchaseOrderHead, PurchaseOrderDto>
    {
        Task<ServiceResult<PagedResultDto<PurchaseOrderDto>>> GetPagedListAsync(PurchaseOrderSearchDto search);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(int id);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo);
        Task<ServiceResult<int>> SaveDetailAsync(PurchaseOrderDetailDto request);
    }
}
