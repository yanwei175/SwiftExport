using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Services.Products.ProductMasters.Import;

/// <summary>独立父级导入用例；只写主型号，不调用带主从同步的常规保存链。</summary>
public sealed class ProductMasterImportService(IProductMasterRepository masters, IProductCategoryRepository categories,
    ISupplierRepository suppliers, ICurrentUser currentUser, IPermissionEvaluationService permissions, IProductAttributeRepository attributes, ServiceBaseContext context) : IProductMasterImportService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<ProductMasterImportService>();

    /// <inheritdoc />
    public async Task<ServiceResult<ProductMasterImportPreviewResult>> PreviewAsync(ProductMasterImportPreviewRequest request, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(false, cancellationToken);
        if (denied is not null) return ServiceResult<ProductMasterImportPreviewResult>.Fail(denied.Value.Code, denied.Value.Message);
        if (!ValidRows(request?.Rows)) return ServiceResult<ProductMasterImportPreviewResult>.Fail(ServiceErrorCodes.Validation, "请提供 1 至 1000 行不重复且有效的原文件行号。");
        return ServiceResult<ProductMasterImportPreviewResult>.Ok(await PrepareAsync(request!.Rows, false, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductMasterImportResult>> ImportAsync(ProductMasterImportRequest request, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(true, cancellationToken);
        if (denied is not null) return ServiceResult<ProductMasterImportResult>.Fail(denied.Value.Code, denied.Value.Message);
        var inputs = request?.Rows?.Select(row => row?.Input!).ToList();
        if (!ValidRows(inputs) || request!.Rows.Any(row => row is null))
            return ServiceResult<ProductMasterImportResult>.Fail(ServiceErrorCodes.Validation, "请提供 1 至 1000 行完整预览依据。");
        var session = context.Session;
        if (session.Transaction is not null) return ServiceResult<ProductMasterImportResult>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已存在未完成事务。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Transaction = session.Connection.BeginTransaction();
            var prepared = await PrepareAsync(inputs!, true, cancellationToken);
            if (prepared.Rows.Any(row => row.Issues.Count > 0)) throw new ImportFailure(ServiceErrorCodes.Validation, "导入校验失败，请重新预览并修正错误。");
            var expected = request.Rows.ToDictionary(row => row.Input.SourceRowNumber);
            foreach (var row in prepared.Rows)
            {
                var basis = expected[row.SourceRowNumber];
                if (basis.ProductMasterId != row.ProductMasterId || basis.ExpectedVersion != row.ExpectedVersion
                    || basis.ExpectedCategoryId != row.ProposedData.CategoryId || basis.ExpectedSupplierId != row.ProposedData.DefaultSupplierId)
                    throw new ImportFailure(ServiceErrorCodes.VersionConflict, "主型号或引用匹配依据已变化，请重新预览。");
            }
            var result = new ProductMasterImportResult();
            // Prepare 已按分类→主型号锁定；导入仅保护关系，不增加标签导入字段或自动清空行为。
            var taggedIds = (await attributes.GetSelectionsAsync(prepared.Rows.Select(row => row.ProductMasterId).Where(id => id != Guid.Empty), cancellationToken))
                .Select(row => row.ProductMasterId).ToHashSet();
            foreach (var row in prepared.Rows.OrderBy(row => row.ProductMasterId).ThenBy(row => row.SourceRowNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var uid = row.ProductMasterId;
                var version = row.ExpectedVersion;
                if (row.Operation != "Unchanged")
                {
                    // 在既有实体副本上应用窄传输数据，保留旧类型标签和所有未纳入本模块的字段。
                    var original = uid == Guid.Empty ? null : await masters.GetByIdAsync(uid, true);
                    var entity = original?.Adapt<ProductMaster>() ?? new ProductMaster();
                    row.ProposedData.Adapt(entity);
                    if (original is not null && original.CategoryId != entity.CategoryId && taggedIds.Contains(original.Uid))
                        throw new ImportFailure(ServiceErrorCodes.BusinessBlocked, $"主型号 {original.MasterSku} 已有标签，请到主型号详情调整分类并明确处理标签。");
                    entity.Uid = uid == Guid.Empty ? entity.Uid : uid;
                    entity.Version = version == 0 ? 1 : version;
                    var write = row.Operation == "Create" ? await masters.AddAsync(entity) : await masters.UpdateAsync(entity);
                    if (!write.Success) throw new ImportFailure(ServiceErrorCodes.VersionConflict, "主型号编码或版本已变化，整个批次已回滚。");
                    uid = write.Uid;
                    version = write.NewVersion ?? entity.Version;
                }
                result.Rows.Add(new() { SourceRowNumber = row.SourceRowNumber, ProductMasterId = uid, Version = version, Operation = row.Operation });
            }
            cancellationToken.ThrowIfCancellationRequested();
            session.Transaction.Commit();
            return ServiceResult<ProductMasterImportResult>.Ok(result, "父级型号导入成功。");
        }
        catch (OperationCanceledException) { Rollback(); throw; }
        catch (ImportFailure failure) { Rollback(); return ServiceResult<ProductMasterImportResult>.Fail(failure.Code, failure.Message); }
        catch (Exception exception)
        {
            Rollback();
            _logger.LogError(exception, "父级型号导入失败，整批回滚");
            return ServiceResult<ProductMasterImportResult>.Fail(ServiceErrorCodes.Unexpected, "父级型号导入失败，整个批次已回滚。");
        }
        finally { session.Transaction?.Dispose(); session.Transaction = null; }
    }

    /// <summary>引用先于主型号加锁，重读编码匹配后构建全批拟保存资料。</summary>
    private async Task<ProductMasterImportPreviewResult> PrepareAsync(List<ProductMasterImportRowInput> inputs, bool locked, CancellationToken cancellationToken)
    {
        var tree = locked ? await categories.GetAllForUpdateAsync() : await categories.GetAllAsync();
        var paths = ProductMasterReferencePaths.Build(tree);
        var keys = inputs.Select(row => (row.SourceRowNumber, row.MasterSku?.Trim() ?? string.Empty)).ToArray();
        var matches = await masters.MatchForImportAsync(keys, cancellationToken);
        var supplierCodes = inputs.Where(row => !string.IsNullOrWhiteSpace(row.DefaultSupplierCode)).Select(row => row.DefaultSupplierCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var referencedSuppliers = await suppliers.GetBySupplierCodeListAsync(supplierCodes);
        var supplierIds = referencedSuppliers.Select(item => item.Uid).Concat(matches.Where(item => item.Existing is not null).Select(item => item.Existing!.DefaultSupplierId))
            .Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        var supplierRows = supplierIds.Length == 0 ? [] : await suppliers.GetByIdListAsync(supplierIds, locked);
        if (locked)
        {
            var masterIds = matches.Where(item => item.Existing is not null).Select(item => item.Existing!.Uid).Distinct().OrderBy(id => id).ToArray();
            if (masterIds.Length > 0) await masters.GetByIdListAsync(masterIds, true);
            matches = await masters.MatchForImportAsync(keys, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var byRow = matches.GroupBy(item => item.SourceRowNumber).ToDictionary(group => group.Key, group => group.ToArray());
        var result = new ProductMasterImportPreviewResult();
        foreach (var input in inputs.OrderBy(row => row.SourceRowNumber))
        {
            byRow.TryGetValue(input.SourceRowNumber, out var found);
            var row = ProductMasterImportPolicy.Build(input, found?.FirstOrDefault()?.Existing, paths, supplierRows);
            if (found is null || found.Length != 1 || found[0].DuplicateCode)
            {
                row.Issues.Add(new() { SourceRowNumber = input.SourceRowNumber, FieldKey = nameof(input.MasterSku), Code = "DuplicateCode", Message = "主型号编码重复或匹配不唯一。" });
                row.Operation = "Invalid";
            }
            result.Rows.Add(row);
        }
        return result;
    }

    /// <summary>只信任服务端活动账号投影，普通查看权限不能绕过管理员提交要求。</summary>
    private async Task<(string Code, string Message)?> AuthorizeAsync(bool write, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } id || id == Guid.Empty)
            return (ServiceErrorCodes.Unauthenticated, "请先登录。");
        var projection = await permissions.GetProjectionAsync(id, cancellationToken);
        if (projection is null) return (ServiceErrorCodes.Unauthenticated, "账号无效或已停用。");
        if (write) return projection.IsAdmin ? null : (ServiceErrorCodes.Forbidden, "只有管理员可以提交父级型号导入。");
        return projection.IsAdmin || projection.Permissions.Contains(PermissionKeys.ProductsMasterView, StringComparer.Ordinal)
            ? null : (ServiceErrorCodes.Forbidden, "缺少父级型号查看权限。");
    }

    /// <summary>在任何业务仓储读取之前限制批次与行号，防止模糊重复回执。</summary>
    private static bool ValidRows(List<ProductMasterImportRowInput>? rows) => rows is { Count: > 0 and <= 1000 }
        && rows.All(row => row is not null && row.SourceRowNumber is > 1 and <= 1_048_576)
        && rows.Select(row => row.SourceRowNumber).Distinct().Count() == rows.Count;

    /// <summary>回滚清理不能遮蔽原始业务错误。</summary>
    private void Rollback()
    { try { context.Session.Transaction?.Rollback(); } catch (Exception exception) { _logger.LogError(exception, "父级导入回滚失败"); } }

    private sealed class ImportFailure(string code, string message) : Exception(message)
    { internal string Code { get; } = code; }
}
