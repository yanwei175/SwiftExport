using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;

namespace SwiftExport.Application.Services.Systems.ExcelReadProfiles;

/// <summary>集中转换持久化头行快照，并附加可读取结构检查，禁止返回实体。</summary>
internal static class ExcelReadProfileMapping
{
    /// <summary>头行直接关联：字段来自 ProfileUid 对应的未删除字段，不依赖其他业务表或当前名称。</summary>
    internal static ExcelReadProfileDto ToDto(ExcelReadProfile head, IReadOnlyList<ExcelReadField> fields)
    {
        var dto = head.Adapt<ExcelReadProfileDto>();
        dto.Fields = fields.Adapt<List<ExcelReadFieldDto>>();
        ExcelReadProfilePolicy.Evaluate(dto);
        return dto;
    }
}
