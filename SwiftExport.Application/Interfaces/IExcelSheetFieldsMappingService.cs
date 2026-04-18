namespace SwiftExport.Application.Interfaces
{
    public interface IExcelSheetFieldsMappingService : IBaseService<ExcelSheetFieldsMapping>
    {
        Task<ServiceResult<Dictionary<string, string>>> GetListByShengXiaoDomain(string sheetDomainName);

    }
}
