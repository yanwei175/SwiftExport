using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    public interface ISysDictDataApi : IGenericApi<DictDataDto>
    {
        // ★ 继承了基类所有的 CRUD 和 BatchSave，
        // 这里只需要额外补充一个“根据 Code 查明细”的特殊业务接口！
        [Get("/by-code/{dictCode}")]
        Task<ServiceResult<IReadOnlyList<DictDataDto>>> GetByCodeAsync(string dictCode);
    }
}
