using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.Catalog.DefaultSupplier;

internal static class ProductDefaultSupplierPolicy
{
    public static Guid[] GetReferencedSupplierIds(IEnumerable<ProductDto> products) => products
        .Select(product => product.DefaultSupplierId)
        .Where(uid => uid.HasValue)
        .Select(uid => uid!.Value)
        .Distinct()
        .ToArray();

    public static string? Normalize(
        IEnumerable<ProductDto> products,
        IEnumerable<Supplier> activeSuppliers)
    {
        var items = products.ToArray();
        var suppliersById = activeSuppliers.ToDictionary(supplier => supplier.Uid);
        var missingIds = GetReferencedSupplierIds(items)
            .Except(suppliersById.Keys)
            .ToArray();
        if (missingIds.Length > 0)
        {
            return "默认供应商不存在或已失效：" + string.Join(", ", missingIds);
        }

        foreach (var item in items)
        {
            item.DefaultSupplierCode = item.DefaultSupplierId is Guid supplierId
                ? suppliersById[supplierId].SupplierCode
                : string.Empty;
        }

        return null;
    }
}
