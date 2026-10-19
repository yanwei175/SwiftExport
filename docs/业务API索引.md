# 当前业务 API 源码索引

核对日期：2026-10-06。按当前Controller路由、HTTP属性和公开方法整理，包含BaseController继承动作；源码入口可逐项追溯。返回类型仅是声明，不代表实际数据、权限或每个状态路径已经运行验收。请求DTO及绑定以链接方法/Contracts为准。

权限以[EndpointPermissionFilter](../SwiftExport.WebApi/Authentication/EndpointPermissionFilter.cs)、[PermissionKeys](../SwiftExport.Contracts/DTOs/Auth/Permissions/PermissionKeys.cs)及所属用例校验为准；表中列出端点不等于匿名可访问。本文不启动宿主或访问数据库。

## AfterSalesConfirmationController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/quality-control/after-sales/{uid:guid}/confirm-demand` | ConfirmDemandAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Confirmation/AfterSalesConfirmationController.cs:16) |
| POST `api/quality-control/after-sales/{uid:guid}/cancel-demand-confirmation` | CancelDemandConfirmationAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Confirmation/AfterSalesConfirmationController.cs:20) |

## AfterSalesLifecycleController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/quality-control/after-sales/{uid:guid}/close` | CloseAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Lifecycle/AfterSalesLifecycleController.cs:16) |
| POST `api/quality-control/after-sales/{uid:guid}/reopen` | ReopenAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Lifecycle/AfterSalesLifecycleController.cs:20) |
| POST `api/quality-control/after-sales/{uid:guid}/void` | VoidAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Lifecycle/AfterSalesLifecycleController.cs:24) |

## AfterSalesMaintenanceController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/quality-control/after-sales` | CreateAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Maintenance/AfterSalesMaintenanceController.cs:16) |
| PUT `api/quality-control/after-sales/{uid:guid}` | SaveAsync | `Task<ServiceResult<AfterSalesCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Maintenance/AfterSalesMaintenanceController.cs:20) |

## AfterSalesQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/quality-control/after-sales` | SearchAsync | `Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Query/AfterSalesQueryController.cs:15) |
| GET `api/quality-control/after-sales/{uid:guid}` | GetAsync | `Task<ServiceResult<AfterSalesDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Query/AfterSalesQueryController.cs:19) |
| GET `api/quality-control/after-sales/lookups/{kind}` | LookupAsync | `Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Query/AfterSalesQueryController.cs:23) |

## FlowController（2026-10-08新增）

流量测试组接口详情见[后端使用说明](QualityControl/FlowManagement/BackendGuide.md)，共享契约见[IFlowApi](../SwiftExport.Contracts/Apis/QualityControl/FlowManagement/IFlowApi.cs)。

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/quality-control/flows/search` | GetPagedAsync | `ServiceResult<PagedResponseBase<FlowListItemDto>>` | [流量控制器](../SwiftExport.WebApi/Controllers/QualityControl/FlowManagement/FlowController.cs) |
| GET `api/quality-control/flows/{id:guid}` | GetByIdAsync | `ServiceResult<FlowDetailDto>` | [流量控制器](../SwiftExport.WebApi/Controllers/QualityControl/FlowManagement/FlowController.cs) |
| POST `api/quality-control/flows` | CreateAsync | `ServiceResult<FlowSaveResponse>` | [流量控制器](../SwiftExport.WebApi/Controllers/QualityControl/FlowManagement/FlowController.cs) |
| PUT `api/quality-control/flows/{id:guid}` | UpdateAsync | `ServiceResult<FlowSaveResponse>` | [流量控制器](../SwiftExport.WebApi/Controllers/QualityControl/FlowManagement/FlowController.cs) |
| DELETE `api/quality-control/flows/{id:guid}` | DeleteAsync | `ServiceResult<SaveItemResult>` | [流量控制器](../SwiftExport.WebApi/Controllers/QualityControl/FlowManagement/FlowController.cs) |

## AuthController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/auth/login` | Login | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:24) |
| GET `api/auth/me` | Me | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:33) |
| POST `api/auth/register` | Register | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:43) |
| POST `api/auth/change-password` | ChangePassword | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:53) |
| POST `api/auth/admin/reset-password` | ResetUserPassword | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:63) |
| GET `api/auth/admin/users` | GetUsers | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs:72) |

## BomController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/Bom/batch-save` | BatchSave | `Task<ServiceResult<BomBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/BomController.cs:18) |
| GET `api/Bom/tree/{productSku}` | GetBomTree | `Task<ServiceResult<IEnumerable<BomTreeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/BomController.cs:21) |
| GET `api/Bom/by-no/{bomNo}` | GetBomDtoByBomNo | `Task<ServiceResult<List<BomDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/BomController.cs:27) |
| GET `api/Bom/validate/{productSku}` | ValidateBom | `Task<ServiceResult<bool>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/BomController.cs:34) |
| GET `api/Bom/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/Bom` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/Bom/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/Bom/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## ClientVersionController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/client-version` | Get | `ServiceResult<ClientVersionInfo>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ClientVersions/ClientVersionController.cs:17) |

## CustomerController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/Customer/all` | GetAll | `Task<ServiceResult<IReadOnlyList<CustomerDto>>>` | 客户管理全量读取，Customers.Customer.View；传递取消令牌 |
| POST `api/Customer/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<CustomerDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/Management/CustomerController.cs:17) |
| POST `api/Customer/batch-save` | BatchSave | `Task<ServiceResult<CustomerBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/Management/CustomerController.cs:22) |
| GET `api/Customer/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/Customer` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/Customer/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/Customer/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## CustomerPoConfigController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/CustomerPoConfig/by-parties` | GetByParties | `Task<ServiceResult<CustomerPoConfigDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/PoConfigs/CustomerPoConfigController.cs:18) |
| POST `api/CustomerPoConfig/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/PoConfigs/CustomerPoConfigController.cs:27) |
| POST `api/CustomerPoConfig/batch-save` | BatchSave | `Task<ServiceResult<CustomerPoConfigBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/PoConfigs/CustomerPoConfigController.cs:34) |
| GET `api/CustomerPoConfig/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/CustomerPoConfig` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/CustomerPoConfig/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/CustomerPoConfig/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## CustomerProductController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/CustomerProduct/{id}` | GetById | `Task<ServiceResult<CustomerProductDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:19) |
| POST `api/CustomerProduct` | Create | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:24) |
| PUT `api/CustomerProduct/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:29) |
| DELETE `api/CustomerProduct/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:34) |
| POST `api/CustomerProduct/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<CustomerProductDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:38) |
| GET `api/CustomerProduct/customersku/{sku}` | GetByCustomerSku | `Task<ServiceResult<CustomerProductDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:42) |
| GET `api/CustomerProduct/haveproductCustomers` | GetAllHaveProductCustomerList | `Task<ServiceResult<IReadOnlyList<CustomerDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:46) |
| POST `api/CustomerProduct/batch-save` | BatchSave | `Task<ServiceResult<CustomerProductBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs:51) |

## DocumentNoController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/DocumentNo/next` | GetNextAsync | `Task<ServiceResult<DocumentNoResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Numbering/DocumentNoController.cs:21) |

## ExcelReadProfileController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/excel-read-profiles` | ListAsync | `Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs:17) |
| GET `api/excel-read-profiles/{uid:guid}` | GetAsync | `Task<ServiceResult<ExcelReadProfileDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs:22) |
| POST `api/excel-read-profiles` | CreateAsync | `Task<ServiceResult<ExcelReadProfileDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs:27) |
| PUT `api/excel-read-profiles/{uid:guid}` | UpdateAsync | `Task<ServiceResult<ExcelReadProfileDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs:32) |
| DELETE `api/excel-read-profiles/{uid:guid}` | DeleteAsync | `Task<ServiceResult<bool>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs:37) |

## ExecutionEligibilityController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/master-data/execution-eligibility/status` | SetStatusAsync | `Task<ServiceResult<ExecutionEligibilityStatusResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ExecutionEligibilityController.cs:19) |

## FileController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| PUT `api/files/{containerId}` | Upload | `Task<ActionResult<ServiceResult<FileUploadResultDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Files/FileController.cs:21) |
| GET `api/files/{containerId}` | Download | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Files/FileController.cs:56) |
| HEAD `api/files/{containerId}` | Download | `Task<IActionResult>` | [声明方法](../SwiftExport.WebApi/Controllers/Files/FileController.cs:56) |

## ForExcelPackingListController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/for-excel/packing-lists/check` | CheckAsync | `Task<ServiceResult<CheckPackingListImportResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPackingListController.cs:13) |

## ForExcelPurchaseOrderController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/for-excel/purchase-orders/check` | CheckAsync | `Task<ServiceResult<CheckPurchaseOrderImportResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPurchaseOrderController.cs:14) |

## ForExcelSalesOrderController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/for-excel/sales-orders/check` | CheckAsync | `Task<ServiceResult<CheckSalesOrderImportResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelSalesOrderController.cs:14) |

## GrossDemandQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/gross-demands/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Query/GrossDemandQueryController.cs:17) |
| POST `api/demand-planning/gross-demands/source-groups/search` | SearchSourceGroupsAsync | `Task<ServiceResult<List<GrossDemandSourceGroupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Query/GrossDemandQueryController.cs:23) |
| GET `api/demand-planning/gross-demands/{uid:guid}` | GetDetailAsync | `Task<ServiceResult<GrossDemandDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Query/GrossDemandQueryController.cs:29) |
| GET `api/demand-planning/gross-demands/{uid:guid}/trace` | GetTraceAsync | `Task<ServiceResult<GrossDemandTraceDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Query/GrossDemandQueryController.cs:34) |

## InboundDraftController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/inbounds/drafts` | CreateAsync | `Task<ServiceResult<InboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundDraftController.cs:17) |
| PUT `api/inventory/inbounds/drafts/{uid:guid}` | UpdateAsync | `Task<ServiceResult<InboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundDraftController.cs:20) |
| POST `api/inventory/inbounds/drafts/{uid:guid}/delete` | DeleteAsync | `Task<ServiceResult<InboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundDraftController.cs:23) |

## InboundPostingController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/inbounds/{uid:guid}/post-preview` | PreviewPostAsync | `Task<ServiceResult<InboundPostPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundPostingController.cs:17) |
| POST `api/inventory/inbounds/{uid:guid}/post` | PostAsync | `Task<ServiceResult<InboundPostResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundPostingController.cs:20) |

## InboundQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/inbounds/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundQueryController.cs:17) |
| GET `api/inventory/inbounds/{uid:guid}` | GetAsync | `Task<ServiceResult<InboundManagerDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundQueryController.cs:20) |

## InboundReversalController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/inbounds/{uid:guid}/reverse-preview` | PreviewReverseAsync | `Task<ServiceResult<InboundReversePreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundReversalController.cs:17) |
| POST `api/inventory/inbounds/{uid:guid}/reverse` | ReverseAsync | `Task<ServiceResult<InboundReverseResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundReversalController.cs:20) |

## InboundSourcesController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/inbound-sources/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundSourcesController.cs:17) |

## InventoryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/balances/search` | SearchBalancesAsync | `Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Query/InventoryController.cs:18) |
| POST `api/inventory/ledger/search` | SearchLedgerAsync | `Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Query/InventoryController.cs:21) |
| GET `api/inventory/balances/{uid:guid}/trace` | GetTraceAsync | `Task<ServiceResult<InventoryBalanceTraceDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Query/InventoryController.cs:24) |

## ManualGrossDemandController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/gross-demands/manual` | CreateManualAsync | `Task<ServiceResult<GrossDemandDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Manual/ManualGrossDemandController.cs:17) |
| PUT `api/demand-planning/gross-demands/manual/{uid:guid}` | UpdateManualAsync | `Task<ServiceResult<GrossDemandDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Manual/ManualGrossDemandController.cs:23) |
| POST `api/demand-planning/gross-demands/manual/{uid:guid}/close` | CloseManualAsync | `Task<ServiceResult<GrossDemandDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Manual/ManualGrossDemandController.cs:30) |

## MaterialDemandEstablishmentController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/net-demands/source-demands/establish` | EstablishFromSourcesAsync | `Task<ServiceResult<EstablishMaterialDemandsResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Establishment/MaterialDemandEstablishmentController.cs:16) |

## MaterialDemandQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/demand-planning/net-demands/{uid:guid}/trace` | GetTraceAsync | `Task<ServiceResult<MaterialDemandTraceDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs:21) |
| POST `api/demand-planning/net-demands/material-search` | SearchManagerAsync | `Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs:25) |
| POST `api/demand-planning/net-demands/material-source-groups` | SearchMaterialSourceGroupsAsync | `Task<ServiceResult<List<NetDemandSourceGroupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs:32) |
| GET `api/demand-planning/net-demands/source-demands/candidate-groups` | GetSourceDemandCandidateGroupsAsync | `Task<ServiceResult<List<SourceDemandCandidateGroupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs:39) |
| GET `api/demand-planning/net-demands/source-demands/candidates/{sourceType}/{sourceUid:guid}` | GetSourceDemandCandidatesBySourceAsync | `Task<ServiceResult<List<SourceDemandCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs:45) |

## MaterialDemandReversalController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/net-demands/source-demands/rollback` | RollbackSourceEstablishmentAsync | `Task<ServiceResult<RollbackMaterialDemandsResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Reversal/MaterialDemandReversalController.cs:16) |

## MaterialDemandSupplierSuggestionController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/net-demands/suggested-purchase-supplier` | UpdateSuggestedPurchaseSupplierAsync | `Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/SupplierSuggestion/MaterialDemandSupplierSuggestionController.cs:18) |

## OutboundDraftController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/outbounds/drafts` | CreateAsync | `Task<ServiceResult<OutboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundDraftController.cs:17) |
| PUT `api/inventory/outbounds/drafts/{uid:guid}` | UpdateAsync | `Task<ServiceResult<OutboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundDraftController.cs:20) |
| POST `api/inventory/outbounds/drafts/{uid:guid}/delete` | DeleteAsync | `Task<ServiceResult<OutboundDraftCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundDraftController.cs:23) |

## OutboundPostingController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/outbounds/{uid:guid}/post-preview` | PreviewPostAsync | `Task<ServiceResult<OutboundPostPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundPostingController.cs:17) |
| POST `api/inventory/outbounds/{uid:guid}/post` | PostAsync | `Task<ServiceResult<OutboundPostResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundPostingController.cs:20) |

## OutboundQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/outbounds/items/search` | SearchItemsAsync | `Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundQueryController.cs:17) |
| POST `api/inventory/outbounds/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundQueryController.cs:20) |
| GET `api/inventory/outbounds/{uid:guid}` | GetAsync | `Task<ServiceResult<OutboundManagerDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundQueryController.cs:23) |

## OutboundReversalController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/inventory/outbounds/{uid:guid}/reverse-preview` | PreviewReverseAsync | `Task<ServiceResult<OutboundReversePreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundReversalController.cs:17) |
| POST `api/inventory/outbounds/{uid:guid}/reverse` | ReverseAsync | `Task<ServiceResult<OutboundReverseResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundReversalController.cs:20) |

## PackingListController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/shipping/packing-lists/drafts/{uid:guid}/lines/{lineUid:guid}/associate-sources` | AssociateSourcesAsync | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:29) |
| POST `api/shipping/packing-lists/{uid:guid}/post-preview` | PostPreviewAsync | `Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:33) |
| POST `api/shipping/packing-lists/{uid:guid}/post` | PostAsync | `Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:38) |
| POST `api/shipping/packing-lists/{uid:guid}/reverse-preview` | ReversePreviewAsync | `Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:43) |
| POST `api/shipping/packing-lists/{uid:guid}/reverse` | ReverseAsync | `Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:48) |
| GET `api/shipping/packing-lists/{uid:guid}/trace` | TraceAsync | `Task<ActionResult<ServiceResult<PackingListTraceDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:52) |
| POST `api/shipping/packing-lists/search` | SearchAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:57) |
| GET `api/shipping/packing-lists/{uid:guid}` | GetAsync | `Task<ActionResult<ServiceResult<PackingListDetailDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:63) |
| POST `api/shipping/packing-lists/sources/search` | SearchSourcesAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:68) |
| POST `api/shipping/packing-lists/drafts` | CreateAsync | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:74) |
| PUT `api/shipping/packing-lists/drafts/{uid:guid}` | UpdateAsync | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:80) |
| POST `api/shipping/packing-lists/drafts/{uid:guid}/delete` | DeleteAsync | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs:86) |

## ProcurementDemandAssociationController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/procurement/orders/{uid}/demand-association-context` | GetDemandAssociationContextAsync | `Task<ServiceResult<ProcurementDemandAssociationContextDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationController.cs:17) |
| POST `api/procurement/orders/{uid}/demand-association-candidates` | SearchDemandAssociationCandidatesAsync | `Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationController.cs:22) |
| POST `api/procurement/orders/{uid}/demand-association-preview` | PreviewDemandAssociationAsync | `Task<ServiceResult<ProcurementDemandAssociationPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationController.cs:27) |
| POST `api/procurement/orders/{uid}/associate-demands` | AssociateDemandsAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationController.cs:32) |

## ProcurementMasterDataQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/master-data/procurement-lookups/customers/candidates/search` | SearchCustomersAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs:19) |
| POST `api/master-data/procurement-lookups/suppliers/candidates/search` | SearchSuppliersAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs:25) |
| POST `api/master-data/procurement-lookups/products/candidates/search` | SearchProductsAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs:31) |
| POST `api/master-data/procurement-lookups/customers/history-filter/search` | SearchCustomerHistoryFiltersAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs:37) |
| POST `api/master-data/procurement-lookups/suppliers/history-filter/search` | SearchSupplierHistoryFiltersAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs:43) |

## ProcurementOrderConfirmationController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/procurement/orders/{uid}/confirmation-context` | GetConfirmationContextAsync | `Task<ServiceResult<ProcurementConfirmationContextDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Confirmation/ProcurementOrderConfirmationController.cs:18) |
| POST `api/procurement/orders/{uid}/confirm` | ConfirmAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Confirmation/ProcurementOrderConfirmationController.cs:23) |

## ProcurementOrderDraftController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/orders` | CreateAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/ProcurementOrderDraftController.cs:18) |
| PUT `api/procurement/orders/{uid}/commercial` | SaveAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/ProcurementOrderDraftController.cs:23) |

## ProcurementOrderQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/orders/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/ProcurementOrderQueryController.cs:18) |
| GET `api/procurement/orders/{uid}` | GetDetailAsync | `Task<ServiceResult<PurchaseOrderDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/ProcurementOrderQueryController.cs:23) |

## ProcurementOrderReversalController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/orders/{uid}/cancel-confirmation-preview` | PreviewCancelConfirmationAsync | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:18) |
| POST `api/procurement/orders/{uid}/cancel-confirmation` | CancelConfirmationAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:23) |
| POST `api/procurement/orders/{uid}/void-preview` | PreviewVoidAsync | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:28) |
| POST `api/procurement/orders/{uid}/void` | VoidAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:33) |
| POST `api/procurement/orders/{uid}/delete-lines-preview` | PreviewDeleteLinesAsync | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:38) |
| POST `api/procurement/orders/{uid}/delete-lines` | DeleteLinesAsync | `Task<ServiceResult<ProcurementOrderCommandResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs:43) |

## ProductCategoryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/ProductCategory/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Categories/ProductCategoryController.cs:18) |
| POST `api/ProductCategory/batch-save` | BatchSave | `Task<ServiceResult<ProductCategoryBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Categories/ProductCategoryController.cs:22) |
| GET `api/ProductCategory/all` | GetAll | `Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Categories/ProductCategoryController.cs:26) |
| GET `api/ProductCategory/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/ProductCategory` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/ProductCategory/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/ProductCategory/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## ProductColorController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/ProductColor/batch-save` | BatchSave | `Task<ServiceResult<ProductColorBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Colors/ProductColorController.cs:20) |
| POST `api/ProductColor/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<ProductColorDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Colors/ProductColorController.cs:23) |
| GET `api/ProductColor/all` | GetAll | `Task<ServiceResult<IReadOnlyList<ProductColorDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Colors/ProductColorController.cs:26) |
| GET `api/ProductColor/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/ProductColor` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/ProductColor/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/ProductColor/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## ProductController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/Product/{id}` | GetById | `Task<ServiceResult<ProductDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:23) |
| POST `api/Product` | Create | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:28) |
| PUT `api/Product/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:33) |
| DELETE `api/Product/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:38) |
| POST `api/Product/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<ProductDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:42) |
| POST `api/Product/lookup` | SearchLookup | `Task<ServiceResult<List<ProductLookupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:46) |
| GET `api/Product/all` | GetAll | `Task<ServiceResult<IReadOnlyList<ProductDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:50) |
| GET `api/Product/customerCodes` | GetAllCustomerCodeList | `Task<ServiceResult<IReadOnlyList<string>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:55) |
| GET `api/Product/sku/{sku}` | GetBySku | `Task<ServiceResult<ProductDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:59) |
| POST `api/Product/batch-save` | BatchSave | `Task<ServiceResult<ProductBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:64) |
| POST `api/Product/pull-master-descriptions` | PullMasterDescriptionsAsync | `Task<ServiceResult<PullProductMasterDataResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:69) |
| POST `api/Product/pull-master-features` | PullMasterFeaturesAsync | `Task<ServiceResult<PullProductMasterDataResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:75) |
| POST `api/Product/pull-master-supplier` | PullMasterSupplierAsync | `Task<ServiceResult<PullProductMasterDataResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:81) |
| POST `api/Product/pull-master-weight-volume` | PullMasterWeightVolumeAsync | `Task<ServiceResult<PullProductMasterDataResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:87) |
| POST `api/Product/pull-master-all` | PullMasterAllAsync | `Task<ServiceResult<PullProductMasterDataResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:93) |
| GET `api/Product/default-warehouse-candidates` | GetDefaultWarehouseCandidates | `Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs:99) |

## ProductImageController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/product-images/batch` | UploadBatch | `Task<ActionResult<ServiceResult<ProductImageBatchUploadResultDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Files/ProductImageController.cs:24) |

## ProductMasterController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/ProductMaster/page` | GetPagedAsync | `Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs:20) |
| POST `api/ProductMaster/assign-category` | AssignCategoryAsync | `Task<ServiceResult<AssignProductMastersToCategoryResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs:27) |
| POST `api/ProductMaster/set-main-images` | SetMainImagesAsync | `Task<ServiceResult<BatchSetProductMasterMainImagesResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs)；按颜色顺序补齐勾选主型号默认主图，已有默认值跳过，权限 `Products.Master.Edit` |
| GET `api/ProductMaster/{productMasterId:guid}` | GetDetailAsync | `Task<ServiceResult<ProductMasterDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs:33) |
| POST `api/ProductMaster/batch-save` | BatchSaveAsync | `Task<ServiceResult<ProductMasterBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs:40) |
| POST `api/ProductMaster/save-detail` | SaveDetailAsync | `Task<ServiceResult<SaveProductMasterDetailResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs:47) |

## ProductNeutralController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/ProductNeutral/{productMasterId:guid}/batch-save` | BatchSaveAsync | `Task<ServiceResult<ProductMasterDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductNeutrals/ProductNeutralController.cs:17) |

## ProductNeutralManagerController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/ProductNeutralManager/page` | GetPagedAsync | `Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductNeutrals/ProductNeutralManagerController.cs:19) |
| POST `api/ProductNeutralManager/batch-save` | BatchSaveAsync | `Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Products/ProductNeutrals/ProductNeutralManagerController.cs:26) |

## PurchaseDraftCreationController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/purchase-drafts/create` | CreateAsync | `Task<ServiceResult<CreatePurchaseDraftsResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/PurchaseDraftCreationController.cs:17) |

## PurchaseDraftPreviewController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/purchase-drafts/preview` | PreviewAsync | `Task<ServiceResult<PurchaseDraftPreviewResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/PurchaseDraftPreviewController.cs:17) |

## PurchaseDraftSourceController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/procurement/purchase-drafts/candidates/source-groups` | GetCandidateSourceGroupsAsync | `Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Sources/PurchaseDraftSourceController.cs:17) |
| POST `api/procurement/purchase-drafts/candidates` | GetCandidatesAsync | `Task<ServiceResult<List<PurchaseDraftCandidateDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Sources/PurchaseDraftSourceController.cs:22) |

## PurchaseOrderManagerQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/order-manager/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/PurchaseOrderManagerQueryController.cs:17) |
| GET `api/procurement/order-manager/{purchaseOrderId:guid}/lines` | GetLinesAsync | `Task<ServiceResult<IReadOnlyList<PurchaseOrderManagerLineDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/PurchaseOrderManagerQueryController.cs:26) |

## PurchaseOrderProgressQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/purchase-order-progress/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressQueryController.cs:18) |
| GET `api/procurement/purchase-order-progress/{purchaseOrderId:guid}/detail` | GetDetailAsync | `Task<ServiceResult<PurchaseOrderProgressDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressQueryController.cs:25) |

## PurchaseOrderProgressReportController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/procurement/purchase-order-progress/{purchaseOrderId:guid}/reports` | ReportAsync | `Task<ServiceResult<PurchaseOrderProgressReportResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressReportController.cs:19) |

## PurchaseOrderTraceController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/procurement/purchase-order-trace/{purchaseOrderId:guid}` | GetAsync | `Task<ServiceResult<PurchaseOrderTraceDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Trace/PurchaseOrderTraceController.cs:17) |

## RbacPermissionsController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/system/permissions` | List | `Task<ServiceResult<List<ManagedPermissionDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacPermissionsController.cs:17) |

## RbacRolesController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/system/roles/page` | Search | `Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:18) |
| GET `api/system/roles/{id:guid}` | Get | `Task<ServiceResult<ManagedRoleDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:23) |
| POST `api/system/roles` | Create | `Task<ServiceResult<ManagedRoleDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:29) |
| PUT `api/system/roles/{id:guid}` | Update | `Task<ServiceResult<ManagedRoleDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:35) |
| PUT `api/system/roles/{id:guid}/status` | SetStatus | `Task<ServiceResult<ManagedRoleDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:41) |
| DELETE `api/system/roles/{id:guid}` | Delete | `Task<ServiceResult<bool>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:47) |
| GET `api/system/roles/{id:guid}/permissions` | GetPermissions | `Task<ServiceResult<RolePermissionsDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:52) |
| PUT `api/system/roles/{id:guid}/permissions` | ReplacePermissions | `Task<ServiceResult<RolePermissionsDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs:58) |

## RbacUsersController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/system/users/page` | Search | `Task<ServiceResult<PagedResponseBase<ManagedUserDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:18) |
| GET `api/system/users/{id:guid}` | Get | `Task<ServiceResult<ManagedUserDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:23) |
| PUT `api/system/users/{id:guid}` | Update | `Task<ServiceResult<ManagedUserDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:29) |
| PUT `api/system/users/{id:guid}/status` | SetStatus | `Task<ServiceResult<ManagedUserDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:35) |
| GET `api/system/users/{id:guid}/roles` | GetRoles | `Task<ServiceResult<UserRolesDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:40) |
| PUT `api/system/users/{id:guid}/roles` | ReplaceRoles | `Task<ServiceResult<UserRolesDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:46) |
| GET `api/system/users/{id:guid}/effective-permissions` | GetEffectivePermissions | `Task<ServiceResult<List<string>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs:51) |

## SalesFulfillmentQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/sales-orders/fulfillment/{uid:guid}` | GetAsync | `Task<ServiceResult<SalesFulfillmentDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesFulfillmentQueryController.cs:14) |

## SalesOrderConfirmationController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/sales-orders/detail/{uid:guid}/confirm` | ConfirmAsync | `Task<ServiceResult<SalesOrderConfirmationResultDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Confirmation/SalesOrderConfirmationController.cs:16) |
| POST `api/sales-orders/detail/{uid:guid}/cancel-confirmation` | CancelConfirmationAsync | `Task<ServiceResult<SalesOrderConfirmationResultDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Confirmation/SalesOrderConfirmationController.cs:21) |

## SalesOrderDraftController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/sales-orders/detail/save` | SaveAsync | `Task<ServiceResult<SaveSalesOrderResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Draft/SalesOrderDraftController.cs:15) |
| POST `api/sales-orders/detail/{uid:guid}/delete-draft` | DeleteDraftAsync | `Task<ServiceResult<bool>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Draft/SalesOrderDraftController.cs:19) |

## SalesOrderLifecycleController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/sales-orders/detail/{uid:guid}/close` | CloseAsync | `Task<ServiceResult<SalesOrderDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Lifecycle/SalesOrderLifecycleController.cs:15) |

## SalesOrderQueryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/sales-orders/manager/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs:15) |
| GET `api/sales-orders/manager/customer-codes` | GetCustomerCodesAsync | `Task<ServiceResult<List<string>>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs:20) |
| GET `api/sales-orders/detail/{uid:guid}` | GetAsync | `Task<ServiceResult<SalesOrderDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs:25) |
| POST `api/sales-orders/detail/products/search` | SearchProductsAsync | `Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs:30) |
| POST `api/sales-orders/detail/customers/search` | SearchCustomersAsync | `Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs:35) |

## SourceDemandDeliveryController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/demand-planning/source-demands/delivery/search` | SearchAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Delivery/SourceDemandDeliveryController.cs:25) |
| GET `api/demand-planning/source-demands/delivery/{grossDemandId:guid}` | GetAsync | `Task<ActionResult<ServiceResult<SourceDemandDeliveryItemDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Delivery/SourceDemandDeliveryController.cs:32) |

## SupplierController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/Supplier/page` | GetPagedList | `Task<ServiceResult<PagedResponseBase<SupplierDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/Management/SupplierController.cs:17) |
| POST `api/Supplier/batch-save` | BatchSave | `Task<ServiceResult<SupplierBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/Management/SupplierController.cs:22) |
| GET `api/Supplier/all` | GetAll | `Task<ServiceResult<IReadOnlyList<SupplierDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/Management/SupplierController.cs:26) |
| GET `api/Supplier/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/Supplier` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/Supplier/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/Supplier/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## SupplierProductController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/SupplierProduct/{id}` | GetById | `Task<ServiceResult<SupplierProductDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:23) |
| POST `api/SupplierProduct` | Create | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:28) |
| PUT `api/SupplierProduct/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:33) |
| DELETE `api/SupplierProduct/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:38) |
| POST `api/SupplierProduct/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<SupplierProductDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:42) |
| POST `api/SupplierProduct/batch-save` | BatchSave | `Task<ServiceResult<SupplierProductBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:47) |
| POST `api/SupplierProduct/set-product-default-supplier` | SetProductDefaultSupplier | `Task<ServiceResult<SetProductDefaultSupplierResult>>` | [声明方法](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs:52) |

## SysDicCurrencyController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/SysDicCurrency/all` | GetAll | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:19) |
| GET `api/SysDicCurrency/base` | GetBaseCurrency | `Task<ServiceResult<SysDicCurrencyDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:26) |
| GET `api/SysDicCurrency/enabled` | GetEnabledList | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:33) |
| GET `api/SysDicCurrency/defaultbasecurrency` | GetDefaultBaseCurrency | `Task<ServiceResult<SysDicCurrencyDetailDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:39) |
| POST `api/SysDicCurrency/setnewbase` | SetNewBaseCurrency | `Task<ServiceResult<int>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:45) |
| POST `api/SysDicCurrency/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:52) |
| POST `api/SysDicCurrency/batch-save` | BatchSave | `Task<ServiceResult<SysDicCurrencyBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs:57) |
| GET `api/SysDicCurrency/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/SysDicCurrency` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/SysDicCurrency/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/SysDicCurrency/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## SysDicCurrencyExchangeController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/SysDicCurrencyExchange/all` | GetAll | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExchangeRates/SysDicCurrencyExchangeController.cs:19) |
| POST `api/SysDicCurrencyExchange/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExchangeRates/SysDicCurrencyExchangeController.cs:22) |
| POST `api/SysDicCurrencyExchange/batch-save` | BatchSave | `Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/ExchangeRates/SysDicCurrencyExchangeController.cs:27) |
| GET `api/SysDicCurrencyExchange/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/SysDicCurrencyExchange` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/SysDicCurrencyExchange/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/SysDicCurrencyExchange/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## SysDictDataController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| POST `api/SysDictData/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<SysDictDataDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Data/SysDictDataController.cs:19) |
| POST `api/SysDictData/batch-save` | BatchSave | `Task<ServiceResult<SysDictDataBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Data/SysDictDataController.cs:24) |
| GET `api/SysDictData/all` | GetAll | `Task<ServiceResult<IReadOnlyList<SysDictDataDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Data/SysDictDataController.cs:27) |
| GET `api/SysDictData/by-code/{dictCode}` | GetByCode | `Task<ServiceResult<IReadOnlyList<SysDictDataDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Data/SysDictDataController.cs:35) |
| GET `api/SysDictData/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/SysDictData` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/SysDictData/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/SysDictData/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## SysDictTypeController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/SysDictType/all` | GetAllTypes | `Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Types/SysDictTypeController.cs:23) |
| GET `api/SysDictType/group-tree` | GetDictTypeGroupTree | `Task<ServiceResult<List<DictTypeGroupTreeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Types/SysDictTypeController.cs:50) |
| POST `api/SysDictType/page` | GetPaged | `Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Types/SysDictTypeController.cs:56) |
| POST `api/SysDictType/batch-save` | BatchSave | `Task<ServiceResult<SysDictTypeBatchSaveResponse>>` | [声明方法](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Types/SysDictTypeController.cs:61) |
| GET `api/SysDictType/{id}` | GetById | `Task<ServiceResult<TDto>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:17) |
| POST `api/SysDictType` | Create | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:24) |
| PUT `api/SysDictType/{id}` | Update | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:31) |
| DELETE `api/SysDictType/{id}` | Delete | `Task<ServiceResult<SaveItemResult>>` | [继承基类](../SwiftExport.WebApi/Controllers/Base/BaseController.cs:39) |

## WarehouseSitesController

| HTTP及路由 | 动作 | 返回契约 | 源码 |
| --- | --- | --- | --- |
| GET `api/inventory/warehouse-sites/picker` | GetWarehousePickerPagedAsync | `Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:20) |
| POST `api/inventory/warehouse-sites/candidates/search` | SearchCandidatesAsync | `Task<ActionResult<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:27) |
| POST `api/inventory/warehouse-sites/search` | SearchAsync | `Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:52) |
| POST `api/inventory/warehouse-sites/suppliers/search` | SearchSuppliersAsync | `Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:55) |
| GET `api/inventory/warehouse-sites/{uid:guid}` | GetAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:58) |
| POST `api/inventory/warehouse-sites/supplier-virtual` | CreateSupplierVirtualAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:61) |
| PUT `api/inventory/warehouse-sites/{uid:guid}` | UpdateAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:64) |
| POST `api/inventory/warehouse-sites/{uid:guid}/deactivate` | DeactivateAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:67) |
| POST `api/inventory/warehouse-sites/{uid:guid}/activate` | ActivateAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:70) |
| DELETE `api/inventory/warehouse-sites/{uid:guid}` | DeleteAsync | `Task<ServiceResult<WarehouseSiteDto>>` | [声明方法](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs:73) |

## 共享客户端契约

Refit声明放[Contracts/Apis](../SwiftExport.Contracts/Apis)，用例DTO放[Contracts/DTOs](../SwiftExport.Contracts/DTOs)；实际WPF消费者由[NavigationRouteRegistry](../SwiftExport.Client.Wpf/Navigation/NavigationRouteRegistry.cs)及对应ViewModel确认。模块对应关系见[能力地图](../.agents/references/业务模块能力与页面契约治理规则.md)。泛型继承CRUD不替代模块命令边界。

## OrderTracking（2026-10-19，工作树实现）

订单跟踪为独立只读查询，全部要求 OrderTracking.View；下游额外权限及数量边界见[设计与交付](订单跟踪首阶段设计与交付.md)。共享调用契约为 [IOrderTrackingApi](../SwiftExport.Contracts/Apis/OrderTracking/IOrderTrackingApi.cs)。实现尚未合并、部署；不以本索引代替运行验收。

| HTTP及路由 | 用例 | 返回数据 |
| --- | --- | --- |
| POST `api/order-tracking/sources/search` | 来源分组分页 | `PagedResponseBase<OrderTrackingSourceGroupDto>` |
| POST `api/order-tracking/overview/search` | 命中成品需求分页 | `PagedResponseBase<OrderTrackingListItemDto>` |
| GET `api/order-tracking/customer-codes` | 独立客户代码候选 | `List<string>` |
| GET `api/order-tracking/demands/{grossDemandId}` | 需求基础快照 | `DemandTrackingHeaderDto` |
| GET `api/order-tracking/demands/{grossDemandId}/materials` | 物料分解及修订 | `List<DemandTrackingMaterialDto>` |
| GET `api/order-tracking/demands/{grossDemandId}/purchases` | 采购关联贡献与整行事实 | `List<DemandTrackingPurchaseDto>` |
| GET `api/order-tracking/demands/{grossDemandId}/receipts` | 关联采购行收货证据 | `DemandTrackingReceiptSectionDto` |
| GET `api/order-tracking/demands/{grossDemandId}/deliveries` | 正式交付与反向历史 | `DemandTrackingDeliverySectionDto` |

以上数据均由 `ServiceResult<T>` 包装；权限、服务失败和未知证据不伪装为空集合或零进度。
