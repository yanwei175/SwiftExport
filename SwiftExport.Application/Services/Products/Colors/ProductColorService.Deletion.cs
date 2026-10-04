using SwiftExport.Contracts.Wrappers;
using Microsoft.Extensions.Logging;

namespace SwiftExport.Application.Services.Products.Colors;

public partial class ProductColorService
{
    /// <summary>在同一事务内锁定颜色并验证引用，保持通用删除的逐项版本结果契约。</summary>
    public override async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
    {
        if (_session.Transaction is not null)
            return ServiceResult<SaveItemResult>.Fail("当前请求已存在未完成事务，不能重复开始产品颜色删除。");
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var result = await base.DeleteAsync(item);
            if (result.Success && result.Data is { Success: true })
                _session.Transaction.Commit();
            else
                _session.Transaction.Rollback();
            return result;
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "产品颜色删除事务失败。");
            return ServiceResult<SaveItemResult>.Fail(
                SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected,
                "系统发生内部错误，请联系管理员。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>先锁父颜色，再读取有效引用，避免与遵循相同顺序的中性产品保存交错。</summary>
    protected override async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> deleteItems)
    {
        var ids = deleteItems.Select(item => item.Uid).Distinct().ToArray();
        await repository.GetByIdListAsync(ids, withUpdLock: true);
        var referenced = await repository.GetReferencedColorIdsAsync(ids);
        return referenced.Count == 0 ? null
            : "删除失败：以下产品颜色仍被产品或中性产品引用：" + string.Join(", ", referenced);
    }
}
