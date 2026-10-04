using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Products;

/// <summary>保留既有单项和按号写入契约，将变更统一交给当前 BOM 保存内核。</summary>
public partial class BomService
{
    /// <summary>将新增请求原样包装为单项批量保存，保留客户端行标识并校验变更后的完整结构。</summary>
    public override async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<BomDto> item)
        => ToSingleSaveResult(await BatchSaveAsync(new BomBatchSaveRequest { CreateItems = [item] }));

    /// <summary>以包装请求的 Uid 和 Version 为权威更新当前行，DTO 内身份不得覆盖目标及并发依据。</summary>
    public override async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<BomDto> item)
        => ToSingleSaveResult(await BatchSaveAsync(new BomBatchSaveRequest { UpdateItems = [item] }));

    /// <summary>保留请求的软删或物理删除选项，删除前仍须验证最终完整图，不能留下空的有效 BOM。</summary>
    public override async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
        => ToSingleSaveResult(await BatchSaveAsync(new BomBatchSaveRequest { DeleteItems = [item] }));

    /// <summary>在统一事务内解析并尝试物理删除当前组成；历史行不参与，空结构门禁失败时回滚且不清父 BOM 标识。</summary>
    public async Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo)
    {
        if (string.IsNullOrWhiteSpace(bomNo))
            return ServiceResult<int>.Fail("BOM 号不能为空。");
        var result = await SaveBatchAsync(new BomBatchSaveRequest(), bomNo.Trim());
        return result.Success
            ? ServiceResult<int>.Ok(result.Data.DeleteSuccessCount, result.Message)
            : new ServiceResult<int> { Success = false, Message = result.Message, Error = result.Error };
    }

    /// <summary>单项成功直接复用保存回执；失败保留服务错误及字段路径，不将已回滚的行回执当作成功。</summary>
    private static ServiceResult<SaveItemResult> ToSingleSaveResult(ServiceResult<BomBatchSaveResponse> result)
        => result.Success
            ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single(), result.Message)
            : new ServiceResult<SaveItemResult> { Success = false, Message = result.Message, Error = result.Error };
}
