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
            // DTO → Model（后端 API 返回数据 → UI 绑定）
            config.NewConfig<SysDictDataDto, DictDataModel>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.ChangeTime, src => src.ChangeTime)
                .Map(dest => dest.DictCode, src => src.DictCode)
                .Map(dest => dest.DataNameEn, src => src.DataNameEn)
                .Map(dest => dest.DataNameZh, src => src.DataNameZh)
                .Map(dest => dest.Remark, src => src.Remark)
                .Map(dest => dest.SortOrder, src => src.SortOrder)
                .Map(dest => dest.CssClass, src => src.CssClass)
                .Map(dest => dest.IsSystem, src => src.IsSystem)
                .Map(dest => dest.IsEnabled, src => src.IsEnabled)
                .Map(dest => dest.State, src => ModelState.Unchanged)
                .TwoWays();
        }
    }
}
