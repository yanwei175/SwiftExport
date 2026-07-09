using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.DependentDemands;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 净需求Api
    /// </summary>
    public interface IDependentDemandApi : IGenericApi<IndependentDemandDto>
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<DependentDemandDto>>> GetPagedAsync([Body] DependentDemandSearchRequest request);

        /// <summary>
        /// 按销售单号触发拆解写入中间表。
        /// </summary>
        [Post("/decompose/{sourceSoNo}")]
        Task<ServiceResult<DependentDemandBatchSaveResponse>> DecomposeBySalesOrderAsync(string sourceSoNo);

        /// <summary>
        /// 按多个销售单号批量触发拆解。
        /// </summary>
        [Post("/decompose/batch")]
        Task<ServiceResult<DependentDemandBatchSaveResponse>> DecomposeBySalesOrdersAsync([Body] List<string> sourceSoNos);

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
