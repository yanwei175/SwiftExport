using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.Mappings
{
    /// <summary>
    /// SysDictDataDto ↔ DictDataModel 映射配置
    /// 用于字典数据（明细）的双向映射
    /// </summary>
    public class DictDataMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            //// DTO → Model（后端 API 返回数据 → UI 绑定）
            //config.NewConfig<SysDictDataDto, DictDataModel>()
            //    // 基础字段
            //    .Map(dest => dest.Id, dto => dto.Id)
            //    .Map(dest => dest.DictCode, dto => dto.DictCode)
            //    .Map(dest => dest.DictValue, dto => dto.DictValue)
            //    .Map(dest => dest.DictLabel, dto => dto.DictLabel)
            //    .Map(dest => dest.SortOrder, dto => dto.SortOrder)
            //    .Map(dest => dest.IsSystem, dto => dto.IsSystem)
            //    .Map(dest => dest.IsDeleted, dto => dto.IsDeleted)
            //    // 样式和状态
            //    .Map(dest => dest.CssClass, dto => dto.CssClass)
            //    .Map(dest => dest.IsEnabled, dto => dto.IsEnabled)
            //    // 扩展字段
            //    .Map(dest => dest.Remark, dto => dto.Remark)
            //    .Map(dest => dest.ParentValue, dto => dto.ParentValue)
            //    // UI 状态初始化
            //    .Map(dest => dest.State, dto => ModelState.Unchanged)
            //    .TwoWays();
        }
    }
}
