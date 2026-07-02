using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 净需求控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DependentDemandController(IDependentDemandService service)
        : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<ServiceResult<IndependentDemandDto>> GetById(Guid id) => await service.GetByIdAsync(id);

        [HttpPost]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<Guid>> Create([FromBody] IndependentDemandDto dto) => await service.AddAsync(dto);

        [HttpPut("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Update(Guid id, [FromBody] IndependentDemandDto dto) => await service.UpdateAsync(dto);

        [HttpDelete("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

        [HttpPost("page")]
        public async Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
            => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

        [HttpPost("batch-save")]
        [PreventDuplicateRequest(WindowSeconds = 5)]
        public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<IndependentDemandDto> req) => await service.BatchSaveAsync(req);

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
        public async Task<ServiceResult<List<string>>> CoverToPoByIdList([FromBody] List<Guid> req)
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
        public async Task<ServiceResult<bool>> UpdateSupplierAsync(Guid id, [FromQuery] string supplierCode, [FromQuery] string supplierName = "")
        {
            return await service.UpdateSupplierAsync(id, supplierCode, supplierName);
        }




    }
}
