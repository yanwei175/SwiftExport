using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    // 注意：这里的泛型我们传 SysDictDataDto，因为右侧 DataGrid 主要操作明细
    public interface IDictApi : IGenericApi<SysDictDataDto>
    {
        // 1. 获取所有字典大类 (供左侧 ListBox 使用)
        [Get("/types")]
        Task<ServiceResult<List<SysDictTypeDto>>> GetAllDictTypesAsync();

        // 2. 根据字典编码获取明细 (供右侧 DataGrid 使用)
        [Get("/data/{dictCode}")]
        Task<ServiceResult<List<SysDictDataDto>>> GetDictDataByCodeAsync(string dictCode);

        // （增删改等基础操作已通过继承 IGenericApi<SysDictDataDto> 自动拥有！）
    }
}
