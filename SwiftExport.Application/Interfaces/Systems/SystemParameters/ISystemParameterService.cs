using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.SystemParameters;

/// <summary>注册系统参数的读取和版本保存用例，活动用户可读，仅管理员可写。</summary>
public interface ISystemParameterService
{
    /// <summary>读取注册参数，包括尚未配置的定义。</summary>
    Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>校验并规范化参数值，按预期版本原子保存。</summary>
    Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, UpdateSystemParameterRequest request, CancellationToken cancellationToken = default);
}
