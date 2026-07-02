using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces;

public partial interface IBomService : IStandardService<BomDto> { }
public partial interface ICustomerPoConfigService : IStandardService<CustomerPoConfigDto> { }
public partial interface ICustomerService : IStandardService<CustomerDto> { }
public partial interface IDependentDemandService : IStandardService<IndependentDemandDto> { }
public partial interface IFollowUpService : IStandardService<FollowUpLogDto> { }
public partial interface IIndependentDemandServices : IStandardService<IndependentDemandDto> { }
public partial interface IPackingListService : IStandardService<PackingListHeadDto> { }
public partial interface IProductCategoryService : IStandardService<ProductCategoryDto> { }
public partial interface IProductColorService : IStandardService<ProductColorDto> { }
public partial interface IProductService : IStandardService<ProductDto> { }
public partial interface IPurchaseOrderService : IStandardService<PurchaseOrderHeadDto> { }
public partial interface ISalesOrderHeadService : IStandardService<SalesOrderHeaderDto> { }
public partial interface ISupplierService : IStandardService<SupplierDto> { }
public partial interface ISysDicCurrencyService : IStandardService<SysDicCurrencyDto> { }
public partial interface ISysDicCurrencyExchangeService : IStandardService<SysDicCurrencyExchangeDto> { }
public partial interface ISysDictDataService : IStandardService<SysDictDataDto> { }
public partial interface ISysDictTypeService : IStandardService<SysDictTypeDto> { }
public partial interface ISysViewRouteService : IStandardService<SysViewRouteDto> { }

public interface IStandardService<TDto> where TDto : class
{
    Task<ServiceResult<TDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(TDto dto);
    Task<ServiceResult<int>> UpdateAsync(TDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<TDto> batchReq);
}
