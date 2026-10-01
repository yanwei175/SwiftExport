// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>Excel 导入字段映射配置契约；客户端 BaseAddress 为 WebApi 根地址。</summary>
    public interface IExcelFieldMappingApi
    {
        /// <summary>读取该组映射，默认仅启用项。</summary>
        [Get("/api/excel-field-mappings/{groupCode}")]
        Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, [Query] bool includeDisabled = false, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>创建一条映射；要求字段映射维护权限。</summary>
        [Post("/api/excel-field-mappings")]
        Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync([Body] CreateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>更新映射；按版本拒绝并发覆盖。</summary>
        [Put("/api/excel-field-mappings/{uid}")]
        Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, [Body] UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>在一个事务中新增或更新当前分组提交的全部行；缺席行不删除。</summary>
        [Put("/api/excel-field-mappings/{groupCode}/batch")]
        Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(string groupCode, [Body] SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>软删除映射；版本由查询参数传入。</summary>
        [Delete("/api/excel-field-mappings/{uid}")]
        Task<ServiceResult<bool>> DeleteAsync(Guid uid, [Query] int expectedVersion, CancellationToken cancellationToken = default(CancellationToken));
    }
}
