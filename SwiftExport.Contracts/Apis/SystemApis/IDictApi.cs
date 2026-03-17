using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    // 注意：这里的泛型我们传 DictDataDto，因为右侧 DataGrid 主要操作明细
    public interface IDictApi : IGenericApi<DictDataDto>
    {
        // 1. 获取所有字典大类 (供左侧 ListBox 使用)
        [Get("/api/dict/types")]
        Task<ApiResponse<List<DictTypeDto>>> GetAllDictTypesAsync();

        // 2. 根据字典编码获取明细 (供右侧 DataGrid 使用)
        [Get("/api/dict/data/{dictCode}")]
        Task<ApiResponse<List<DictDataDto>>> GetDictDataByCodeAsync(string dictCode);

        // （增删改等基础操作已通过继承 IGenericApi<DictDataDto> 自动拥有！）
    }
}
