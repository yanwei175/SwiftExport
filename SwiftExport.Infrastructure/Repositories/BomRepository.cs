using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SwiftExport.Infrastructure.Repositories
{
    public class BomRepository : DapperRepository<BOM>, IBomRepository
    {

        private readonly IDataTableFactory<BOM> _fac;
        public BomRepository(IDbConnectionFactory _connFac, IMappingCache cache,IDataTableFactory<BOM> fac) : base(cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where BomNo = @BomNo";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<BOM>(sql, new { BomNo = bomNo });
        }

        public async Task<Dictionary<string, IEnumerable<BOM>>> Get一堆BomBy一堆Bom号(IEnumerable<string> bomNos)
        {
            if (bomNos == null || !bomNos.Any()) return new Dictionary<string, IEnumerable<BOM>>();

            // 【核心优化】使用 WHERE IN 进行单次查询
            var sql = $"SELECT * FROM [{tableName}] WHERE BomNo IN @BomNos";

            // Dapper 会自动将 IEnumerable<string> BomNos 转换为 SQL 参数列表
            using (var _connection = _connFac.CreateConnection())
            {
                var allBoms = await _connection.QueryAsync<BOM>(
                                         sql,
                                         new { BomNos = bomNos });
                // 使用 LINQ 将结果分组并转换为字典
                return allBoms.GroupBy(b => b.BomNo)
                              .ToDictionary(g => g.Key, g => g.AsEnumerable());
            }
        }

        public async Task<int> DeleteBomByBomNoAsync(string bomNo, IUnitOfWork uow)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE BomNo = @BomNo";
            // 使用基类已有的字段
            return await uow.Connection.ExecuteAsync(sql, new { BomNo = bomNo }, transaction: uow.Transaction);
        }

        public async Task<int> SyncBomItemsAsync(IEnumerable<BOM> items, IUnitOfWork uow)
        {
           var dt= await _fac.CreatDtByEntityPropertysAsync(items);
                    return await uow.Connection.ExecuteAsync(
                        "SyncBoms",
                        new { Boms = dt.AsTableValuedParameter("BomType") },
                        transaction: uow.Transaction,
                        commandType: CommandType.StoredProcedure
                    );
        }


    }
}
