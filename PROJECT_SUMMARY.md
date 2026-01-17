# PROJECT SUMMARY

Generated: 2026-01-17 16:33:23

## Included Projects

- SwiftExport.Application
- SwiftExport.Core
- SwiftExport.Infrastructure
- SwiftExport.Contracts
- SwiftExport.WebApi
- SwiftExport.UI
- SwiftExport.Desktop
- SwiftExport.ExcelClient

## File Tree

```
  |- DependencyInjection.cs
    |- IBaseService.cs
    |- IProductService.cs
    |- ProductProfile.cs
      |- BaseService.cs
      |- ProductService.cs
  |- SwiftExport.Application.csproj
      |- BaseDto.cs
    |- PagedResultDto.cs
      |- ProductCategoryDto.cs
      |- ProductColorDto.cs
      |- ProductDto.cs
      |- ProductSearchDto.cs
    |- IProductApi.cs
  |- SwiftExport.Contracts.csproj
    |- ServiceResult.cs
    |- BaseEntity.cs
    |- PagedResult.cs
    |- QueryCondition.cs
      |- AccessControlList.cs
      |- ExcelImportConfig.cs
      |- SysRole.cs
      |- SysUser.cs
      |- UserRoleView.cs
      |- Customer.cs
      |- Product.cs
      |- ProductCategory.cs
      |- ProductColor.cs
      |- ProductDetail.cs
      |- ProductMaster.cs
      |- ProductPackage.cs
      |- Supplier.cs
      |- BillOfMaterial.cs
      |- PackingList.cs
      |- PackingListItem.cs
      |- PlanOrderItem.cs
      |- PurchaseOrder.cs
      |- PurchaseOrderItem.cs
      |- ReceiptItem.cs
      |- SalesOrder.cs
      |- SalesOrderItem.cs
      |- ProductSearchMeta.cs
      |- CustomerPoConfig.cs
      |- CustomerProduct.cs
      |- SupplierProduct.cs
    |- OrderStatus.cs
    |- IProductRepository.cs
    |- IRepository.cs
    |- IUnitOfWork.cs
  |- SwiftExport.Core.csproj
  |- App.xaml
  |- App.xaml.cs
  |- AssemblyInfo.cs
  |- MainWindow.xaml
  |- MainWindow.xaml.cs
  |- SwiftExport.Desktop.csproj
  |- AddIn.cs
    |- TaskPaneManager.cs
    |- launchSettings.json
  |- RibbonController.cs
  |- SwiftExport.ExcelClient.csproj
    |- ProductView.cs
    |- ProductView.resx
    |- DbConnectionFactory.cs
    |- DbSession.cs
    |- UnitOfWork.cs
  |- DependencyInjection.cs
      |- DapperRepository.cs
    |- ProductRepository.cs
  |- SwiftExport.Infrastructure.csproj
  |- AppStarter.cs
    |- CachedImageProvider.cs
    |- IImageProvider.cs
    |- GlobalApi.cs
  |- SwiftExport.UI.csproj
      |- RelayCommand.cs
      |- ViewModelBase.cs
    |- ProductColorViewModel.cs
    |- FrmProductColorManager.xaml
    |- FrmProductColorManager.xaml.cs
    |- MainWindow.xaml
    |- MainWindow.xaml.cs
  |- appsettings.Development.json
  |- appsettings.json
    |- ProductController.cs
  |- Program.cs
    |- launchSettings.json
  |- SwiftExport.WebApi.csproj
```

## Types Overview

### class DependencyInjection  `(SwiftExport.Application)`

- File: `SwiftExport.Application\DependencyInjection.cs`

#### Properties

- `class DependencyInjection`

#### Methods (signatures)

- `IServiceCollection AddApplicationServices(this IServiceCollection services)`
- `services.AddScoped<IProductService, ProductService>()`

### interface IBaseService<TEntity, TDto>  `(SwiftExport.Application.Interfaces)`

- File: `SwiftExport.Application\Interfaces\IBaseService.cs`

#### Methods (signatures)

- `Task<bool> ExistsIdAsync(int id)`
- `Task<ServiceResult<IReadOnlyList<int>>> GetExistingIdListAsync(IEnumerable<int> idList)`
- `Task<ServiceResult<TDto>> GetByIdAsync(int id)`
- `Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<int> idList)`
- `Task<ServiceResult<TDto?>> GetSingleEntityAsync()`
- `Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync()`
- `Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null)`
- `Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria)`
- `Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object noNameObj)`
- `Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")`
- `Task<ServiceResult<int>> AddAsync(TDto dto)`
- `Task<ServiceResult<int>> UpdateAsync(TDto dto)`
- `Task<ServiceResult<int>> DeleteSoftAsync(int id)`
- `Task<ServiceResult<int>> DeleteAsync(int id)`
- `Task<ServiceResult<IReadOnlyList<int>>> AddRangeAsync(IEnumerable<TDto> dtoList)`
- `Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<int> idList)`
- `Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<int> idList)`
- `Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList)`

### interface IProductService  `(SwiftExport.Application.Interfaces)`

- File: `SwiftExport.Application\Interfaces\IProductService.cs`

#### Methods (signatures)

- `Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)`

### class ProductProfile  `(SwiftExport.Application.Mappings)`

- File: `SwiftExport.Application\Mappings\ProductProfile.cs`

#### Methods (signatures)

- ` ProductProfile()`
- `CreateMap<ProductDto, Product>()`

### class BaseService<TEntity, TDto>  `(SwiftExport.Application.Services.Base)`

- File: `SwiftExport.Application\Services\Base\BaseService.cs`

### class ProductService  `(SwiftExport.Application.Services.Products)`

- File: `SwiftExport.Application\Services\Products\ProductService.cs`

#### Methods (signatures)

- ` if(list == null)`

### class PagedResultDto<T>  `(SwiftExport.Contracts.DTOs)`

- File: `SwiftExport.Contracts\DTOs\PagedResultDto.cs`
- Detected: DTO

#### Properties

- `IReadOnlyList<T> Items`
- `int TotalCount`
- `int PageIndex`
- `int PageSize`

### class ProductCategoryDto  `(SwiftExport.Contracts.DTOs.Product)`

- File: `SwiftExport.Contracts\DTOs\Product\ProductCategoryDto.cs`
- Detected: DTO

#### Properties

- `string Name`
- `int ParentId`
- `int SortNo`
- `List<ProductCategoryDto> Children`

### class ProductColorDto  `(SwiftExport.Contracts.DTOs.Product)`

- File: `SwiftExport.Contracts\DTOs\Product\ProductColorDto.cs`
- Detected: DTO

#### Properties

- `string ColorName`
- `string ColorCode`

### class ProductDto  `(SwiftExport.Contracts.DTOs.Product)`

- File: `SwiftExport.Contracts\DTOs\Product\ProductDto.cs`
- Detected: DTO

#### Properties

- `string Sku`
- `string Description`
- `decimal RmbPrice`
- `decimal UsdPrice`
- `string CategoryName`
- `string SupplierName`
- `string CustomerCode`
- `string CSku`
- `string MainImagePath`

### class ProductSearchDto  `(SwiftExport.Contracts.DTOs.Product)`

- File: `SwiftExport.Contracts\DTOs\Product\ProductSearchDto.cs`
- Detected: DTO

#### Properties

- `class ProductSearchDto`
- `string CustomerCode`
- `int CategoryId`
- `int PageIndex`
- `int PageSize`

### interface IProductApi  `(SwiftExport.Contracts.Interfaces)`

- File: `SwiftExport.Contracts\Interfaces\IProductApi.cs`

#### Properties

- `interface IProductApi`

#### Methods (signatures)

- `Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync()`
- `Task<ServiceResult<PagedResultDto<ProductDto>>> GetProductsAsync([Query] ProductSearchDto search)`
- `Task<ServiceResult<ProductDto>> GetByIdAsync(int id)`

### class ServiceResult<T>  `(SwiftExport.Contracts.Wrappers)`

- File: `SwiftExport.Contracts\Wrappers\ServiceResult.cs`

#### Properties

- `bool Success`
- `string Message`
- `T Data`

### class BaseEntity  `(SwiftExport.Core.Common)`

- File: `SwiftExport.Core\Common\BaseEntity.cs`
- Detected: Entity/Model

#### Properties

- `class BaseEntity`
- `string? Even`
- `string? Status`
- `short IsDeleted`
- `DateTime? CreatedTime`
- `DateTime? UpdatedTime`

### class PagedResult<T>  `(SwiftExport.Core.Common)`

- File: `SwiftExport.Core\Common\PagedResult.cs`

#### Properties

- `List<T> Items`
- `int TotalCount`
- `int PageIndex`
- `int PageSize`

### class QueryCondition  `(SwiftExport.Core.Common)`

- File: `SwiftExport.Core\Common\QueryCondition.cs`

#### Properties

- `class QueryCondition`

### class QueryCriteria  `(SwiftExport.Core.Common)`

- File: `SwiftExport.Core\Common\QueryCondition.cs`

#### Properties

- `class QueryCriteria`
- `Dictionary<string, QueryCondition> Items`
- `List<Dictionary<string, QueryCondition>> OrGroups`

#### Methods (signatures)

- `void Add(string field, QueryCondition condition)`
- `void Add(string field, 查询操作符 op, object v1, object? v2 = null)`

### class AccessControlList  `(SwiftExport.Core.Entities.Auth)`

- File: `SwiftExport.Core\Entities\Auth\AccessControlList.cs`

#### Properties

- `string ExcelWhiteList`

### class ExcelImportConfig  `(SwiftExport.Core.Entities.Auth)`

- File: `SwiftExport.Core\Entities\Auth\ExcelImportConfig.cs`

#### Properties

- `string EffectiveScope`
- `string SourceFields`
- `string TargetFields`

### class SysRole  `(SwiftExport.Core.Entities.Auth)`

- File: `SwiftExport.Core\Entities\Auth\SysRole.cs`

### class SysUser  `(SwiftExport.Core.Entities.Auth)`

- File: `SwiftExport.Core\Entities\Auth\SysUser.cs`

#### Properties

- `string UserName`
- `string? PcName`
- `string? NickName`
- `string Password`
- `string? Email`
- `string? PhoneNumber`

### class UserRoleView  `(SwiftExport.Core.Entities.Auth)`

- File: `SwiftExport.Core\Entities\Auth\UserRoleView.cs`

#### Properties

- `string UserName`
- `string? PcName`
- `string Password`
- `string? NickName`
- `int RoleID`
- `string? RoleName`

### class Customer  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\Customer.cs`

#### Properties

- `string CustomerCode`
- `string? Name`
- `string? Address`
- `string? PhoneNumber`
- `string? ContactPerson`
- `string? LabelFolderPath`
- `string? ManualFolderPath`
- `string? DrawingFolderPath`
- `string? LinedrawingFolderPath`
- `string? BarcodeFolderPath`
- `string? SpecSheetFolderPath`
- `string? DocRelateNo`

### class Product  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\Product.cs`

#### Properties

- `string? MasterSku`
- `string SKU`
- `string? Description`
- `string? Finish`
- `string? Features`
- `string SupplyType`
- `string? BomNo`
- `string? Note`
- `string? LinkSku`
- `string? MainImage`
- `string? CustomerCode`
- `string? CSKU`
- `string? SupplierCode`
- `string? SSKU`
- `string? ProductCategory`

### class ProductCategory  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\ProductCategory.cs`

#### Properties

- `string CategoryName`
- `int? ParentID`
- `string? ParentName`
- `string? CategoryNo`
- `int SortNo`

### class ProductColor  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\ProductColor.cs`

#### Properties

- `string ColorCodeUnique`
- `string ColorCode`
- `string? ColorNameEn`
- `string? ColorNameZh`

### class ProductDetail  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\ProductDetail.cs`

#### Properties

- `string ProductSKU`
- `string? LabelPath`
- `string? IMPath`
- `string? DrawingPath`
- `string? LinedrawingPath`
- `string? SpecSheetPath`
- `string? Barcode1Path`
- `string? Barcode2Path`

### class ProductMaster  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\ProductMaster.cs`

#### Properties

- `string MasterSku`
- `string? Description`
- `string? Features`
- `string? ProductClass`
- `string? ProductType`
- `string? ProductTag`

### class ProductPackage  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\ProductPackage.cs`

#### Properties

- `string? ProductSKU`
- `int? PcsOfCarton`
- `decimal? NwOfCarton`
- `decimal? GwOfCarton`
- `decimal? CbmOfCarton`
- `string? CartonClass`
- `string? CartonType`
- `decimal? CartonLength_CM`
- `decimal? CartonWidth_CM`
- `decimal? CartonHeight_CM`
- `string? ShippCartonClass`
- `string? ShippCartonType`
- `decimal? ShippCartonLength_CM`
- `decimal? ShippCartonWidth_CM`
- `decimal? ShippCartonHeight_CM`
- `string? BoxClass`
- `string? BoxType`
- `decimal? BoxLength_CM`
- `decimal? BoxWidth_CM`
- `decimal? BoxHeight_CM`

### class Supplier  `(SwiftExport.Core.Entities.MasterData)`

- File: `SwiftExport.Core\Entities\MasterData\Supplier.cs`

#### Properties

- `string? SupplierCode`
- `string? SupplierName`
- `decimal? ZheKou`
- `string? ZheKouBeiZhu`
- `string? DocRelateNo`

### class BillOfMaterial  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\BillOfMaterial.cs`

#### Properties

- `string BomNo`
- `string ProductSKU`
- `decimal Qty`
- `decimal SkuID`
- `string SubType`

### class PackingList  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\PackingList.cs`

#### Properties

- `string? PLNo`
- `DateTime? PLDate`
- `string? CustomerCode`
- `string? ShipTo`
- `string? SoNo`
- `DateTime? SoDate`
- `string? TransportNo`
- `string? SealNo`
- `DateTime? ETD`
- `DateTime? ETA`
- `string? Attention`
- `string? PLFolder`

### class PackingListItem  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\PackingListItem.cs`

#### Properties

- `string? PLNo`
- `string? SKU`
- `string? CSku`
- `string? LinkSku`
- `string? CDescription`
- `string? CFinish`
- `string? HSCode`
- `string? SoNo`
- `string? CustomerPoNo`
- `decimal? QTY`
- `decimal? PcsOfCarton`
- `decimal? NwOfCarton`
- `decimal? GwOfCarton`
- `decimal? CbmOfCarton`
- `decimal? PcsSubTotal`
- `decimal? NwSubTotal`
- `decimal? GwSubTotal`
- `decimal? CbmSubTotal`
- `string? SubNote`

### class PlanOrderItem  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\PlanOrderItem.cs`

#### Properties

- `string? PlanOrderNo`
- `string? SoNo`
- `long? SoLineID`
- `string? SKU`
- `decimal QTY`
- `string? DeliveryTo`
- `string? CustomerCode`
- `string? Note`
- `string? SupplierCode`

### class PurchaseOrder  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\PurchaseOrder.cs`

#### Properties

- `string PoNo`
- `string? SoNo`
- `string? CustomerPoNo`
- `DateTime? PoDate`
- `DateTime? ExpectedDate`
- `DateTime? ReplyDate`
- `string? CustomerCode`
- `string? SupplierCode`
- `string? SupplierName`
- `string? SoPerson`
- `string? PoPerson`
- `string? InletHoseType`
- `string? AeratorType`
- `string? PalletType`
- `string? BoxType`
- `string? LabelType`
- `string? IMType`
- `string? CartonType`
- `string? LogoType`
- `string? PoFolder`
- `string? PoNote`
- `decimal? DirectDiscountAmount`
- `decimal? DiscountRate`
- `string? DiscountRemarks`
- `string? CreatedBy`
- `string? ReviewedBy`
- `string? ApprovedBy`

### class PurchaseOrderItem  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\PurchaseOrderItem.cs`

#### Properties

- `string? PoNo`
- `string? SKU`
- `string? CSku`
- `string? LinkSku`
- `string? SSKU`
- `string? Description`
- `string? Finish`
- `string? Features`
- `decimal? RMBPrice1`
- `decimal? RMBPrice2`
- `decimal? QTY`
- `decimal? RMB1Amount`
- `decimal? RMB2Amount`
- `string? SubNote`
- `string? SupplyType`
- `string? DeliveryTo`
- `long? SoLineID`
- `decimal? RecQTY`
- `DateTime? LastRecDate`
- `string? FollowUpRemarks`
- `DateTime? FollowUpTime`
- `DateTime? FollowExpectedDate`

### class ReceiptItem  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\ReceiptItem.cs`

#### Properties

- `string? PoNo`
- `long? PoLineID`
- `string? SKU`
- `string? SupplierCode`
- `decimal? ReceiveQTY`
- `string? Note`

### class SalesOrder  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\SalesOrder.cs`

#### Properties

- `string? SoNo`
- `DateTime? SoDate`
- `DateTime? RePlyDate`
- `string? CustomerCode`
- `string? CustomerPoNo`
- `string? SoFolder`
- `string? SoNote`

### class SalesOrderItem  `(SwiftExport.Core.Entities.Orders)`

- File: `SwiftExport.Core\Entities\Orders\SalesOrderItem.cs`

#### Properties

- `string? SoNo`
- `string? SKU`
- `string? CSku`
- `string? LinkSku`
- `string? CDescription`
- `string? CFinish`
- `string? CFeatures`
- `decimal? RMBPrice`
- `decimal? USDPrice`
- `decimal? QTY`
- `decimal? RMBAmount`
- `decimal? USDAmount`
- `decimal? PcsOfCarton`
- `decimal? NwOfCarton`
- `decimal? GwOfCarton`
- `decimal? CbmOfCarton`
- `decimal? PcsSubTotal`
- `decimal? NwSubTotal`
- `decimal? GwSubTotal`
- `decimal? CbmSubTotal`
- `string? SubNote`
- `decimal? ShippedQTY`
- `DateTime? LastShippedDate`
- `DateTime? ExpectedDeliveryDate`

### class ProductSearchMeta  `(SwiftExport.Core.Entities.Others)`

- File: `SwiftExport.Core\Entities\Others\ProductSearchMeta.cs`

#### Properties

- `string? SKU`
- `string? SearchText`

### class CustomerPoConfig  `(SwiftExport.Core.Entities.Relations)`

- File: `SwiftExport.Core\Entities\Relations\CustomerPoConfig.cs`

#### Properties

- `string? CustomerCode`
- `string? SupplierCode`
- `string? InletHoseType`
- `string? AeratorType`
- `string? PalletType`
- `string? BoxType`
- `string? LabelType`
- `string? IMType`
- `string? CartonType`
- `string? LogoType`
- `string? SalesPerson`
- `string? Purchaser`
- `string? Note`

### class CustomerProduct  `(SwiftExport.Core.Entities.Relations)`

- File: `SwiftExport.Core\Entities\Relations\CustomerProduct.cs`

#### Properties

- `string? CustomerCode`
- `string? CSku`
- `string? LinkSku`
- `string? ProductSku`
- `string? CSeries`
- `string? CDescription`
- `string? CFinish`
- `string? CFeatures`
- `string? SpecFeatures`
- `decimal RmbPrice`
- `decimal UsdPrice`
- `string? Barcode1`
- `string? Barcode2`
- `string? Note`

### class SupplierProduct  `(SwiftExport.Core.Entities.Relations)`

- File: `SwiftExport.Core\Entities\Relations\SupplierProduct.cs`

#### Properties

- `string? ProductSKU`
- `string? SupplierCode`
- `string? SSKU`
- `string? SDescription`
- `string? SFeatures`
- `decimal RMBPrice1`
- `decimal RMBPrice2`

### enum OrderStatus  `(SwiftExport.Core.Enumns)`

- File: `SwiftExport.Core\Enums\OrderStatus.cs`

#### Properties

- `enum OrderStatus`

### enum LineStatus  `(SwiftExport.Core.Enumns)`

- File: `SwiftExport.Core\Enums\OrderStatus.cs`

#### Properties

- `enum LineStatus`

### enum PackingStatus  `(SwiftExport.Core.Enumns)`

- File: `SwiftExport.Core\Enums\OrderStatus.cs`

#### Properties

- `enum PackingStatus`

### interface IProductRepository  `(SwiftExport.Core.Interfaces)`

- File: `SwiftExport.Core\Interfaces\IProductRepository.cs`

#### Properties

- `interface IProductRepository`

### interface IRepository<T>  `(SwiftExport.Core.Interfaces)`

- File: `SwiftExport.Core\Interfaces\IRepository.cs`

#### Methods (signatures)

- `Task<bool> ExistsIdAsync(int id)`
- `Task<IReadOnlyList<int>> GetExistingIdListAsync(IEnumerable<int> idList)`
- `Task<T?> GetByIdAsync(int id)`
- `Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<int> idList)`
- `Task<T?> GetSingleEntityAsync()`
- `Task<IReadOnlyList<T>> GetAllAsync()`
- `Task<IReadOnlyList<T>> GetListAsync(string whereSql, object? param = null)`
- `Task<IReadOnlyList<T>> GetListAsync(QueryCriteria criteria)`
- `Task<IReadOnlyList<T>> GetListAsync(object noNameObj)`
- `Task<PagedResult<T>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")`
- `Task<int> AddAsync(T entity)`
- `Task<int> UpdateAsync(T entity)`
- `Task<int> DeleteSoftAsync(int id)`
- `Task<int> DeleteAsync(int id)`
- `Task<IReadOnlyList<int>> AddRangeAsync(IEnumerable<T> entitieList)`
- `Task<int> DeleteRangeSoftAsync(IEnumerable<int> idList)`
- `Task<int> DeleteRangeAsync(IEnumerable<int> idList)`
- `Task<int> UpdateRangeAsync(IEnumerable<T> entitieList)`

### interface IUnitOfWork  `(SwiftExport.Core.Interfaces)`

- File: `SwiftExport.Core\Interfaces\IUnitOfWork.cs`

#### Methods (signatures)

- `void BeginTransaction()`
- `void Commit()`
- `void Rollback()`

### class App  `(SwiftExport.Desktop)`

- File: `SwiftExport.Desktop\App.xaml.cs`

### class MainWindow  `(SwiftExport.Desktop)`

- File: `SwiftExport.Desktop\MainWindow.xaml.cs`

#### Methods (signatures)

- ` MainWindow()`
- ` InitializeComponent()`

### class AddIn  `(SwiftExport.ExcelClient)`

- File: `SwiftExport.ExcelClient\AddIn.cs`

#### Methods (signatures)

- `void AutoOpen()`
- `void AutoClose()`

### class TaskPaneManager  `(SwiftExport.ExcelClient.Managers)`

- File: `SwiftExport.ExcelClient\Managers\TaskPaneManager.cs`

#### Properties

- `class TaskPaneManager`

#### Methods (signatures)

- `void ShowTaskPane()`
- `{ if(_taskPane == null)`
- `{ if(!ctp.Visible)`
- `} catch(Exception ex)`

### class RibbonController  `(SwiftExport.ExcelClient)`

- File: `SwiftExport.ExcelClient\RibbonController.cs`

#### Methods (signatures)

- `void OnShowTaskPanePressed(IRibbonControl control)`
- ` if(result != null && result.Success && result.Data != null)`
- `} catch(Exception ex)`
- `void OnOpenWindow(IRibbonControl control)`

### interface IProductView  `(SwiftExport.ExcelClient.Views)`

- File: `SwiftExport.ExcelClient\Views\ProductView.cs`

#### Properties

- `interface IProductView`

### class ProductView  `(SwiftExport.ExcelClient.Views)`

- File: `SwiftExport.ExcelClient\Views\ProductView.cs`

#### Methods (signatures)

- ` ProductView()`
- `{ InitializeComponent()`
- `} catch(Exception ex)`
- ` if(result.Success)`
- `} catch(Exception ex)`
- `void InitializeComponent()`
- `((System.ComponentModel.ISupportInitialize)DgvResult).BeginInit(); SuspendLayout()`
- `((System.ComponentModel.ISupportInitialize)DgvResult).EndInit(); ResumeLayout(false)`
- ` PerformLayout()`

### interface IDbConnectionFactory  `(SwiftExport.Infrastructure.Data)`

- File: `SwiftExport.Infrastructure\Data\DbConnectionFactory.cs`

#### Properties

- `interface IDbConnectionFactory`

#### Methods (signatures)

- `IDbConnection CreateConnection()`

### class SqlConnectionFactory  `(SwiftExport.Infrastructure.Data)`

- File: `SwiftExport.Infrastructure\Data\DbConnectionFactory.cs`

#### Methods (signatures)

- `IDbConnection CreateConnection()`

### class DbSession  `(SwiftExport.Infrastructure.Data)`

- File: `SwiftExport.Infrastructure\Data\DbSession.cs`

#### Properties

- `IDbConnection Connection`
- `IDbTransaction? Transaction`

#### Methods (signatures)

- ` DbSession(IConfiguration configuration)`
- `void Dispose()`

### class UnitOfWork  `(SwiftExport.Infrastructure.Data)`

- File: `SwiftExport.Infrastructure\Data\UnitOfWork.cs`

#### Methods (signatures)

- `void BeginTransaction()`
- `void Commit()`
- `void Rollback()`
- `void Dispose()`

### class DependencyInjection  `(SwiftExport.Infrastructure)`

- File: `SwiftExport.Infrastructure\DependencyInjection.cs`

#### Properties

- `class DependencyInjection`

#### Methods (signatures)

- `IServiceCollection AddInfrastructureServices(this IServiceCollection services)`
- `services.AddScoped<IUnitOfWork, UnitOfWork>()`

### class DapperRepository<T>  `(SwiftExport.Infrastructure.Repositories.Base)`

- File: `SwiftExport.Infrastructure\Repositories\Base\DapperRepository.cs`

#### Methods (signatures)

- `List<string> GetWriteableProperties()`
- ` if(noNameObj != null)`
- ` if(props.Length > 0)`
- ` for(int i = 0; i < entityList.Count; i++)`
- ` foreach(var prop in props)`
- ` if(idProperty != null && idProperty.CanWrite)`
- ` for(int i = 0; i < newIds.Count && i < entityList.Count; i++)`
- ` for(int i = 0; i < entityList.Count; i++)`
- ` foreach(var prop in updateProps)`
- `string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)`
- ` switch(cond.操作符)`
- `string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters)`
- ` foreach(var kvp in criteria.Items)`
- ` foreach(var groupDict in criteria.OrGroups)`
- ` foreach(var kvp in groupDict)`
- ` if(orParts.Count > 0)`

### class ProductRepository  `(SwiftExport.Infrastructure.Repositories)`

- File: `SwiftExport.Infrastructure\Repositories\ProductRepository.cs`

### class AppStarter  `(SwiftExport.UI)`

- File: `SwiftExport.UI\AppStarter.cs`

#### Properties

- `class AppStarter`

#### Methods (signatures)

- `void ShowMain(IntPtr ownerHwnd)`
- ` if(GlobalApi.Client == null)`
- ` if(_mainWindow == null || !_mainWindow.IsLoaded)`
- ` if(ownerHwnd != IntPtr.Zero)`
- `{ if(_mainWindow.WindowState == WindowState.Minimized)`

### class CachedImageProvider  `(SwiftExport.UI.Common)`

- File: `SwiftExport.UI\Common\CachedImageProvider.cs`

#### Methods (signatures)

- `= new()`

### interface IImageProvider  `(SwiftExport.UI.Interfaces)`

- File: `SwiftExport.UI\Interfaces\IImageProvider.cs`

#### Properties

- `interface IImageProvider`

#### Methods (signatures)

- `Task<ImageSource> GetImageAsync(string pathOrUrl)`

### class GlobalApi  `(SwiftExport.UI.Services)`

- File: `SwiftExport.UI\Services\GlobalApi.cs`

#### Properties

- `class GlobalApi`

#### Methods (signatures)

- `void Initialize()`

### class RelayCommand  `(SwiftExport.UI.ViewModels.Base)`

- File: `SwiftExport.UI\ViewModels\Base\RelayCommand.cs`

#### Methods (signatures)

- ` RelayCommand(Action execute, Func<bool> canExecute = null)`
- `bool CanExecute(object parameter)`
- `void Execute(object parameter)`
- `=> _execute()`
- `void RaiseCanExecuteChanged()`

### class ProductColorViewModel  `(SwiftExport.UI.ViewModels)`

- File: `SwiftExport.UI\ViewModels\ProductColorViewModel.cs`
- Detected: Entity/Model

#### Properties

- `ObservableCollection<ProductColorDto> Colors`
- `ProductColorDto Selected`
- `RelayCommand LoadCommand`
- `RelayCommand NewCommand`
- `RelayCommand SaveCommand`
- `RelayCommand DeleteCommand`

#### Methods (signatures)

- `Raise(nameof(Selected)); RaiseCommands()`
- ` ProductColorViewModel()`
- `void Load()`
- `void NewItem()`
- `void Save()`
- `void Delete()`
- `void RaiseCommands()`

### class FrmProductColorManager  `(SwiftExport.UI.Views)`

- File: `SwiftExport.UI\Views\FrmProductColorManager.xaml.cs`

#### Methods (signatures)

- ` FrmProductColorManager()`
- ` InitializeComponent()`

### class MainWindow  `(SwiftExport.UI.Views)`

- File: `SwiftExport.UI\Views\MainWindow.xaml.cs`

#### Methods (signatures)

- ` MainWindow()`
- `void Button_Click(object sender, RoutedEventArgs e)`

### class ProductController  `(SwiftExport.WebApi.Controllers)`

- File: `SwiftExport.WebApi\Controllers\ProductController.cs`

## Notes

- UI designer files (*.Designer.cs) 已排除以减小输出体积。
- 若希望包含 designer 文件，请传入扩展参数并修改脚本。
- 解析基于简单正则与花括号计数，复杂语法建议用 Roslyn 精确解析。

