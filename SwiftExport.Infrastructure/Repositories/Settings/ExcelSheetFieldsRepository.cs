using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class ExcelSheetFieldsRepository : DapperRepository<ExcelSheetFieldsMapping>, IExcelSheetFieldsRepository
    {

        private readonly IDataTableFactory<ExcelSheetFieldsMapping> _fac;
        public ExcelSheetFieldsRepository(IDbConnectionFactory _connFac, IMappingCache cache, IDataTableFactory<ExcelSheetFieldsMapping> fac) : base( cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<Dictionary<string, string>> GetListByShengXiaoDomain(string sheetDomainName)
        {
            var sql = $"SELECT SourceFields, TargetFields FROM  [{tableName}] WHERE IsDelete<>1 AND ShengXiaoDomain = @sheetDomainName";
            using (var _connection = _connFac.CreateConnection())
            {
                var lst = await _connection.QueryAsync<ExcelSheetFieldsMapping>(sql, new { sheetDomainName });
                return lst.ToDictionary(x => x.SourceFields, x => x.TargetFields);
            }

        }
    }
}
