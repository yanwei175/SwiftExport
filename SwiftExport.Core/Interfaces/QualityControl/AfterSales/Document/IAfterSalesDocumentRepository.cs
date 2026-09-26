using SwiftExport.Core.Entities.QualityControl;
namespace SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
/// <summary>售后头行持久化，所有写入参加调用方事务。</summary>
public interface IAfterSalesDocumentRepository
{
    /// <summary>读取或锁定售后单头。</summary>
    Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false);
    /// <summary>读取全部历史行并可加锁。</summary>
    Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false);
    /// <summary>写入头并校验原版本。</summary>
    Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert);
    /// <summary>写入行并校验原版本。</summary>
    Task<bool> WriteLineAsync(AfterSalesLine line, bool insert);
}
