using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Independent.Requests;
using SwiftExport.Contracts.DTOs.Independent.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 独立需求服务接口（流程编排层）。
/// </summary>
public partial interface IIndependentDemandServices : IBaseService<IndependentDemand, IndependentDemandDto>
{

    Task<ServiceResult<IReadOnlyList<IndependentDemandDto>>> GetAllAsync();

    Task<ServiceResult<IndependentDemandBatchSaveResponse>> BatchSaveAsync(IndependentDemandBatchSaveRequest batchReq);

    Task<ServiceResult<bool>> UpdateFilledQtyAsync(Guid id, decimal addQty);

    //获取分页数据
    Task<ServiceResult<PagedResponseBase<IndependentDemandDto>>> GetPagedAsync(IndependSearchRequest search);
    /// <summary>
    /// 按来源单据执行销售需求拆解（由毛需求驱动，拆解结果写入需求池）。
    /// </summary>
    /// <param name="sourceType">来源单据类型</param>
    /// <param name="sourceNo">来源单号</param>
    /// <returns>拆解后的需求池明细</returns>
    Task<ServiceResult<List<IndependentDemandDto>>> 根据来源单据拆解至净需求Async(string sourceType, string sourceNo);
    Task<ServiceResult<List<IndependentDemandDto>>> 根据选定毛需求拆解至净需求Async(List<Guid> ids);

    Task<ServiceResult<List<IndependentDemandDto>>> 批量回退草稿(List<Guid> ids);

}
