using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.QualityControl;
/// <summary>售后头行事务持久化及只读查询，不执行跨域业务命令。</summary>
public interface IAfterSalesRepository
{
    /// <summary>读取单头，可在调用方事务中锁定。</summary>
    Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false);
    /// <summary>读取全部历史行，保留被删除行的身份与行号。</summary>
    Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false);
    /// <summary>保存单头，更新返回是否命中原版本。</summary>
    Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert);
    /// <summary>保存明细，保留应用指定的 Uid。</summary>
    Task<bool> WriteLineAsync(AfterSalesLine line, bool insert);
    /// <summary>序列化同一命令的并发重试，必须在事务内调用。</summary>
    Task LockCommandAsync(Guid commandId);
    /// <summary>按单头分页，不因多条匹配行重复累计。</summary>
    Task<PagedResponseBase<AfterSalesListItemDto>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken);
    /// <summary>查询有限种类的安全候选。</summary>
    Task<PagedResponseBase<AfterSalesLookupDto>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken);
    /// <summary>验证引用并读取快照；活动校验只用于新承接。</summary>
    Task<AfterSalesLookupDto?> ReferenceAsync(string kind, Guid uid, Guid? customerId, bool requireActive);
}

