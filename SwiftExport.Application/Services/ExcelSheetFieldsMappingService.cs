using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ExcelSheetFieldsMappingService : IExcelSheetFieldsMappingService
    {
        private readonly IExcelSheetFieldsRepository _repo;
        private readonly IUintOfWorkFactory _uowFac;
        public ExcelSheetFieldsMappingService(IExcelSheetFieldsRepository  repo, IUintOfWorkFactory uowFac)
        {
            _repo = repo;
            _uowFac = uowFac;
        }
        public async Task<IEnumerable<ExcelSheetFieldsMapping>> GetAll()
        {
            return await _repo.GetAllAsync();

        }

        public async Task<Dictionary<string, string>> GetListByShengXiaoDomain(string sheetDomainName)
        {
            return await _repo.GetListByShengXiaoDomain(sheetDomainName);
        }
    }
}
