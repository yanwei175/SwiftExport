using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExportClient.Features.QualityControl.Categories.Models;

namespace SwiftExportClient.Features.QualityControl.Categories.Mappings;

/// <summary>售后分类专属映射，避免改变其他 Feature 的全局映射规则。</summary>
internal static class AfterSalesQualityCategoryMappings
{
    internal static readonly TypeAdapterConfig Config = CreateConfig();

    /// <summary>读取归一化可空文本；写入只传编辑字段并修剪输入。</summary>
    private static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<AfterSalesQualityCategoryDto, AfterSalesQualityCategoryNode>()
            .Ignore(d => d.Children)
            .Map(d => d.ParentId, s => s.ParentId == Guid.Empty ? null : s.ParentId)
            .Map(d => d.CategoryName, s => s.CategoryName ?? string.Empty)
            .Map(d => d.FakeName, s => s.FakeName ?? string.Empty)
            .Map(d => d.CategoryCode, s => s.CategoryCode ?? string.Empty);
        config.NewConfig<AfterSalesQualityCategoryNode, AfterSalesQualityCategoryDto>();
        config.NewConfig<AfterSalesQualityCategoryNode, AfterSalesQualityCategoryEditDto>()
            .Map(d => d.ParentId, s => s.ParentId == Guid.Empty ? null : s.ParentId)
            .Map(d => d.CategoryName, s => s.CategoryName.Trim())
            .Map(d => d.FakeName, s => s.FakeName.Trim())
            .Map(d => d.CategoryCode, s => s.CategoryCode.Trim());
        return config;
    }
}
