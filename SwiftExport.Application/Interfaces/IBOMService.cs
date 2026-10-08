using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

/// <summary>共享 BOM 组成保存与只读结构用例，当前编号共享规则由服务统一校验。</summary>
public partial interface IBomService : IBaseService<Bom, BomDto>
{
    Task<ServiceResult<IReadOnlyList<BomDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<BomDto>>> GetPagedListAsync(BomSearchRequest request);
    Task<ServiceResult<BomBatchSaveResponse>> BatchSaveAsync(BomBatchSaveRequest batchReq);

    Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo);

    //以下为批量操作

    /// <summary>可取消地读取当前共享组成，允许空 BOM 继续录入。</summary>
    Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNoAsync(string bomNo, CancellationToken cancellationToken = default);


    /// <summary>可取消地校验已保存结构的比例、组件与循环。</summary>
    Task<ServiceResult<bool>> ValidateBomAsync(string productSku, CancellationToken cancellationToken = default);

    /// <summary>可取消地读取当前编号的只读多层组成树。</summary>
    Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku, CancellationToken cancellationToken = default);

    //同步批量的BomItems
    //Task<ServiceResult<int>> SyncBomItemsAsync(IEnumerable<Bom> items);
}
