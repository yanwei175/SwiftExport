using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.Database;

/// <summary>为查询夹具创建可按编码匹配的中性产品，并给物料填写自身型号；插入与回滚由调用方会话负责。</summary>
internal static class ProductNeutralReferenceTestData
{
    /// <summary>用指定原文编码创建可选匹配资料，将同一文本存入物料 LinkSku；不建立中性产品身份引用或同步主型号、颜色。</summary>
    internal static ProductNeutral Create(Product product, string code)
    {
        var neutral = new ProductNeutral
        {
            ProductCode = code,
            ProductMasterId = Guid.NewGuid(),
            ColorId = Guid.NewGuid()
        };
        product.LinkSku = code;
        return neutral;
    }
}
