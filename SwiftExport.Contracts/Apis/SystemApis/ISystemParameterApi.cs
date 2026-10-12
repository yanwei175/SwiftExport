using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>共享系统参数接口；活动账号可读取，仅管理员可维护注册参数。</summary>
    public interface ISystemParameterApi
    {
        /// <summary>读取公开注册参数及其当前版本。</summary>
        [Get("/")]
        Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按预期版本保存一个参数，返回规范化后的权威值。</summary>
        [Put("/{key}")]
        Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, [Body] UpdateSystemParameterRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
