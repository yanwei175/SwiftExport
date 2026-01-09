using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class WhiteListRepository : DapperRepository<WhiteLists>, IWhiteListRepository
    {
        public WhiteListRepository(IMappingCache cache, IDbConnectionFactory _connFac) : base(cache, _connFac)
        {
        }

        public async Task<IEnumerable<string>> GetVstoWhiteListAsync()
        {
            using (var connection = _connFac.CreateConnection())
            {
                // SQL 修正：仅查询 CustomerCode 字段，并使用 IN 语句
                var sql = $"SELECT ExcelWhiteList FROM [{tableName}]";
                return await connection.QueryAsync<string>(
                    sql
                );
            }
        }
    }
}

