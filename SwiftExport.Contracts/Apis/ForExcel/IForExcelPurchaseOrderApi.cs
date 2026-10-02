using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.ForExcel
{
    /// <summary>采购导入只读检查，客户端根地址使用 api/for-excel/purchase-orders。</summary>
    public interface IForExcelPurchaseOrderApi
    {
        /// <summary>检查已解析采购数据；不创建单据或消费命令标识。</summary>
        [Post("/check")]
        Task<ServiceResult<CheckPurchaseOrderImportResponse>> CheckAsync([Body] CheckPurchaseOrderImportRequest request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
