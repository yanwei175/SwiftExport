using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.ForExcel
{
    /// <summary>Excel 销售导入只读检查，配置客户端根地址为 api/for-excel/sales-orders。</summary>
    public interface IForExcelSalesOrderApi
    {
        /// <summary>批量匹配和检查已解析订单；不创建草稿或消费命令身份。</summary>
        [Post("/check")]
        Task<ServiceResult<CheckSalesOrderImportResponse>> CheckAsync([Body] CheckSalesOrderImportRequest request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
