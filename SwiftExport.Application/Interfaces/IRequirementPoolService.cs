using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.RequirementPools;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 销售订单拆解中间表应用服务接口。
    /// </summary>
    public interface IRequirementPoolService : IBaseService<RequirementPool, RequirementPoolDto>
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        Task<ServiceResult<PagedResultDto<RequirementPoolDto>>> GetPagedListAsync(RequirementPoolSearchDto search);

        /// <summary>
        /// 按来源单据单号拆解并写入中间表。
        /// </summary>
        Task<ServiceResult<List<RequirementPool>>> ExecuteDecomposeToPoolAsync(DecomposeRequestDto source);
        /// <summary>
        /// 按来源单据单号批量拆解并写入中间表。
        /// </summary>
        Task<ServiceResult<List<RequirementPool>>> ExecuteDecomposeToPoolAsync(List<DecomposeRequestDto> sourceList);

        /// <summary>
        /// 根据来源单据判断是否允许回退审批。
        /// 规则：需求池中存在执行态（StatusFlag > 0）则不允许回退。
        /// </summary>
        Task<ServiceResult<bool>> CanRollbackBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按来源删除需求池记录（用于审批回退）。
        /// </summary>
        Task<ServiceResult<int>> RemoveBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
        /// </summary>
        /// <param name="sourceNo">来源单号</param>
        /// <returns>生成的采购单号列表</returns>
        Task<List<string>> ConvertSourceToPurchaseOrdersAsync(string sourceNo);

    }
}
