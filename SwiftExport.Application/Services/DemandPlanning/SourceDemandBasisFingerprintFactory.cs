using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>为来源需求的实物要求生成稳定指纹，不把来源单头技术版本当作实物变化。</summary>
internal static class SourceDemandBasisFingerprintFactory
{
    /// <summary>根据交付客户、产品身份、业务快照、数量和交期生成稳定 SHA-256 指纹。</summary>
    public static string Create(GrossDemand demand)
    {
        var json = JsonSerializer.Serialize(new
        {
            demand.CustomerId, demand.ProductId, demand.IsNonStandard,
            Sku = Normalize(demand.SkuSnapshot), LinkSku = Normalize(demand.LinkSkuSnapshot),
            CustomerSku = Normalize(demand.CustomerSkuSnapshot), Description = Normalize(demand.DescriptionSnapshot),
            Color = Normalize(demand.ColorSnapshot), Specification = Normalize(demand.SpecificationSnapshot),
            ImagePath = Normalize(demand.ImagePathSnapshot), Unit = Normalize(demand.UnitSnapshot),
            demand.DemandQty, demand.RequiredDate, Remark = Normalize(demand.Remark)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
