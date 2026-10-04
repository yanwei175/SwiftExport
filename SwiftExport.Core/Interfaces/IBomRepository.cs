using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>提供 BOM 组成持久化、当前结构及产品引用读取契约；业务资格和事务边界由应用服务控制。</summary>
    public interface IBomRepository : IRepository<Bom>
    {
        /// <summary>在调用方事务中读取全图共享保存事实并关联请求下标；空请求仍读图。withUpdLock 保留 SQL Server 组成保存的既有范围保护，普通产品校验应显式关闭；不承诺 PostgreSQL 并发保护。</summary>
        Task<IReadOnlyList<SharedBomGroup>> GetSharedSaveFactsAsync(IReadOnlyList<string> requestedBomNos, bool withUpdLock = true);

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
        /// 根据BOM号获取一张BOM
        /// </summary>
        /// <param name="bomNo">
        /// BOM号
        /// </param>
        /// <returns>一张BOM</returns>
        Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo);

        /// <summary>
        /// 按旧存储标记精确读取未删组成，仅保留历史核验用途；当前需求按共享编号读取，不以标记数值选版。
        /// </summary>
        Task<IReadOnlyList<Bom>> GetByBomNoAndVersionAsync(string bomNo, int version);

        /// <summary>
        /// 判断产品是否仍有有效 BOM。用于客供件终端物料约束。
        /// </summary>
        Task<bool> HasActiveBomForProductAsync(Guid productId, string? bomNo = null);

        /// <summary>读取父或子身份命中指定产品的当前未删组成；按数据库编号等值规则联接有效父产品当前版本，空输入不查询，复用事务且不增加锁。</summary>
        Task<IReadOnlyList<Bom>> GetCurrentReferencesForProductsAsync(IEnumerable<Guid> productIds);

        /// <summary>分页查询父产品当前版本下的有效 BOM 组成行。</summary>
        Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request);

        Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo);

        // 已停用的旧批量同步入口仅作历史定位，不使用 XML 文档注释，避免生成无归属成员说明。
        //Task<int> SyncBomItemsAsync(IEnumerable<Bom> items);
    }
}
