namespace SwiftExport.Application.Services
{
    public class ExcelSheetFieldsMappingService : BaseService<ExcelSheetFieldsMapping>, IExcelSheetFieldsMappingService
    {

        private readonly IExcelSheetFieldsRepository _thisrepo;
        public ExcelSheetFieldsMappingService(IExcelSheetFieldsRepository _repo,
            IUintOfWorkFactory _uowFac) : base(_repo, _uowFac)
        {
            _thisrepo = _repo;
        }

        public async Task<ServiceResult<Dictionary<string, string>>> GetListByShengXiaoDomain(string sheetDomainName)
        {
            if (string.IsNullOrWhiteSpace(sheetDomainName))
                return ServiceResult<Dictionary<string, string>>.Fail("参数错误");

            var rst = await _thisrepo.GetListByShengXiaoDomain(sheetDomainName);
            if (rst == null)
                return ServiceResult<Dictionary<string, string>>.Fail("无结果");
            return ServiceResult<Dictionary<string, string>>.Ok(rst);
        }
    }
}
