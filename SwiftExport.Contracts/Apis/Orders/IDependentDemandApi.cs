using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 销售订单拆解中间表 API。
    /// </summary>
    public interface IDependentDemandApi : IGenericApi<IndependentDemandDto>
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync([Body] DependentDemandSearchDto search);

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
        [Post("/convert-to-po/{sourceType}/{sourceNo}")]
        Task<ServiceResult<List<string>>> ConvertSourceToPurchaseOrdersAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 将指定需求池行按选中范围转为采购单。
        /// </summary>
        [Post("/convert-to-po/by-ids")]
        Task<ServiceResult<List<string>>> ConvertByDependentDemandIdsAsync([Body] List<Guid> requirementPoolIds);

        /// <summary>
        /// 一键将需求池中全部待采购需求转为采购单。
        /// </summary>
        [Post("/convert-to-po-all")]
        Task<ServiceResult<List<string>>> ConvertAllPendingToPurchaseOrdersAsync();

        /// <summary>
        /// 手工更新需求池行供应商信息。
        /// </summary>
        [Post("/{id}/set-supplier")]
        Task<ServiceResult<bool>> UpdateSupplierAsync(Guid id, [Query] string supplierCode, [Query] string supplierName = "");
    }
}
