using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 销售订单拆解中间表控制器。
    /// </summary>
    public class RequirementPoolController(IRequirementPoolService service)
        : BaseController<RequirementPool, RequirementPoolDto>(service)
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        [HttpPost("search")]
        public async Task<ServiceResult<PagedResultDto<RequirementPoolDto>>> GetPagedListAsync([FromBody] RequirementPoolSearchDto search)
        {
            return await service.GetPagedListAsync(search);
        }

        /// <summary>
        /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
        /// </summary>
        [HttpPost("convert-to-po/{sourceNo}")]
        public async Task<ServiceResult<List<string>>> ConvertSourceToPurchaseOrdersAsync(string sourceNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceNo))
                    return ServiceResult<List<string>>.Fail("来源单号不能为空");

                var poNos = await service.ConvertSourceToPurchaseOrdersAsync(sourceNo);
                if (poNos == null || poNos.Count == 0)
                    return ServiceResult<List<string>>.Fail("未生成采购单，请检查是否存在待处理采购行或供应商未维护");

                return ServiceResult<List<string>>.Ok(poNos);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<string>>.Fail("转采购失败：" + ex.Message);
            }
        }




    }
}
