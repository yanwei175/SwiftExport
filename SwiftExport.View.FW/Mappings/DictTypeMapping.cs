using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.Mappings
{
    /// <summary>
    /// SysDictTypeDto ↔ DictTypeModel 映射配置
    /// 用于字典类型的双向映射（左侧树形结构）
    /// </summary>
    public class DictTypeMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // DTO → Model（后端 API 返回数据 → UI 绑定）
            config.NewConfig<SysDictTypeDto, DictTypeModel>()
                // 基础字段
                .Map(dest => dest.Id, dto => dto.Id)
                .Map(dest => dest.DictCode, dto => dto.DictCode)
                .Map(dest => dest.DictName, dto => dto.DictName)
                .Map(dest => dest.IsSystem, dto => dto.IsSystem)
                .Map(dest => dest.Remark, dto => dto.Remark)
                // 树形结构
                .Map(dest => dest.ParentId, dto => dto.ParentId)
                .Map(dest => dest.SortOrder, dto => dto.SortOrder)
                // 视图控制和分组
                .Map(dest => dest.TargetViewKey, dto => dto.TargetViewKey)
                .Map(dest => dest.GroupCode, dto => dto.GroupCode)
                .Map(dest => dest.GroupName, dto => dto.GroupName)
                // UI 状态初始化
                .Map(dest => dest.State, dto => ModelState.Unchanged)
                .TwoWays();
        }
    }
}
