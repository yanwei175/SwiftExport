using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services
{
    /// <summary>地址配置的 API 入口与变更通知，不缓存列表或详情，也不预热。</summary>
    public sealed class ExcelReadProfileService
    {
        private readonly IExcelReadProfileApi api;
        /// <summary>借用统一认证管线的 Refit API。</summary>
        public ExcelReadProfileService(IExcelReadProfileApi api) { this.api = api; }
        /// <summary>保存或删除成功后通知仍存活页面重新获取列表。</summary>
        public event EventHandler Changed;
        /// <summary>按分组按需获取配置列表。</summary>
        public Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync(CancellationToken token) => api.ListAsync("SalesOrder", token);
        /// <summary>获取完整配置快照。</summary>
        public Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken token) => api.GetAsync(uid, token);
        /// <summary>创建空配置，实际授权由服务器校验。</summary>
        public Task<ServiceResult<ExcelReadProfileDto>> CreateAsync(string name, CancellationToken token) => api.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = "SalesOrder", ConfigName = name }, token);
        /// <summary>以预期版本保存完整快照，失败时不发布通知。</summary>
        public async Task<ServiceResult<ExcelReadProfileDto>> SaveAsync(Guid uid, UpdateExcelReadProfileRequest request, CancellationToken token)
        { var result = await api.UpdateAsync(uid, request, token); if (result.Success && !token.IsCancellationRequested) Changed?.Invoke(this, EventArgs.Empty); return result; }
        /// <summary>以预期版本删除配置，失败时不发布通知。</summary>
        public async Task<ServiceResult<bool>> DeleteAsync(Guid uid, int version, CancellationToken token)
        { var result = await api.DeleteAsync(uid, version, token); if (result.Success && !token.IsCancellationRequested) Changed?.Invoke(this, EventArgs.Empty); return result; }
    }
}
