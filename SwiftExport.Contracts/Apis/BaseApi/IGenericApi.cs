using Refit;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.BaseApi
{
    public interface IGenericApi<TDto> where TDto : class
    {
        // ================= 查询 (GET) =================

        [Get("/")]
        Task<ServiceResult<PagedResultDto<TDto>>> GetPagedAsync([Query] PagedRequestDto request);

        // 1. 获取列表 (传入 "api/product" 或 "api/product?page=1")
        // 注意 {**path} 里的两个星号，这是防止路径里的斜杠 "/" 被转义的终极魔法！
        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync();

        // 2. 获取单体 (传入 "api/product/5")
        // 返回单体 TDto
        [Get("/{id}")]
        Task<ServiceResult<TDto>> GetByIdAsync(int id);


        // ================= 操作 (POST/PUT/DELETE) =================

        // 3. 新建 (传入 "api/product")
        // DTO 会被自动序列化成 JSON 塞进请求体 (Body)
        [Post("/")]
        Task<ServiceResult<int>> CreateAsync([Body] TDto request);

        // 4. 更新 (传入 "api/product/5")
        // 同样，数据走 Body，URL 只负责定位资源
        [Put("/{id}")]
        Task<ServiceResult<int>> UpdateAsync(int id, [Body] TDto request);

        // 5. 删除 (传入 "api/product/5")
        // 直接利用 path 即可，不需要再传 id，返回单个 int
        [Delete("/{id}")]
        Task<ServiceResult<int>> DeleteAsync(int id);


        // ================= 批量操作 (特殊的 POST) =================

        // 6. 批量保存混合包 (实际传入比如 "api/product/batch-save")
        [Post("/batch-save")]
        Task<ServiceResult<object>> BatchSaveAsync([Body] BatchRequestGeneric<TDto> request);

    }
}
