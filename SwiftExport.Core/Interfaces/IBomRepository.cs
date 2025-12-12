using SwiftExport.Core.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IBomRepository : IRepository<BOM>
    {
        /// <summary>
        /// 通过一堆BOM号,获取多张BOM
        /// </summary>
        /// <param name="bomNos">
        /// 一堆BOM号的集合
        /// </param>
        /// <returns>字典(BOM号,对应的BOM集合)</returns>
        Task<Dictionary<string, IEnumerable<BOM>>> Get一堆BomBy一堆Bom号(IEnumerable<String> bomNos);

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
        Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo);

        /// <summary>
        /// 调用数据库的存储过程,执行Merge 操作,适用于Excel批量修改BOM条目,不针对单个BOM对象
        /// 同步客户产品,
        /// 需要数据库里建有对应的存储过程和客户自定义表
        /// </summary>
        /// <param name="items">
        /// 必须带ID的一个BOM对象列表.
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> 同步BomItemsAsync(IEnumerable<BOM> items);
    }
}
