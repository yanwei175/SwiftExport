using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 单据流水号仓储接口。
    /// </summary>
    public interface IDocumentSequenceRepository : IRepository<DocumentSequence>
    {
        /// <summary>
        /// 原子获取并递增流水号。
        /// 若不存在对应 DocType+YearMonth 记录则自动初始化。
        /// </summary>
        Task<DocumentSequence> GetNextAsync(string docType, string yearMonth, string? updatedBy);
    }
}
