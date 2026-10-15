using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.Dictionaries;

/// <summary>提供用户明确触发的内置字典落库操作；启动、查询与普通保存不会调用此入口。</summary>
public interface IBuiltInDictionarySyncService
{
    /// <summary>在一个事务内补齐内置类型与默认值，保留已有身份和显示配置，并修复内置项的有效标记。</summary>
    Task<ServiceResult<BuiltInDictionarySyncResponse>> SyncAsync(CancellationToken cancellationToken = default);
}
