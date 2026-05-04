using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 销售订单拆解中间表控制器。
    /// </summary>
    public class DependentDemandController(IDependentDemandService service)
        : BaseController<DependentDemand, DependentDemandDto>(service)
    {
        /// <summary>
        /// 分页查询拆解中间表。
        /// </summary>
        [HttpPost("search")]
        public async Task<ServiceResult<PagedResultDto<DependentDemandDto>>> GetPagedListAsync([FromBody] DependentDemandSearchDto search)
        {
            return await service.GetPagedListAsync(search);
        }

        /// <summary>
        /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
        /// </summary>
        [HttpPost("convert-to-po/{sourceType}/{sourceNo}")]
        public async Task<ServiceResult<List<string>>> ConvertSourceToPurchaseOrdersAsync(string sourceType, string sourceNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceType))
                    return ServiceResult<List<string>>.Fail("来源类型不能为空");

                if (string.IsNullOrWhiteSpace(sourceNo))
                    return ServiceResult<List<string>>.Fail("来源单号不能为空");

                var poNos = await service.ConvertSourceToPurchaseOrdersAsync(sourceType, sourceNo);
                if (poNos == null || poNos.Count == 0)
                    return ServiceResult<List<string>>.Fail("未生成采购单，请检查是否存在待处理采购行或供应商未维护");

                return ServiceResult<List<string>>.Ok(poNos);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<string>>.Fail("转采购失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 按选中需求池ID列表转采购。
        /// </summary>
        [HttpPost("convert-to-po/by-ids")]
        public async Task<ServiceResult<List<string>>> ConvertByDependentDemandIdsAsync([FromBody] List<int> requirementPoolIds)
        {
            try
            {
                var request = new DependentDemandConvertToPurchaseRequestDto
                {
                    GroupMode = DependentDemandConvertGroupModes.ByDependentDemandIds,
                    DependentDemandIds = requirementPoolIds ?? new List<int>()
                };

                var poNos = await service.ConvertToPurchaseOrdersAsync(request);
                if (poNos == null || poNos.Count == 0)
                    return ServiceResult<List<string>>.Fail("未生成采购单，请检查选中行状态和供应商维护");

                return ServiceResult<List<string>>.Ok(poNos);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<string>>.Fail("转采购失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 一键将需求池中全部待采购需求转为采购单。
        /// </summary>
        [HttpPost("convert-to-po-all")]
        public async Task<ServiceResult<List<string>>> ConvertAllPendingToPurchaseOrdersAsync()
        {
            try
            {
                var poNos = await service.ConvertAllPendingToPurchaseOrdersAsync();
                if (poNos == null || poNos.Count == 0)
                    return ServiceResult<List<string>>.Fail("未生成采购单，请检查需求池是否存在待处理采购行或供应商未维护");

                return ServiceResult<List<string>>.Ok(poNos);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<string>>.Fail("一键转采购失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 通用转采购（支持按来源单号、按供应商、按选中需求池行）。
        /// </summary>
        [HttpPost("convert-to-po")]
        public async Task<ServiceResult<List<string>>> ConvertToPurchaseOrdersAsync([FromBody] DependentDemandConvertToPurchaseRequestDto request)
        {
            try
            {
                var poNos = await service.ConvertToPurchaseOrdersAsync(request);
                if (poNos == null || poNos.Count == 0)
                    return ServiceResult<List<string>>.Fail("未生成采购单，请检查分组条件、待处理状态和供应商维护");

                return ServiceResult<List<string>>.Ok(poNos);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<string>>.Fail("转采购失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 手工指定需求池行供应商并持久化。
        /// </summary>
        [HttpPost("{id}/set-supplier")]
        public async Task<ServiceResult<bool>> UpdateSupplierAsync(int id, [FromQuery] string supplierCode, [FromQuery] string supplierName = "")
        {
            return await service.UpdateSupplierAsync(id, supplierCode, supplierName);
        }




    }
}
