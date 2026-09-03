using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.MasterData
{
    /// <summary>独立采购主数据窄查询；新增候选与历史筛选分开，错误按 HTTP 状态处理，不解析消息文本。</summary>
    public interface IProcurementMasterDataQueryApi
    {
        /// <summary>分页读取客户新增候选；要求有效登录身份，不替代创建或确认命令的权威权限校验。</summary>
        [Post("/customers/candidates/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>> SearchCustomersAsync(
            [Body] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>分页读取供应商新增候选；要求有效登录身份，不替代创建或确认命令的权威权限校验。</summary>
        [Post("/suppliers/candidates/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(
            [Body] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>分页读取采购供货产品新增候选；要求有效登录身份，不替代创建或确认命令的权威权限校验。</summary>
        [Post("/products/candidates/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>> SearchProductsAsync(
            [Body] ProcurementProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>分页读取客户历史筛选项；要求有效登录身份，不替代业务命令的权威权限校验。</summary>
        [Post("/customers/history-filter/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>> SearchCustomerHistoryFiltersAsync(
            [Body] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>分页读取供应商历史筛选项；要求有效登录身份，不替代业务命令的权威权限校验。</summary>
        [Post("/suppliers/history-filter/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>> SearchSupplierHistoryFiltersAsync(
            [Body] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
