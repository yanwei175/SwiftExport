using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IExcelSheetFieldsMappingService : IBaseService<ExcelSheetFieldsMapping>
    {
        Task<ServiceResult<Dictionary<string,string>>> GetListByShengXiaoDomain(string sheetDomainName);

    }
}
