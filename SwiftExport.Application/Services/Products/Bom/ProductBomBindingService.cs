using System.Data;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.BomManagement;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.BomManagement;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Services.Products.BomManagement;

/// <summary>拥有产品 BOM 绑定事务，使用当前完整实体验证候选图，并通过窄仓储保护其他产品字段。</summary>
public sealed class ProductBomBindingService(IProductRepository products, IBomRepository boms,
    IProductBomBindingRepository writer, ICurrentUser currentUser, IPermissionEvaluationService permissions,
    ServiceBaseContext context) : IProductBomBindingService
{
    private readonly ILogger<ProductBomBindingService> _logger = context.LoggerFactory.CreateLogger<ProductBomBindingService>();

    /// <inheritdoc />
    public async Task<ServiceResult<SaveItemResult>> BindAsync(Guid productId, ProductBomBindingRequest request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            return Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        if (!await permissions.HasPermissionAsync(userId, PermissionKeys.ProductsProductEdit, cancellationToken)
            || !await permissions.HasPermissionAsync(userId, PermissionKeys.ProductsBomView, cancellationToken))
            return Fail(ServiceErrorCodes.Forbidden, "建立或绑定 BOM 需要产品编辑和 BOM 查看权限。");
        if (productId == Guid.Empty || request is null || request.ExpectedVersion <= 0
            || string.IsNullOrWhiteSpace(request.BomNo) || request.BomNo.Length > 200)
            return Fail(ServiceErrorCodes.Validation, "产品身份、版本或 BOM 编号无效。");
        var session = context.Session;
        if (session.Transaction is not null) return Fail(ServiceErrorCodes.BusinessBlocked, "当前请求存在未完成事务。");
        IDbTransaction? transaction = null;
        var committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
            session.Transaction = transaction;
            var product = await products.GetByIdAsync(productId, cancellationToken);
            if (product is null || product.IsDeleted) return Fail(ServiceErrorCodes.NotFound, "产品不存在或已删除。");
            if (product.Version != request.ExpectedVersion) return Fail(ServiceErrorCodes.VersionConflict, "产品已被修改，请刷新后重试。");
            var parentError = BomParentPolicy.ValidateParent(product) ?? BomParentPolicy.ValidateBinding(product);
            if (parentError is not null) return Fail(ServiceErrorCodes.BusinessBlocked, parentError);
            if (product.BomVersion > 0 && !string.IsNullOrWhiteSpace(product.BomNo))
                return Fail(ServiceErrorCodes.BusinessBlocked, "产品已有有效 BOM，不能更换或解除绑定。");
            var facts = await boms.GetSharedSaveFactsAsync([request.BomNo.Trim()], withUpdLock: false, cancellationToken);
            var group = facts.SingleOrDefault(item => item.InputIndexes.Contains(0));
            if (group is null) return Fail(ServiceErrorCodes.BusinessBlocked, "无法确认编号归属，请刷新后重试。");
            if (!request.BindExisting && (group.Products.Count > 0 || group.Lines.Count > 0))
                return Fail(ServiceErrorCodes.BusinessBlocked, "编号已有绑定或组成，请使用绑定已有 BOM，或更换新编号。");
            if (request.BindExisting && (group.Products.Count == 0 || group.Lines.Count == 0))
                return Fail(ServiceErrorCodes.BusinessBlocked, "绑定目标必须具有当前有效产品和组成。");
            var candidate = product.Adapt<Product>();
            candidate.BomNo = request.BomNo.Trim();
            candidate.BomVersion = 1;
            var affected = new HashSet<long>();
            var error = ProductMaintenanceService.ValidateNewBinding(candidate, group, affected);
            if (error is not null) return Fail(ServiceErrorCodes.BusinessBlocked, error);
            if (affected.Count > 0)
            {
                var finalGroups = facts.Select(item => new SharedBomGroup
                {
                    GroupKey = item.GroupKey, BomNo = item.BomNo, InputIndexes = item.InputIndexes, Lines = item.Lines,
                    Products = item.GroupKey == group.GroupKey ? item.Products.Concat([candidate]).ToArray() : item.Products
                }).ToArray();
                var candidates = finalGroups.SelectMany(item => item.Products).DistinctBy(item => item.Uid).ToDictionary(item => item.Uid);
                var componentIds = finalGroups.SelectMany(item => item.Lines).Where(item => item.ComponentProductId.HasValue)
                    .Select(item => item.ComponentProductId!.Value).Where(id => !candidates.ContainsKey(id)).Distinct().ToArray();
                foreach (var child in await products.GetByIdListAsync(componentIds, cancellationToken)) candidates[child.Uid] = child;
                error = new SharedBomStructureValidator().Validate(finalGroups, affected, candidates);
                if (error is not null) return Fail(ServiceErrorCodes.BusinessBlocked, error);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var write = await writer.BindAsync(candidate, request.ExpectedVersion, cancellationToken);
            if (!write.Success) return Fail(write.ErrorCode, write.Message);
            // 提交开始后不以查询取消信号判断结果；明确序列化失败可重做，其余提交异常必须核实。
            try { transaction.Commit(); committed = true; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "产品 BOM 绑定提交失败。");
                return session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure
                    ? Fail(ServiceErrorCodes.ServiceUnavailable, "并发结构变化导致绑定未提交，请刷新后重试。")
                    : Fail(ServiceErrorCodes.OutcomeUnknown, "BOM 绑定结果无法确认，请刷新核实，不能直接重试。");
            }
            return ServiceResult<SaveItemResult>.Ok(new SaveItemResult
            {
                Uid = productId, Operation = SaveOperation.Update, Success = true,
                OldVersion = write.OldVersion, NewVersion = write.NewVersion, Message = write.Message
            }, "BOM 绑定已保存。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "产品 BOM 绑定失败。");
            return session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure
                ? Fail(ServiceErrorCodes.ServiceUnavailable, "并发结构变化导致绑定未提交，请刷新后重试。")
                : Fail(ServiceErrorCodes.Unexpected, "BOM 绑定失败，请稍后重试。");
        }
        finally
        {
            if (transaction is not null)
            {
                // 未确认提交的事务尽力回滚；提交回执丢失仍保留未知结果，清理不伪造失败或成功。
                if (!committed)
                    try { transaction.Rollback(); } catch (Exception exception) { _logger.LogWarning(exception, "回滚 BOM 绑定事务失败。"); }
                try { transaction.Dispose(); } catch (Exception exception) { _logger.LogWarning(exception, "释放 BOM 绑定事务失败。"); }
                session.Transaction = null;
            }
        }
    }

    /// <summary>构造保持稳定错误码的失败，不伪造成功保存回执。</summary>
    private static ServiceResult<SaveItemResult> Fail(string code, string message) => ServiceResult<SaveItemResult>.Fail(code, message);
}
