using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>共享 Excel 地址读取配置 API；实际 Excel 读取和 DTO 赋值由客户端执行。</summary>
    public interface IExcelReadProfileApi
    {
        /// <summary>按单据分组读取未删除配置摘要；已登录用户可读。</summary>
        [Get("/api/excel-read-profiles")]
        Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync([Query] string groupCode, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取主配置、字段和完整性提示。</summary>
        [Get("/api/excel-read-profiles/{uid}")]
        Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>创建空配置，要求字段映射维护权限。</summary>
        [Post("/api/excel-read-profiles")]
        Task<ServiceResult<ExcelReadProfileDto>> CreateAsync([Body] CreateExcelReadProfileRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按主版本原子替换完整配置，缺席字段明确清除。</summary>
        [Put("/api/excel-read-profiles/{uid}")]
        Task<ServiceResult<ExcelReadProfileDto>> UpdateAsync(Guid uid, [Body] UpdateExcelReadProfileRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按主版本同时软删除主配置和全部字段。</summary>
        [Delete("/api/excel-read-profiles/{uid}")]
        Task<ServiceResult<bool>> DeleteAsync(Guid uid, [Query] int expectedVersion, CancellationToken cancellationToken = default(CancellationToken));
    }
}

