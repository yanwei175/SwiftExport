using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 销售订单拆解依赖需求应用服务接口。
    /// </summary>
    public interface IDependentDemandService : IBaseService<DependentDemand, IndependentDemandDto>
    {
        /// <summary>
        /// 分页查询拆解依赖需求。
        /// </summary>
        Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync(DependentDemandSearchDto search);

        /// <summary>
        /// 批量保存拆解后的依赖需求。
        /// </summary>
        /// <param name="demands">待保存依赖需求集合</param>
        /// <returns>保存结果</returns>
        Task<ServiceResult<int>> SaveToDependentDemandAsync(List<DependentDemand> demands);

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
        /// 根据单据类型和单据号转采购单。
        /// </summary>
        /// <param name="sourceType">来源类型</param>
        /// <param name="sourceNo">来源单号</param>
        /// <returns>生成的采购单号列表</returns>
        Task<ServiceResult<List<string>>> 根据单据类型和单据号转采购单Async(string sourceType, string sourceNo);

        /// <summary>
        /// 根据指定净需求行Id集合转采购单
        /// </summary>
        /// <param name="净需求行IdList">净需求行Id集合</param>
        /// <returns>生成的采购单号列表</returns>
        Task<ServiceResult<List<string>>> 根据指定净需求行转采购单Async(List<Guid> 净需求行IdList);

        /// <summary>
        /// 更新需求池行的手工指定供应商。
        /// </summary>
        /// <param name="id">需求池行ID</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="supplierName">供应商名称</param>
        /// <returns>是否成功</returns>
        Task<ServiceResult<bool>> UpdateSupplierAsync(Guid id, string supplierCode, string? supplierName);

    }
}
