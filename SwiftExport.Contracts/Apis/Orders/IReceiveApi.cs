using Refit;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 采购入库 API。
    /// </summary>
    public interface IReceiveApi
    {
        /// <summary>
        /// 执行采购入库。
        /// </summary>
        [Post("/execute-purchase-receive")]
        Task<ServiceResult<string>> ExecutePurchaseReceiveAsync([Body] PurchaseReceiveExecuteRequestDto request);
    }
}
