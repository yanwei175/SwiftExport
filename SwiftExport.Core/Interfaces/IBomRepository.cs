using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
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

        Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo);

        /// <summary>
        /// 调用数据库的存储过程,执行Merge 操作,适用于Excel批量修改BOM条目,不针对单个BOM对象
        /// 同步客户产品,
        /// 需要数据库里建有对应的存储过程和客户自定义表
        /// </summary>
        /// <param name="items">
        /// 必须带ID的一个BOM对象列表.
        /// </param>
        /// <returns>受影响的行数</returns>
        //Task<int> SyncBomItemsAsync(IEnumerable<Bom> items);
    }
}
