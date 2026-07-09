using Refit;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.BaseApi
{
    public interface IGenericApi<TDto> where TDto : class
    {

        // 2. 获取单体 (传入 "api/product/5")
        // 返回单体 TDto
        [Get("/{id}")]
        Task<ServiceResult<TDto>> GetByIdAsync(Guid id);

        // ================= 操作 (POST/PUT/DELETE) =================

        // 3. 新建 (传入 "api/product")
        // DTO 会被自动序列化成 JSON 塞进请求体 (Body)
        [Post("/")]
        Task<ServiceResult<Guid>> CreateAsync([Body] TDto request);

        // 4. 更新 (传入 "api/product/5")
        // 同样，数据走 Body，URL 只负责定位资源
        [Put("/{id}")]
        Task<ServiceResult<Guid>> UpdateAsync(Guid id, [Body] TDto request);

        // 5. 删除 (传入 "api/product/5")
        // 直接利用 path 即可，不需要再传 id，返回单个 int
        [Delete("/{id}")]
        Task<ServiceResult<Guid>> DeleteAsync(Guid id);

    }
}
