using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Mappings;

/// <summary>注册流量 DTO 与公共编辑模型映射；空日期仅在提交前由模型验证阻断。</summary>
public sealed class FlowMappings : IRegister
{
    /// <summary>配置头行读写映射，避免头模型携带页面行集合。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<FlowDetailDto, FlowHeaderModel>();
        config.NewConfig<FlowHeaderModel, FlowDetailDto>().Ignore(x => x.Lines)
            .Map(d => d.TestDate, s => s.TestDate ?? default);
        config.NewConfig<FlowHeaderModel, FlowEditDto>().Ignore(x => x.Lines)
            .Map(d => d.TestDate, s => s.TestDate ?? default);
        config.NewConfig<FlowLineDto, FlowLineModel>();
        config.NewConfig<FlowLineModel, FlowLineDto>();
        config.NewConfig<FlowLineModel, FlowLineEditDto>();
    }
}

