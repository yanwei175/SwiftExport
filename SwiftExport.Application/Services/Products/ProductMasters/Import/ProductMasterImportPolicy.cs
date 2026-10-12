using System.Globalization;
using System.Text.Json;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.ProductMasters.Import;

/// <summary>将输入转换为父级资料；空白合并、单位精度及引用匹配均显式表达，不参与子级同步。</summary>
internal static class ProductMasterImportPolicy
{
    /// <summary>只在有效引用及字段全部通过后提供可写结果，保留文件原行号。</summary>
    internal static ProductMasterImportPreviewRowDto Build(ProductMasterImportRowInput input, ProductMaster? existing,
        IReadOnlyDictionary<Guid, string?> paths, IReadOnlyList<Supplier> suppliers)
    {
        var current = existing?.Adapt<ProductMasterTransferData>();
        var data = current?.Adapt<ProductMasterTransferData>() ?? new();
        var row = new ProductMasterImportPreviewRowDto { SourceRowNumber = input.SourceRowNumber,
            ProductMasterId = existing?.Uid ?? Guid.Empty, ExpectedVersion = existing?.Version ?? 0,
            CurrentData = current!, ProposedData = data };
        data.MasterSku = existing?.MasterSku ?? Text(input.MasterSku)!;
        CheckText(input.MasterSku, 300, nameof(input.MasterSku));
        if (Text(input.MasterSku) is null) Issue(nameof(input.MasterSku), "Required", "主型号不能为空。");
        // 稀疏输入不是完整覆盖：未提供的字段保留已有资料，新记录沿用默认值。
        data.DefaultDescriptionZh = Merge(input.DefaultDescriptionZh, data.DefaultDescriptionZh, 2000, nameof(input.DefaultDescriptionZh));
        data.DefaultDescriptionEn = Merge(input.DefaultDescriptionEn, data.DefaultDescriptionEn, 2000, nameof(input.DefaultDescriptionEn));
        data.DefaultFeaturesZh = Merge(input.DefaultFeaturesZh, data.DefaultFeaturesZh, 500, nameof(input.DefaultFeaturesZh));
        data.DefaultFeaturesEn = Merge(input.DefaultFeaturesEn, data.DefaultFeaturesEn, 500, nameof(input.DefaultFeaturesEn));
        if (Text(input.DefaultPcsPerPackage) is { } pcs)
        {
            if (int.TryParse(pcs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
                data.DefaultPcsPerPackage = value;
            else Issue(nameof(input.DefaultPcsPerPackage), "InvalidNumber", "每箱装率必须为正整数。");
        }
        data.DefaultNetWeight = Measure(input.DefaultNetWeight, data.DefaultNetWeight, 4, nameof(input.DefaultNetWeight));
        data.DefaultGrossWeight = Measure(input.DefaultGrossWeight, data.DefaultGrossWeight, 4, nameof(input.DefaultGrossWeight));
        data.DefaultCbm = Measure(input.DefaultCbm, data.DefaultCbm, 6, nameof(input.DefaultCbm));
        if (Text(input.CategoryPath) is { } category)
        {
            if (category.Length > 16000) Issue(nameof(input.CategoryPath), "TooLong", "分类路径超过允许长度。");
            else
            {
                var normalized = ProductMasterReferencePaths.Normalize(category);
                var candidates = paths.Where(pair => pair.Value is not null && string.Equals(pair.Value, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length == 1) data.CategoryId = candidates[0].Key;
                else Issue(nameof(input.CategoryPath), "InvalidReference", "分类路径不存在或匹配不唯一。");
            }
        }
        if (Text(input.DefaultSupplierCode) is { } supplierCode)
        {
            CheckText(supplierCode, 300, nameof(input.DefaultSupplierCode));
            var candidates = suppliers.Where(item => !item.IsDeleted && string.Equals(item.SupplierCode.Trim(), supplierCode, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 1) data.DefaultSupplierId = candidates[0].Uid;
            else Issue(nameof(input.DefaultSupplierCode), "InvalidReference", "供应商编码不存在或匹配不唯一。");
        }
        if (data.CategoryId != Guid.Empty && (!paths.TryGetValue(data.CategoryId, out var path) || path is null))
            Issue(nameof(input.CategoryPath), "InvalidReference", "分类引用或层级已失效。");
        if (data.DefaultSupplierId != Guid.Empty && !suppliers.Any(item => item.Uid == data.DefaultSupplierId && !item.IsDeleted))
            Issue(nameof(input.DefaultSupplierCode), "InvalidReference", "默认供应商引用已失效。");
        row.Operation = row.Issues.Count > 0 ? "Invalid" : current is null ? "Create" : Same(current, data) ? "Unchanged" : "Update";
        row.CurrentCategoryPath = CategoryText(current?.CategoryId ?? Guid.Empty);
        row.ProposedCategoryPath = CategoryText(data.CategoryId);
        row.CurrentSupplierCode = SupplierText(current?.DefaultSupplierId ?? Guid.Empty);
        row.ProposedSupplierCode = SupplierText(data.DefaultSupplierId);
        return row;

        string CategoryText(Guid id) => id == Guid.Empty ? "未分类" : paths.TryGetValue(id, out var path) && path is not null ? path : "分类已失效";
        string SupplierText(Guid id) => id == Guid.Empty ? "未设置" : suppliers.FirstOrDefault(item => item.Uid == id && !item.IsDeleted)?.SupplierCode ?? "供应商已失效";
        void Issue(string key, string code, string message) => row.Issues.Add(new()
        { SourceRowNumber = input.SourceRowNumber, FieldKey = key, Code = code, Message = message });
        void CheckText(string? value, int maximum, string key)
        { if (value?.Trim().Length > maximum) Issue(key, "TooLong", $"字段长度超过 {maximum} 字符。"); }
        string Merge(string? value, string? previous, int maximum, string key)
        { CheckText(value, maximum, key); return Text(value) ?? previous!; }
        decimal? Measure(string? value, decimal? previous, int scale, string key)
        {
            if (Text(value) is not { } text) return previous;
            // 使用固定小数文化，不把千分位和另一种小数分隔符静默解释为不同数量。
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || number < 0 || number >= (scale == 4 ? 100_000_000_000_000m : 1_000_000_000_000m)
                || decimal.Round(number, scale) != number)
            { Issue(key, "InvalidNumber", $"数值须非负且符合 {scale} 位小数及数据库容量。"); return previous; }
            return number;
        }
    }

    /// <summary>按稳定字段值比较，不因展示名称或旧类型标签变化误判为待更新。</summary>
    internal static bool Same(ProductMasterTransferData left, ProductMasterTransferData right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    /// <summary>与当前父级维护保持相同空白归一化。</summary>
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
