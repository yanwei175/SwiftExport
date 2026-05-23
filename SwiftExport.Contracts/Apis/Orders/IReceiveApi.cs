using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 采购入库 API。
    /// </summary>
    public interface IReceiveApi 
    {
        /// <summary>
        /// 分页查询采购订单头。
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<ReceiveHeadDto>>> GetPagedListAsync([Body] ReceiveOrderSearchDto search);

        /// <summary>
        /// 按供应商与物料类型获取待入库候选行。
        /// </summary>
        [Get("/awating-inbound-lines")]
        Task<ServiceResult<IReadOnlyList<AwatingInboundDto>>> GetAwatingInboundLinesAsync([Query] string supplierCode, [Query] string materialType = null);
        
        [Get("/lines-by-head-id")]
        Task<ServiceResult<IReadOnlyList<ReceiveLineDto>>> GetLinesByHeadIdAsync([Query] int receiveHeadId);


        /// <summary>
        /// 执行采购入库。
        /// </summary>
        [Post("/execute-purchase-receive")]
        Task<ServiceResult<string>> ExecutePurchaseReceiveAsync([Body] PurchaseReceiveExecuteRequestDto request);

        /// <summary>
        /// 保存采购入库单据。
        /// </summary>
        [Post("/save-receive-detail")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto> request);

        [Post("/post-receive-by-head-id-list")]
        Task<ServiceResult<List<PostReceiveResultDto>>> PostReceiveByHeadIdListAsync([Body] List<int> ids);

        [Get("/head-by-id")]
        Task<ServiceResult<ReceiveHeadDto>> GetHeadByIdAsync([Query] int finalResult);
    }
}
