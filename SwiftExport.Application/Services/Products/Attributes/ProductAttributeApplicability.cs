using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExport.Application.Services.Products.Attributes;

/// <summary>检查属性模板的最终关联是否仍覆盖对象选择；不读取数据库或修改关系。</summary>
public static class ProductAttributeApplicability
{
    /// <summary>统计解除模板关联后失去适用性的对象数；对象类型参与身份，缺失模板同样视为不适用。</summary>
    public static int CountUnavailableOwners(IEnumerable<ProductAttributeProfileGroupDto> relations,
        IEnumerable<ProductAttributeSelectionDto> references)
    {
        var applicable = relations.Select(row => (row.ProductAttributeProfileId, row.AttributeGroupId)).ToHashSet();
        return references.Where(row => !row.ProductAttributeProfileId.HasValue
                || !applicable.Contains((row.ProductAttributeProfileId.Value, row.AttributeGroupId)))
            .Select(row => (row.OwnerType, row.OwnerId)).Distinct().Count();
    }
}
