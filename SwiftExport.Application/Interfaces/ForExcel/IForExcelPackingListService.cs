using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.ForExcel;
/// <summary>装箱导入的只读检查，不承担草稿保存或来源关联。</summary>
public interface IForExcelPackingListService
{
    /// <summary>检查客户、单头和各行产品匹配，成功响应仍可能包含阻断问题。</summary>
    Task<ServiceResult<CheckPackingListImportResponse>> CheckAsync(CheckPackingListImportRequest request, CancellationToken token = default);
}
