# PROJECT SUMMARY

Generated: 2026-01-11 16:18:47 (cleaned & grouped)

## Included Projects

- SwiftExport.AppLayer  
- SwiftExport.Core  
- SwiftExport.Infrastructure  
- SwiftExport.UI.Framework  
- SwiftExport.UiWinForm  
- SwiftExport.Utility  
- SwiftExportAddIn

---

## File Tree (按项目分组，省略 Designer / 大型资源)

```
|- SwiftExport.Application (AppLayer)
  |- DTOs
    |- BaseDto.cs
    |- ProductDto.cs
    |- ProductCategoryDto.cs
    |- ProductColorDto.cs
    |- SuppliersDto.cs
    |- WhiteListsDto.cs
    |- PagedResultDto.cs
    |- PageRequestDto.cs
    |- OtherDTOs\ProductQueryInput.cs
    |- OtherDTOs\ProductSearchMetaDto.cs
  |- Interfaces
    |- IBaseService.cs
    |- IProductService.cs
    |- IProductCategoryService.cs
    |- IBOMService.cs
    |- ISuppliersService.cs
    |- IWhiteListServices.cs
  |- Services
    |- BaseService.cs
    |- ProductService.cs
    |- BomService.cs
    |- CustomersService.cs
    |- SuppliersService.cs
  |- DI_AppLayer.cs
  |- SwiftExport.AppLayer.csproj

|- SwiftExport.Core
  |- Common
    |- BaseEntity.cs
    |- ServiceResult.cs
    |- PagedResult.cs
    |- Pagination.cs
    |- QueryCondition.cs
  |- Entities
    |- Product.cs
    |- ProductCategory.cs
    |- ProductColor.cs
    |- BOM.cs
    |- Customers.cs
    |- Suppliers.cs
    |- ViewUserRole.cs
  |- Interfaces
    |- IRepository.cs
    |- IProductRepository.cs
    |- IBomRepository.cs
    |- IUnitOfWork.cs
  |- SwiftExport.Core.csproj

|- SwiftExport.Infrastructure
  |- DI_Infrastructure.cs
  |- Repositories
    |- ProductRepository.cs
    |- BomRepository.cs
    |- CustomersRepository.cs
    |- ExcelSheetFieldsRepository.cs
  |- Factory
    |- SqlConnectionFactory.cs
    |- UnitOfWorkFactory.cs
    |- DataTableFactory.cs
  |- MappingCache.cs
  |- UnitOfWork.cs
  |- SwiftExport.Infrastructure.csproj

|- SwiftExport.UI.Framework
  |- FrmUtility
    |- UIStyle
      |- DataGridViewStyleExtensions.cs
      |- UITheme.cs
    |- Validation
      |- DataGridViewValidatorExtensions.cs
      |- DataGridViewValidationConfig.cs
      |- LabelExtensions.cs
    |- BindingSourceExtensions.cs
  |- AppForms / ManagerForms / DialogsForms
    |- FrmProductSelector.cs
    |- FrmProductManager.cs
    |- FrmCustomersManager.cs
    |- FrmProductColorManager.cs
    |- FrmSuppliersManager.cs
  |- Common
    |- IconProvider.cs
    |- ThreadHelper.cs
    |- SessionContext.cs
  |- DI_UIFramework.cs
  |- SwiftExport.UI.Framework.csproj

|- SwiftExport.UiWinForm
  |- DI
    |- DI_UiWinForm.cs
    |- DI_WinFormHost.cs
  |- MainFrm.cs
  |- Program.cs
  |- 静态类\ActiveUserClass.cs
  |- SwiftExport.UiWinForm.csproj

|- SwiftExport.Utility
  |- EntityListToPage.cs
  |- ExcelHelp\ClipboardHelper.cs
  |- SwiftExport.Utility.csproj

|- SwiftExportAddIn
  |- DI\AppConfig.cs
  |- DI\DI_VstoHost.cs
  |- ThisAddIn.cs
  |- SwiftExportAddInRibbon.cs
  |- TaskPanel\TaskPanelManager.cs
  |- UserCtrls\ProductSheetButtons.cs
  |- SwiftExportAddIn.csproj
```

---

## Types Overview (清洁版：仅列出类型、属性与规范方法签名，移除方法体噪声)

> 说明：方法签名已移除解析错误或方法体片段（例如单独的 `if` / `catch` 行）。若需要完整签名或参数列表的精确解析，建议使用 Roslyn。

### SwiftExport.Application (AppLayer) - DTOs & Services

- DI_AppLayer (class) — `SwiftExport.Application\DI\DI_AppLayer.cs`
  - Methods:
    - `IServiceCollection AddAppLayerServers(this IServiceCollection services, IConfiguration configuration)`

- BaseDto (class) — DTO
  - Properties:
    - `bool Loading`
    - `int ID`

- ProductDto (class) — DTO
  - Properties:
    - `string SKU`
    - `string 配置` (等其它字段已检测)

- ProductCategoryDto, ProductColorDto, SuppliersDto, WhiteListsDto, ExcelSheetFieldsMappingDto, CustomersDto (classes) — DTOs

- PagedResultDto<T> (class)
  - Properties:
    - `List<T> Items`
    - `int TotalCount`
    - `int PageIndex`
    - `int PageSize`

- IBaseService<T> (interface)
  - Common async CRUD and paging methods, e.g.:
    - `Task<ServiceResult<T>> GetByIdAsync(int id)`
    - `Task<ServiceResult<PagedResult<T>>> GetPagedListAsync(QueryCriteria queryCondition, int pageIndex, int pageSize)`

- IProductService (interface)
  - Selected signatures:
    - `Task<Product> GetBySkuAsync(string sku)`
    - `Task<int> SyncProductsAsync(IReadOnlyList<Product> productList)`

- ProductService, BomService, CustomersService, SuppliersService (classes) — 服务实现位于 Services 目录

### SwiftExport.Core - Entities & Interfaces

- BaseEntity (class)
  - Properties:
    - `short IsDelete`
    - `DateTime CreateDate`
    - `DateTime ChangeTime`

- Product (class)
  - Properties (示例):
    - `string MasterSku`, `string SKU`, `string Description`, `string CustomerCode`, `string ProductCategory`

- ProductCategory, ProductColor, BOM, Customers, Suppliers, WhiteLists, ViewUserRole (classes) — 各自的字段已提取

- IRepository<T>, IProductRepository, IBomRepository, IUnitOfWork (interfaces)
  - 示例签名：
    - `Task<int> AddAsync(T entity, IUnitOfWork uow)`
    - `Task CommitAsync()`

### SwiftExport.Infrastructure - Repositories / Factories

- DI_Infrastructure (class)
  - Methods:
    - `IServiceCollection AddInfrastructureServices(this IServiceCollection services, string connString)`

- ProductRepository, BomRepository, CustomersRepository, ExcelSheetFieldsRepository, UnitOfWork (classes)

- SqlConnectionFactory, UnitOfWorkFactory, DataTableFactory<T> (factories)

### SwiftExport.UI.Framework - UI Controls & Helpers

- FrmProductSelector (class)
  - Constructor:
    - `FrmProductSelector(IProductService productService, IMapper mapper)`
  - Event handlers (examples):
    - `void Dgv_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)`
    - `void Dgv_CellDoubleClick(object sender, DataGridViewCellEventArgs e)`

- DataGridViewValidatorExtensions (static class)
  - Methods:
    - `DataGridView SetNotNullColumnsColumns(this DataGridView dgv, params string[] cols)`
    - `DataGridView EnableTxtWrap(this DataGridView dgv, params string[] cols)` (注意：此处原始异常源于按列名查找返回 null，应在实现中加入空检查或备用匹配)

- UITheme, IconProvider, ThreadHelper, SessionContext (helpers / singletons)

### SwiftExport.UiWinForm - Host & Main

- DI_UiWinForm, DI_WinFormHost (DI bootstrap)
- MainFrm (WinForm)
  - Event handlers:
    - `void Button1_Click(object sender, EventArgs e)`

### SwiftExport.Utility & SwiftExportAddIn

- ClipboardHelper.GetClipboardMatrix(bool skipHeader = false)
- EntityListToPage (pagination helper)
- SwiftExportAddIn ribbon and task pane manager classes

---

## Detected Mapping Methods / DTO Conversions (AutoMapper Profiles)
- Found CreateMap usage in profile classes:
  - `CustomersProfiles` : `CreateMap<CustomersDto, Customers>()`
  - `ProductProfiles` : `CreateMap<ProductDto, Product>()`
  - `ProductColorProfiles` : `CreateMap<ProductColorDto, ProductColor>()`
  - `ProductCategoryProfiles` : `CreateMap<ProductCategoryDto, ProductCategory>()`
  - `WhiteListsDtoProfiles`, `ExcelSheetFieldsMappingProfiles`, `SuppliersProfiles`, `UserProfiles` etc.

---

## Notes & Recommendations

- 本文件为“清理与分组后”的 PROJECT_SUMMARY.md 源码，去除了方法体噪声和 Designer 文件，便于 AI/人工快速阅读与分析。
- 若需要：
  - 完整的每个方法签名（含参数类型与返回类型）和每个 DTO <-> Entity 的字段映射明细，建议用 Roslyn 做语法级解析（我可以生成一个小工具来完成）。
  - 或者将 Designer 文件临时包含以提取窗体控件名，也可按需开启。


