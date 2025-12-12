using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class BomRepository : DapperRepository<BOM>, IBomRepository
    {
        private readonly IDbConnectionFactory _connFactory;
        private readonly IMappingCache _cache;
        private readonly string tableName;
        private readonly IDataTableFactory<BOM> _fac;
        public BomRepository(IDbConnectionFactory connFactory, IMappingCache cache,IDataTableFactory<BOM> fac) : base(connFactory, cache)
        {
            _connFactory = connFactory;
            _cache = cache;
            tableName = _cache.GetTableNameByEntity<BOM>() ?? typeof(BOM).Name;
            _fac = fac;
        }

        public async Task<int> DeleteBomByBomNoAsync(string bomNo)
        {
            var sql = $"DELETE FROM  [{tableName}] WITH Where BomNo = @BomNo";
            using (var conn = _connFactory.CreateConnection())
            {
                return await conn.ExecuteAsync(sql, new { BomNo = bomNo });
            }
        }

        public async Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where BomNo = @BomNo";

            using (var conn = _connFactory.CreateConnection())
            {
                return await conn.QueryAsync<BOM>(sql, new { BomNo = bomNo });
            }
        }

        public async Task<Dictionary<string, IEnumerable<BOM>>> Get一堆BomBy一堆Bom号(IEnumerable<string> bomNos)
        {
            var dic = new Dictionary<string, IEnumerable<BOM>>();
                using (var conn = _connFactory.CreateConnection())
                {
                    foreach (var BomNo in bomNos)
                    {
                        var sql = $"SELECT * FROM  [{tableName}] Where BomNo = @BomNo";
                        var rst = await conn.QueryAsync<BOM>(sql, new { BomNo });
                        if (rst.Any()) dic[BomNo]= rst; 
                    }
                }
                return dic;
        }

        public async Task<int> 同步BomItemsAsync(IEnumerable<BOM> items)
        {
           var dt= await _fac.CreatDtByEntityPropertysAsync(items);
                using (var conn = _connFactory.CreateConnection())
                {
                    return await conn.ExecuteAsync(
                        "SyncBoms",
                        new { Boms = dt.AsTableValuedParameter("BomType") },
                        commandType: CommandType.StoredProcedure
                    );
                }
        }






    }
}
