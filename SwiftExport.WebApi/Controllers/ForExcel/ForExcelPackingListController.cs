using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.ForExcel;
/// <summary>装箱单导入检查的 HTTP 边界，不读取 Excel 或写入单据。</summary>
[ApiController, Authorize, Route("api/for-excel/packing-lists")]
public sealed class ForExcelPackingListController(IForExcelPackingListService service) : ControllerBase
{
    /// <summary>返回各行匹配和校验反馈，草稿保存另行独立复核。</summary>
    [HttpPost("check")]
    public Task<ServiceResult<CheckPackingListImportResponse>> CheckAsync([FromBody] CheckPackingListImportRequest request, CancellationToken token) => service.CheckAsync(request, token);
}
