using System.Globalization;
using System.Text.RegularExpressions;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Services.Products.ProductMasters.Import;

/// <summary>一百行批次的父级保存用例；每行重读加锁，空白清空，不进入子产品同步链。</summary>
public sealed class ProductMasterImportService(IProductMasterRepository masters, IProductCategoryRepository categories,
    ISupplierRepository suppliers, ICurrentUser user, IPermissionEvaluationService permissions,
    IProductAttributeRepository attributes, ServiceBaseContext context) : IProductMasterImportService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<ProductMasterImportService>();

    /// <inheritdoc />
    public async Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductMasterImportRowDto> rows, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id || id == Guid.Empty)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unauthenticated, "请先登录。");
        var projection = await permissions.GetProjectionAsync(id, cancellationToken);
        if (projection is null) return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unauthenticated, "账号无效或已停用。");
        if (!projection.IsAdmin) return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Forbidden, "只有管理员可以提交父级型号导入。");
        if (rows is not { Count: > 0 and <= 100 } || rows.Any(row => row is null || row.SourceRowNumber is < 2 or > 1_048_576)
            || rows.Select(row => row.SourceRowNumber).Distinct().Count() != rows.Count)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Validation, "每批须为 1 至 100 行，原文件行号必须有效且不重复。");
        if (context.Session.Transaction is not null)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已有未完成事务。");
        var duplicates = rows.GroupBy(row => Text(row.MasterSku), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).SelectMany(group => group).Select(row => row.SourceRowNumber).ToHashSet();
        var response = new BatchSaveResponseBase { TotalCount = rows.Count };
        try
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                response.Items.Add(await SaveRowAsync(row, duplicates.Contains(row.SourceRowNumber), cancellationToken));
            }
            return ServiceResult<BatchSaveResponseBase>.Ok(response, $"成功 {response.SuccessCount} 行，失败 {response.FailedCount} 行。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "父级导入批次中断，之前完成的行可能已经提交");
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unexpected, "批次处理已中断，部分行可能已保存，请核对后重新导入。");
        }
    }

    /// <summary>只在此行校验通过后开启事务；引用先于主型号锁定，已知业务失败只回滚当前行。</summary>
    private async Task<SaveItemResult> SaveRowAsync(ProductMasterImportRowDto row, bool duplicate, CancellationToken token)
    {
        var receipt = new SaveItemResult { RowIndex = row.SourceRowNumber, Operation = SaveOperation.Create };
        try
        {
            var data = Parse(row);
            if (duplicate) throw new RowFailure(ServiceErrorCodes.Validation, "文件中主型号重复。");
            context.Session.Transaction = context.Session.Connection.BeginTransaction();
            // 分类维护与主型号详情同样先锁分类，防止导入建立已失效的层级引用。
            var tree = await categories.GetAllForUpdateAsync();
            var path = Text(row.CategoryPath);
            data.CategoryId = path is null ? Guid.Empty : ResolveCategory(tree, path);
            var supplierCode = Text(row.DefaultSupplierCode);
            if (supplierCode is not null)
            {
                var found = (await suppliers.GetBySupplierCodeListAsync([supplierCode])).Where(item => !item.IsDeleted).ToArray();
                if (found.Length != 1) throw new RowFailure(ServiceErrorCodes.Validation, "供应商编码不存在或匹配不唯一。");
                var locked = await suppliers.GetByIdAsync(found[0].Uid, true);
                if (locked is null || locked.IsDeleted || !string.Equals(locked.SupplierCode?.Trim(), supplierCode, StringComparison.OrdinalIgnoreCase))
                    throw new RowFailure(ServiceErrorCodes.Validation, "供应商编码已变化，请核对后重试。");
                data.DefaultSupplierId = locked.Uid;
            }
            var matches = await masters.GetByMasterSkusAsync([data.MasterSku], true, token);
            if (matches.Count > 1) throw new RowFailure(ServiceErrorCodes.Validation, "主型号编码匹配不唯一。");
            var existing = matches.SingleOrDefault();
            receipt.Operation = existing is null ? SaveOperation.Create : SaveOperation.Update;
            receipt.Uid = existing?.Uid;
            receipt.OldVersion = existing?.Version;
            if (existing is not null && existing.CategoryId != data.CategoryId
                && (await attributes.GetSelectionsAsync([existing.Uid], token)).Count > 0)
                throw new RowFailure(ServiceErrorCodes.BusinessBlocked, "主型号已有分类标签，请到详情调整分类并处理标签。");
            // 只覆盖十一字段，保留原编码、审计、旧类型标签和其他非导入字段。
            var entity = existing?.Adapt<ProductMaster>() ?? new ProductMaster();
            entity.MasterSku = existing?.MasterSku ?? data.MasterSku;
            entity.DefaultDescriptionZh = data.DefaultDescriptionZh;
            entity.DefaultDescriptionEn = data.DefaultDescriptionEn;
            entity.DefaultFeaturesZh = data.DefaultFeaturesZh;
            entity.DefaultFeaturesEn = data.DefaultFeaturesEn;
            entity.DefaultPcsPerPackage = data.DefaultPcsPerPackage;
            entity.DefaultNetWeight = data.DefaultNetWeight;
            entity.DefaultGrossWeight = data.DefaultGrossWeight;
            entity.DefaultCbm = data.DefaultCbm;
            entity.CategoryId = data.CategoryId;
            entity.DefaultSupplierId = data.DefaultSupplierId;
            token.ThrowIfCancellationRequested();
            var write = existing is null ? await masters.AddAsync(entity) : await masters.UpdateAsync(entity);
            if (!write.Success) throw new RowFailure(write.ErrorCode ?? ServiceErrorCodes.VersionConflict, write.Message ?? "保存失败，请核对后重试。");
            token.ThrowIfCancellationRequested();
            context.Session.Transaction.Commit();
            receipt.Success = true; receipt.Uid = write.Uid; receipt.NewVersion = write.NewVersion;
            return receipt;
        }
        catch (RowFailure failure)
        {
            context.Session.Transaction?.Rollback();
            receipt.ErrorCode = failure.Code; receipt.Message = failure.Message;
            return receipt;
        }
        catch
        {
            // 技术故障或取消不能假装是普通行失败；停止批次并保留客户端结果未知状态。
            try { context.Session.Transaction?.Rollback(); }
            catch (Exception failure) { _logger.LogError(failure, "父级导入行回滚失败"); }
            throw;
        }
        finally { context.Session.Transaction?.Dispose(); context.Session.Transaction = null; }
    }

    /// <summary>服务端独立重验原始文本，空白转 null；固定文化避免机器小数分隔符改变数值。</summary>
    private static ProductMaster Parse(ProductMasterImportRowDto row)
    {
        var errors = new List<string>();
        var data = new ProductMaster
        {
            MasterSku = Value(row.MasterSku, 300, "主型号")!,
            DefaultDescriptionZh = Value(row.DefaultDescriptionZh, 2000, "中文描述"),
            DefaultDescriptionEn = Value(row.DefaultDescriptionEn, 2000, "英文描述"),
            DefaultFeaturesZh = Value(row.DefaultFeaturesZh, 500, "中文配置"),
            DefaultFeaturesEn = Value(row.DefaultFeaturesEn, 500, "英文配置"),
            DefaultPcsPerPackage = Integer(row.DefaultPcsPerPackage),
            DefaultNetWeight = Measure(row.DefaultNetWeight, 4, "每箱净重"),
            DefaultGrossWeight = Measure(row.DefaultGrossWeight, 4, "每箱毛重"),
            DefaultCbm = Measure(row.DefaultCbm, 6, "每箱体积"),
            CategoryId = Guid.Empty, DefaultSupplierId = Guid.Empty
        };
        if (data.MasterSku is null) errors.Add("主型号不能为空。");
        Value(row.CategoryPath, 16000, "分类路径"); Value(row.DefaultSupplierCode, 300, "供应商编码");
        if (errors.Count > 0) throw new RowFailure(ServiceErrorCodes.Validation, string.Join("；", errors));
        return data;
        string? Value(string? raw, int length, string name)
        { var value = Text(raw); if (value?.Length > length) errors.Add($"{name}不能超过 {length} 字符"); return value; }
        int? Integer(string? raw)
        {
            if (Text(raw) is not { } value) return null;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0) return number;
            errors.Add("每箱装率须为正整数或空白"); return null;
        }
        decimal? Measure(string? raw, int scale, string name)
        {
            if (Text(raw) is not { } value) return null;
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0
                && number < (scale == 4 ? 100_000_000_000_000m : 1_000_000_000_000m) && decimal.Round(number, scale) == number) return number;
            errors.Add($"{name}须为非负数，最多 {scale} 位小数且不超数据库容量"); return null;
        }
    }

    /// <summary>匹配完整真实祖先路径；环、断链、同名歧义均拒绝，不自动创建分类。</summary>
    private static Guid ResolveCategory(IReadOnlyList<ProductCategory> tree, string path)
    {
        var nodes = tree.Where(item => !item.IsDeleted).ToDictionary(item => item.Uid);
        var found = new List<Guid>();
        foreach (var item in nodes.Values)
        {
            var parts = new List<string>(); var seen = new HashSet<Guid>(); var current = item;
            while (current is not null && seen.Add(current.Uid) && !string.IsNullOrWhiteSpace(current.CategoryName))
            {
                parts.Add(current.CategoryName.Trim());
                if (current.ParentId is null || current.ParentId == Guid.Empty)
                {
                    parts.Reverse();
                    if (string.Equals(Normalize(string.Join(" / ", parts)), Normalize(path), StringComparison.OrdinalIgnoreCase)) found.Add(item.Uid);
                    break;
                }
                nodes.TryGetValue(current.ParentId.Value, out current);
            }
        }
        return found.Count == 1 ? found[0] : throw new RowFailure(ServiceErrorCodes.Validation, "分类路径不存在、层级失效或匹配不唯一。");
    }
    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s*/\s*", " / ");
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    /// <summary>仅承载可安全作为行回执返回的业务错误。</summary>
    private sealed class RowFailure(string code, string message) : Exception(message)
    { internal string Code { get; } = code; }
}
