using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IExcelSheetFieldsRepository : IRepository<ExcelSheetFieldsMapping>
    {
            Task<Dictionary<string, string>> GetListByShengXiaoDomain(string sheetDomainName);
    }
}
