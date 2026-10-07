using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.QualityControl
{
    /// <summary>售后品质分类树专用接口，按站点根地址注册，不暴露绕过树校验的单条写入口。</summary>
    public interface IAfterSalesQualityCategoryApi
    {
        /// <summary>返回全部未软删分类，包括停用节点；父级和末级具有相同选择资格。</summary>
        [Get("/api/quality-control/after-sales/quality-categories/all")]
        Task<ServiceResult<List<AfterSalesQualityCategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>校验最终树并原子保存本批变更，失败时不保留部分写入。</summary>
        [Post("/api/quality-control/after-sales/quality-categories/batch-save")]
        Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>> BatchSaveAsync([Body] AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
