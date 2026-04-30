using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 销售订单拆解中间表 API。
    /// </summary>
    public interface IRequirementPoolApi : IGenericApi<RequirementPoolDto>
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<RequirementPoolDto>>> GetPagedListAsync([Body] RequirementPoolSearchDto search);

        /// <summary>
        /// 按销售单号触发拆解写入中间表。
        /// </summary>
        [Post("/decompose/{sourceSoNo}")]
        Task<ServiceResult<BatchResultDto>> DecomposeBySalesOrderAsync(string sourceSoNo);

        /// <summary>
        /// 按多个销售单号批量触发拆解。
        /// </summary>
        [Post("/decompose/batch")]
        Task<ServiceResult<BatchResultDto>> DecomposeBySalesOrdersAsync([Body] List<string> sourceSoNos);

        /// <summary>
        /// 将某个来源单据下的需求按供应商分组转为采购单。
        /// </summary>
        [Post("/convert-to-po/{sourceNo}")]
        Task<ServiceResult<List<string>>> ConvertSourceToPurchaseOrdersAsync(string sourceNo);
    }
}
