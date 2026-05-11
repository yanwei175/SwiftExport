using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 净需求控制器。
    /// </summary>
    public class DependentDemandController(IDependentDemandService service)
        : BaseController<DependentDemand, IndependentDemandDto>(service)
    {
        /// <summary>
        /// 分页查询净需求。
        /// </summary>
        [HttpPost("search")]
        public async Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync([FromBody] DependentDemandSearchDto search)
        {
            return await service.GetPagedListAsync(search);
        }

  
        /// <summary>
        /// 按选中需求池ID列表转采购。
        /// </summary>
        [HttpPost("convert-to-po/by-ids")]
        public async Task<ServiceResult<List<string>>> CoverToPoByIdList([FromBody] List<int> req)
        {
            return await service.根据指定净需求行转采购单Async(req);
        }

        /// <summary>
        /// 根据单据类型和单据号转采购单Async
        /// </summary>
        [HttpPost("convert-to-po/{sourceType}/{sourceNo}")]
        public async Task<ServiceResult<List<string>>> CoverToPoBySourceTypeAndSourceNo(string sourceType,string sourceNo)
        {
            sourceType = sourceType.Trim();
            sourceNo = sourceNo.Trim();

            if (string.IsNullOrWhiteSpace(sourceType))
                return ServiceResult<List<string>>.Fail("来源类型不能为空");

            if (string.IsNullOrWhiteSpace(sourceNo))
                return ServiceResult<List<string>>.Fail("来源单号不能为空");

            if (!DependSourceTypesEnum.SourceTypes.Any(x => x == sourceType))
            {
                return ServiceResult<List<string>>.Fail($"不支持的来源类型: {sourceType}");
            }

            return await service.根据单据类型和单据号转采购单Async(sourceType, sourceNo);
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
