using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.ForExcel
{
    /// <summary>装箱单导入只读检查，根地址 api/for-excel/packing-lists。</summary>
    public interface IForExcelPackingListApi
    {
        /// <summary>检查产品和输入，不创建草稿或预留交付额度。</summary>
        [Post("/check")]
        Task<ServiceResult<CheckPackingListImportResponse>> CheckAsync([Body] CheckPackingListImportRequest request, CancellationToken token = default(CancellationToken));
    }
}
