using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>提供 BOM 组成持久化、当前结构及产品引用读取契约；业务资格和事务边界由应用服务控制。</summary>
    public interface IBomRepository : IRepository<Bom>
    {

        Task<bool> IsBomItemExistAsync(string bomNo, string productSku);
        /// <summary>
        /// 通过一堆BOM号,获取多张BOM
        /// </summary>
        /// <param name="bomNos">
        /// 一堆BOM号的集合
        /// </param>
        /// <returns>字典(BOM号,对应的BOM集合)</returns>
        Task<Dictionary<string, IEnumerable<Bom>>> GetBomsCollectionByBomNoListAsync(IEnumerable<String> bomNos);

        /// <summary>
        /// 根据BOM号删除一张BOM
        /// </summary>
        /// <param name="bomNo">
        /// BOM号
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteBomByBomNoAsync(string bomNo);

        /// <summary>
        /// 根据BOM号获取一张BOM
        /// </summary>
        /// <param name="bomNo">
        /// BOM号
        /// </param>
        /// <returns>一张BOM</returns>
        Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo);

        /// <summary>
        /// 按 BOM 编号和版本读取当前有效明细。需求拆解必须使用此方法，
        /// 避免把其他版本或已删除明细混入同一次拆解。
        /// </summary>
        Task<IReadOnlyList<Bom>> GetByBomNoAndVersionAsync(string bomNo, int version);

        /// <summary>
        /// 判断产品是否仍有有效 BOM。用于客供件终端物料约束。
        /// </summary>
        Task<bool> HasActiveBomForProductAsync(Guid productId, string? bomNo = null);

        /// <summary>读取父或子身份命中指定产品的当前未删组成；按数据库编号等值规则联接有效父产品当前版本，空输入不查询，复用事务且不增加锁。</summary>
        Task<IReadOnlyList<Bom>> GetCurrentReferencesForProductsAsync(IEnumerable<Guid> productIds);

        /// <summary>获取所有父产品当前版本下的有效 BOM 行。</summary>
        Task<IReadOnlyList<Bom>> GetCurrentVersionBomGraphAsync();

        /// <summary>分页查询父产品当前版本下的有效 BOM 组成行。</summary>
        Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request);

        Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo);

        // 已停用的旧批量同步入口仅作历史定位，不使用 XML 文档注释，避免生成无归属成员说明。
        //Task<int> SyncBomItemsAsync(IEnumerable<Bom> items);
    }
}
