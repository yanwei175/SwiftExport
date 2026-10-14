using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.Database;

/// <summary>为物料查询夹具建立独立中性产品引用；只构造实体，插入与回滚仍由各测试自己的会话负责。</summary>
internal static class ProductNeutralReferenceTestData
{
    /// <summary>用调用方指定的原文编码创建中性产品并将其 Uid 写入物料；不推导或同步物料的主型号和颜色。使用重新分配 Uid 的新增仓储时，调用方插入后重新接纳该 Uid。</summary>
    internal static ProductNeutral Create(Product product, string code)
    {
        var neutral = new ProductNeutral
        {
            ProductCode = code,
            ProductMasterId = Guid.NewGuid(),
            ColorId = Guid.NewGuid()
        };
        product.ProductNeutralId = neutral.Uid;
        return neutral;
    }
}
