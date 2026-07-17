using Refit;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>客户端 BaseAddress 为 api/inventory。</summary>
    public interface IInventoryApi
    {
        [Post("/balances/search")]
        Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync([Body] InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/ledger/search")]
        Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync([Body] InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/balances/{uid}/trace")]
        Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/opening-balances/preview")]
        Task<ServiceResult<System.Collections.Generic.List<InventoryPostingResultDto>>> PreviewOpeningBalancesAsync([Body] OpeningBalanceCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/opening-balances/post")]
        Task<ServiceResult<System.Collections.Generic.List<InventoryPostingResultDto>>> PostOpeningBalancesAsync([Body] OpeningBalanceCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}
