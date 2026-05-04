using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 销售订单拆解依赖需求应用服务接口。
    /// </summary>
    public interface IDependentDemandService : IBaseService<DependentDemand, DependentDemandDto>
    {
        /// <summary>
        /// 分页查询拆解依赖需求。
        /// </summary>
        Task<ServiceResult<PagedResultDto<DependentDemandDto>>> GetPagedListAsync(DependentDemandSearchDto search);

        /// <summary>
        /// 按来源单据单号拆解BOM并写入依赖需求。
        /// </summary>
        Task<ServiceResult<List<DependentDemand>>> ExecuteDecomposeToPoolAsync(DecomposeRequestDto source);
        /// <summary>
        /// 按来源单据单号批量拆解并写入依赖需求。
        /// </summary>
        Task<ServiceResult<List<DependentDemand>>> ExecuteDecomposeToPoolAsync(List<DecomposeRequestDto> sourceList);

        /// <summary>
        /// 批量保存拆解后的依赖需求。
        /// </summary>
        /// <param name="demands">待保存依赖需求集合</param>
        /// <returns>保存结果</returns>
        Task<ServiceResult<int>> SaveToPoolAsync(List<DependentDemand> demands);

        /// <summary>
        /// 根据来源单据判断是否允许回退审批。
        /// 规则：需求池中存在执行态（StatusFlag != 待处理）则不允许回退。
        /// </summary>
        Task<ServiceResult<bool>> CanRollbackBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按来源删除需求池记录（用于审批回退）。
        /// </summary>
        Task<ServiceResult<int>> RemoveBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
        /// </summary>
        /// <param name="sourceType">来源类型</param>
        /// <param name="sourceNo">来源单号</param>
        /// <returns>生成的采购单号列表</returns>
        Task<List<string>> ConvertSourceToPurchaseOrdersAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按指定分组策略将需求池行转为采购单。
        /// </summary>
        /// <param name="request">转采购请求（支持按来源单号/供应商/指定需求池ID列表分组）</param>
        /// <returns>生成的采购单号列表</returns>
        Task<List<string>> ConvertToPurchaseOrdersAsync(DependentDemandConvertToPurchaseRequestDto request);

        /// <summary>
        /// 一键将需求池中待采购需求转为采购单（不限定来源单号）。
        /// </summary>
        /// <returns>生成的采购单号列表</returns>
        Task<List<string>> ConvertAllPendingToPurchaseOrdersAsync();

        /// <summary>
        /// 更新需求池行的手工指定供应商。
        /// </summary>
        /// <param name="id">需求池行ID</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="supplierName">供应商名称</param>
        /// <returns>是否成功</returns>
        Task<ServiceResult<bool>> UpdateSupplierAsync(int id, string supplierCode, string? supplierName);

    }
}
