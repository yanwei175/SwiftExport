using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IExcelSheetFieldsMappingService
    {
        Task<IEnumerable<ExcelSheetFieldsMapping>> GetAll();
        Task<Dictionary<string,string>> GetListByShengXiaoDomain(string sheetDomainName);

    }
}
