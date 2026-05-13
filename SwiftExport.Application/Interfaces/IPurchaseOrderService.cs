using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 采购订单应用服务接口。
    /// </summary>
    public interface IPurchaseOrderService : IBaseService<PurchaseOrderHead, PurchaseOrderHeadDto>
    {
        Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedListAsync(PurchaseOrderSearchDto search);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByHeadIdAsync(int id);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByBoNoAsync(string boNo);
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo);
        Task<ServiceResult<List<SupplierDto>>> GetAwaitingInboundSuppliersAsync();
        /// <summary>
        /// 保存一张采购订单的详情，包括订单头和订单行。根据订单头的ID来判断是新增还是更新，如果订单行中有ID为0的行则视为新增行，如果有ID不为0的行则视为更新行，如果有DeletedLineIds则视为删除行。
        /// </summary>
        /// <param name="request"></param>
        /// <returns>返回订单的ID</returns>
        Task<ServiceResult<int>> SaveDetailAsync(PurchaseOrderDetailDto request);

 




    }
}
