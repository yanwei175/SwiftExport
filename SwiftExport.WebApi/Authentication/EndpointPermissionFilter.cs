using System.Collections;
using SwiftExport.Contracts.DTOs.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Controllers;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.WebApi.Authentication;

/// <summary>按阶段 01 冻结的 Controller.Action 矩阵执行具体权限检查；未列出的业务 Action 拒绝访问。</summary>
public sealed class EndpointPermissionFilter(ICurrentUser currentUser, IPermissionEvaluationService permissions) : IAsyncActionFilter
{
    private static readonly Dictionary<string, string> SingleKeys = new(StringComparer.Ordinal)
    {
        ["AuthController.Register"] = PermissionKeys.SystemUsersManage,
        ["AuthController.ResetUserPassword"] = PermissionKeys.SystemUsersManage,
        ["AuthController.GetUsers"] = PermissionKeys.SystemUsersView,
        ["CustomerProductController.GetById"] = PermissionKeys.CustomersProductsView,
        ["CustomerProductController.Create"] = PermissionKeys.CustomersProductsCreate,
        ["CustomerProductController.Update"] = PermissionKeys.CustomersProductsEdit,
        ["CustomerProductController.Delete"] = PermissionKeys.CustomersProductsDelete,
        ["CustomerProductController.GetPaged"] = PermissionKeys.CustomersProductsView,
        ["CustomerProductController.GetByCustomerSku"] = PermissionKeys.CustomersProductsView,
        ["CustomerProductController.GetAllHaveProductCustomerList"] = PermissionKeys.CustomersProductsView,
        ["CustomerController.GetPaged"] = PermissionKeys.CustomersCustomerView,
        ["CustomerController.GetById"] = PermissionKeys.CustomersCustomerView,
        ["CustomerController.Create"] = PermissionKeys.CustomersCustomerCreate,
        ["CustomerController.Update"] = PermissionKeys.CustomersCustomerEdit,
        ["CustomerController.Delete"] = PermissionKeys.CustomersCustomerDelete,
        ["CustomerPoConfigController.GetByParties"] = PermissionKeys.CustomersPoConfigView,
        ["CustomerPoConfigController.GetPaged"] = PermissionKeys.CustomersPoConfigView,
        ["CustomerPoConfigController.GetById"] = PermissionKeys.CustomersPoConfigView,
        ["CustomerPoConfigController.Create"] = PermissionKeys.CustomersPoConfigCreate,
        ["CustomerPoConfigController.Update"] = PermissionKeys.CustomersPoConfigEdit,
        ["CustomerPoConfigController.Delete"] = PermissionKeys.CustomersPoConfigDelete,
        ["MaterialDemandEstablishmentController.EstablishFromSourcesAsync"] = PermissionKeys.DemandPlanningMaterialEstablish,
        ["MaterialDemandQueryController.SearchManagerAsync"] = PermissionKeys.DemandPlanningMaterialView,
        ["MaterialDemandQueryController.GetTraceAsync"] = PermissionKeys.DemandPlanningMaterialView,
        ["MaterialDemandQueryController.SearchMaterialSourceGroupsAsync"] = PermissionKeys.DemandPlanningMaterialView,
        ["MaterialDemandQueryController.GetSourceDemandCandidateGroupsAsync"] = PermissionKeys.DemandPlanningMaterialView,
        ["MaterialDemandQueryController.GetSourceDemandCandidatesBySourceAsync"] = PermissionKeys.DemandPlanningMaterialView,
        ["MaterialDemandReversalController.RollbackSourceEstablishmentAsync"] = PermissionKeys.DemandPlanningMaterialRollback,
        ["MaterialDemandSupplierSuggestionController.UpdateSuggestedPurchaseSupplierAsync"] = PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage,
        ["SourceDemandDeliveryController.SearchAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["SourceDemandDeliveryController.GetAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["ManualGrossDemandController.CreateManualAsync"] = PermissionKeys.DemandPlanningSourceManualManage,
        ["ManualGrossDemandController.UpdateManualAsync"] = PermissionKeys.DemandPlanningSourceManualManage,
        ["ManualGrossDemandController.CloseManualAsync"] = PermissionKeys.DemandPlanningSourceManualManage,
        ["GrossDemandQueryController.SearchAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["GrossDemandQueryController.SearchSourceGroupsAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["GrossDemandQueryController.GetDetailAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["GrossDemandQueryController.GetTraceAsync"] = PermissionKeys.DemandPlanningSourceView,
        ["FileController.Upload"] = PermissionKeys.SystemFilesManage,
        ["ProductImageController.UploadBatch"] = PermissionKeys.SystemFilesManage,
        ["InboundDraftController.CreateAsync"] = PermissionKeys.InventoryInboundCreate,
        ["InboundDraftController.UpdateAsync"] = PermissionKeys.InventoryInboundEdit,
        ["InboundDraftController.DeleteAsync"] = PermissionKeys.InventoryInboundDelete,
        ["InboundPostingController.PreviewPostAsync"] = PermissionKeys.InventoryInboundPost,
        ["InboundPostingController.PostAsync"] = PermissionKeys.InventoryInboundPost,
        ["InboundQueryController.SearchAsync"] = PermissionKeys.InventoryInboundView,
        ["InboundQueryController.GetAsync"] = PermissionKeys.InventoryInboundView,
        ["InboundReversalController.PreviewReverseAsync"] = PermissionKeys.InventoryInboundReverse,
        ["InboundReversalController.ReverseAsync"] = PermissionKeys.InventoryInboundReverse,
        ["InboundSourcesController.SearchAsync"] = PermissionKeys.InventoryInboundView,
        ["OutboundDraftController.CreateAsync"] = PermissionKeys.InventoryOutboundCreate,
        ["OutboundDraftController.UpdateAsync"] = PermissionKeys.InventoryOutboundEdit,
        ["OutboundDraftController.DeleteAsync"] = PermissionKeys.InventoryOutboundDelete,
        ["OutboundPostingController.PreviewPostAsync"] = PermissionKeys.InventoryOutboundPost,
        ["OutboundPostingController.PostAsync"] = PermissionKeys.InventoryOutboundPost,
        ["OutboundQueryController.SearchItemsAsync"] = PermissionKeys.InventoryOutboundView,
        ["OutboundQueryController.SearchAsync"] = PermissionKeys.InventoryOutboundView,
        ["OutboundQueryController.GetAsync"] = PermissionKeys.InventoryOutboundView,
        ["OutboundReversalController.PreviewReverseAsync"] = PermissionKeys.InventoryOutboundReverse,
        ["OutboundReversalController.ReverseAsync"] = PermissionKeys.InventoryOutboundReverse,
        ["InventoryController.SearchBalancesAsync"] = PermissionKeys.InventoryBalanceView,
        ["InventoryController.SearchLedgerAsync"] = PermissionKeys.InventoryBalanceView,
        ["InventoryController.GetTraceAsync"] = PermissionKeys.InventoryBalanceView,
        ["WarehouseSitesController.GetWarehousePickerPagedAsync"] = PermissionKeys.InventoryWarehouseView,
        ["WarehouseSitesController.SearchAsync"] = PermissionKeys.InventoryWarehouseView,
        ["WarehouseSitesController.SearchSuppliersAsync"] = PermissionKeys.InventoryWarehouseView,
        ["WarehouseSitesController.GetAsync"] = PermissionKeys.InventoryWarehouseView,
        ["WarehouseSitesController.CreateSupplierVirtualAsync"] = PermissionKeys.InventoryWarehouseManage,
        ["WarehouseSitesController.UpdateAsync"] = PermissionKeys.InventoryWarehouseManage,
        ["WarehouseSitesController.DeactivateAsync"] = PermissionKeys.InventoryWarehouseManage,
        ["WarehouseSitesController.ActivateAsync"] = PermissionKeys.InventoryWarehouseManage,
        ["WarehouseSitesController.DeleteAsync"] = PermissionKeys.InventoryWarehouseManage,
        ["ExecutionEligibilityController.SetStatusAsync"] = PermissionKeys.MasterDataEligibilityManage,
        ["ProcurementMasterDataQueryController.SearchCustomersAsync"] = PermissionKeys.ProcurementLookupView,
        ["ProcurementMasterDataQueryController.SearchSuppliersAsync"] = PermissionKeys.ProcurementLookupView,
        ["ProcurementMasterDataQueryController.SearchProductsAsync"] = PermissionKeys.ProcurementLookupView,
        ["ProcurementMasterDataQueryController.SearchCustomerHistoryFiltersAsync"] = PermissionKeys.ProcurementLookupView,
        ["ProcurementMasterDataQueryController.SearchSupplierHistoryFiltersAsync"] = PermissionKeys.ProcurementLookupView,
        ["ProcurementOrderConfirmationController.GetConfirmationContextAsync"] = PermissionKeys.ProcurementOrderConfirm,
        ["ProcurementOrderConfirmationController.ConfirmAsync"] = PermissionKeys.ProcurementOrderConfirm,
        ["ProcurementOrderDraftController.CreateAsync"] = PermissionKeys.ProcurementOrderCreate,
        ["ProcurementOrderDraftController.SaveAsync"] = PermissionKeys.ProcurementOrderEdit,
        ["PurchaseDraftCreationController.CreateAsync"] = PermissionKeys.ProcurementOrderCreate,
        ["PurchaseDraftPreviewController.PreviewAsync"] = PermissionKeys.ProcurementOrderCreate,
        ["PurchaseOrderProgressQueryController.SearchAsync"] = PermissionKeys.ProcurementProgressView,
        ["PurchaseOrderProgressQueryController.GetDetailAsync"] = PermissionKeys.ProcurementProgressView,
        ["PurchaseOrderProgressReportController.ReportAsync"] = PermissionKeys.ProcurementProgressReport,
        ["ProcurementOrderQueryController.SearchAsync"] = PermissionKeys.ProcurementOrderRead,
        ["ProcurementOrderQueryController.GetDetailAsync"] = PermissionKeys.ProcurementOrderRead,
        ["PurchaseOrderManagerQueryController.SearchAsync"] = PermissionKeys.ProcurementOrderRead,
        ["PurchaseOrderManagerQueryController.GetLinesAsync"] = PermissionKeys.ProcurementOrderRead,
        ["PurchaseOrderTraceController.GetAsync"] = PermissionKeys.ProcurementOrderRead,
        ["ProcurementOrderReversalController.PreviewCancelConfirmationAsync"] = PermissionKeys.ProcurementOrderConfirm,
        ["ProcurementOrderReversalController.CancelConfirmationAsync"] = PermissionKeys.ProcurementOrderConfirm,
        ["ProcurementOrderReversalController.PreviewVoidAsync"] = PermissionKeys.ProcurementOrderVoid,
        ["ProcurementOrderReversalController.VoidAsync"] = PermissionKeys.ProcurementOrderVoid,
        ["ProcurementOrderReversalController.PreviewDeleteLinesAsync"] = PermissionKeys.ProcurementOrderEdit,
        ["ProcurementOrderReversalController.DeleteLinesAsync"] = PermissionKeys.ProcurementOrderEdit,
        ["PurchaseDraftSourceController.GetCandidateSourceGroupsAsync"] = PermissionKeys.ProcurementOrderRead,
        ["PurchaseDraftSourceController.GetCandidatesAsync"] = PermissionKeys.ProcurementOrderRead,
        ["BomController.GetBomTree"] = PermissionKeys.ProductsBomView,
        ["BomController.GetBomDtoByBomNo"] = PermissionKeys.ProductsBomView,
        ["BomController.ValidateBom"] = PermissionKeys.ProductsBomView,
        ["BomController.GetById"] = PermissionKeys.ProductsBomView,
        ["BomController.Create"] = PermissionKeys.ProductsBomCreate,
        ["BomController.Update"] = PermissionKeys.ProductsBomEdit,
        ["BomController.Delete"] = PermissionKeys.ProductsBomDelete,
        ["ProductController.GetById"] = PermissionKeys.ProductsProductView,
        ["ProductController.Create"] = PermissionKeys.ProductsProductCreate,
        ["ProductController.Update"] = PermissionKeys.ProductsProductEdit,
        ["ProductController.Delete"] = PermissionKeys.ProductsProductDelete,
        ["ProductController.GetPaged"] = PermissionKeys.ProductsProductView,
        ["ProductController.SearchLookup"] = PermissionKeys.ProductsProductView,
        ["ProductController.GetAll"] = PermissionKeys.ProductsProductView,
        ["ProductController.GetAllCustomerCodeList"] = PermissionKeys.ProductsProductView,
        ["ProductController.GetBySku"] = PermissionKeys.ProductsProductView,
        ["ProductController.GetDefaultWarehouseCandidates"] = PermissionKeys.ProductsProductView,
        ["ProductController.PullMasterDescriptionsAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductController.PullMasterFeaturesAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductController.PullMasterSupplierAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductController.PullMasterWeightVolumeAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductController.PullMasterAllAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductCategoryController.GetPaged"] = PermissionKeys.ProductsCategoryView,
        ["ProductCategoryController.GetAll"] = PermissionKeys.ProductsCategoryView,
        ["ProductCategoryController.GetById"] = PermissionKeys.ProductsCategoryView,
        ["ProductCategoryController.Create"] = PermissionKeys.ProductsCategoryCreate,
        ["ProductCategoryController.Update"] = PermissionKeys.ProductsCategoryEdit,
        ["ProductCategoryController.Delete"] = PermissionKeys.ProductsCategoryDelete,
        ["ProductColorController.GetPaged"] = PermissionKeys.ProductsColorView,
        ["ProductColorController.GetAll"] = PermissionKeys.ProductsColorView,
        ["ProductColorController.GetById"] = PermissionKeys.ProductsColorView,
        ["ProductColorController.Create"] = PermissionKeys.ProductsColorCreate,
        ["ProductColorController.Update"] = PermissionKeys.ProductsColorEdit,
        ["ProductColorController.Delete"] = PermissionKeys.ProductsColorDelete,
        ["ProductMasterController.GetPagedAsync"] = PermissionKeys.ProductsMasterView,
        ["ProductMasterController.AssignCategoryAsync"] = PermissionKeys.ProductsMasterEdit,
        ["ProductMasterController.GetDetailAsync"] = PermissionKeys.ProductsMasterView,
        ["ProductMasterController.SaveDetailAsync"] = PermissionKeys.ProductsMasterEdit,
        ["ProductMasterController.SyncDescriptionsAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductMasterController.SyncFeaturesAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductMasterController.SyncSupplierAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductMasterController.SyncWeightVolumeAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductMasterController.SyncAllAsync"] = PermissionKeys.ProductsProductEdit,
        ["ProductNeutralManagerController.GetPagedAsync"] = PermissionKeys.ProductsNeutralView,
        ["AfterSalesConfirmationController.ConfirmDemandAsync"] = PermissionKeys.AfterSalesConfirm,
        ["AfterSalesConfirmationController.CancelDemandConfirmationAsync"] = PermissionKeys.AfterSalesCancelConfirmation,
        ["AfterSalesLifecycleController.CloseAsync"] = PermissionKeys.AfterSalesLifecycle,
        ["AfterSalesLifecycleController.ReopenAsync"] = PermissionKeys.AfterSalesLifecycle,
        ["AfterSalesLifecycleController.VoidAsync"] = PermissionKeys.AfterSalesLifecycle,
        ["AfterSalesMaintenanceController.CreateAsync"] = PermissionKeys.AfterSalesMaintain,
        ["AfterSalesMaintenanceController.SaveAsync"] = PermissionKeys.AfterSalesMaintain,
        ["AfterSalesQueryController.SearchAsync"] = PermissionKeys.QualityControlAfterSalesView,
        ["AfterSalesQueryController.GetAsync"] = PermissionKeys.QualityControlAfterSalesView,
        ["AfterSalesQueryController.LookupAsync"] = PermissionKeys.QualityControlAfterSalesView,
        ["SalesOrderConfirmationController.ConfirmAsync"] = PermissionKeys.SalesOrderConfirm,
        ["SalesOrderConfirmationController.CancelConfirmationAsync"] = PermissionKeys.SalesOrderCancelConfirmation,
        ["SalesOrderDraftController.SaveAsync"] = PermissionKeys.SalesOrderMaintain,
        ["SalesOrderDraftController.DeleteDraftAsync"] = PermissionKeys.SalesOrderDelete,
        ["SalesOrderLifecycleController.CloseAsync"] = PermissionKeys.SalesOrderLifecycle,
        ["SalesOrderQueryController.SearchAsync"] = PermissionKeys.SalesOrderView,
        ["SalesFulfillmentQueryController.GetAsync"] = PermissionKeys.SalesOrderView,
        ["SalesOrderQueryController.GetCustomerCodesAsync"] = PermissionKeys.SalesOrderView,
        ["SalesOrderQueryController.GetAsync"] = PermissionKeys.SalesOrderView,
        ["SalesOrderQueryController.SearchProductsAsync"] = PermissionKeys.SalesOrderView,
        ["SalesOrderQueryController.SearchCustomersAsync"] = PermissionKeys.SalesOrderView,
        ["PackingListController.PostPreviewAsync"] = PermissionKeys.PackingListPost,
        ["PackingListController.PostAsync"] = PermissionKeys.PackingListPost,
        ["PackingListController.ReversePreviewAsync"] = PermissionKeys.PackingListReverse,
        ["PackingListController.ReverseAsync"] = PermissionKeys.PackingListReverse,
        ["PackingListController.TraceAsync"] = PermissionKeys.ShippingPackingListView,
        ["PackingListController.SearchAsync"] = PermissionKeys.ShippingPackingListView,
        ["PackingListController.GetAsync"] = PermissionKeys.ShippingPackingListView,
        ["PackingListController.SearchSourcesAsync"] = PermissionKeys.ShippingPackingListView,
        ["PackingListController.CreateAsync"] = PermissionKeys.ShippingPackingListCreate,
        ["PackingListController.UpdateAsync"] = PermissionKeys.ShippingPackingListEdit,
        ["PackingListController.DeleteAsync"] = PermissionKeys.ShippingPackingListDelete,
        ["SupplierController.GetPagedList"] = PermissionKeys.SuppliersSupplierView,
        ["SupplierController.GetAll"] = PermissionKeys.SuppliersSupplierView,
        ["SupplierController.GetById"] = PermissionKeys.SuppliersSupplierView,
        ["SupplierController.Create"] = PermissionKeys.SuppliersSupplierCreate,
        ["SupplierController.Update"] = PermissionKeys.SuppliersSupplierEdit,
        ["SupplierController.Delete"] = PermissionKeys.SuppliersSupplierDelete,
        ["SupplierProductController.GetById"] = PermissionKeys.SuppliersProductsView,
        ["SupplierProductController.Create"] = PermissionKeys.SuppliersProductsCreate,
        ["SupplierProductController.Update"] = PermissionKeys.SuppliersProductsEdit,
        ["SupplierProductController.Delete"] = PermissionKeys.SuppliersProductsDelete,
        ["SupplierProductController.GetPaged"] = PermissionKeys.SuppliersProductsView,
        ["SupplierProductController.SetProductDefaultSupplier"] = PermissionKeys.SuppliersProductsEdit,
        ["SysDicCurrencyController.GetAll"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.GetBaseCurrency"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.GetEnabledList"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.GetDefaultBaseCurrency"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.SetNewBaseCurrency"] = PermissionKeys.SystemCurrencyManage,
        ["SysDicCurrencyController.GetPaged"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.GetById"] = PermissionKeys.SystemCurrencyView,
        ["SysDicCurrencyController.Create"] = PermissionKeys.SystemCurrencyCreate,
        ["SysDicCurrencyController.Update"] = PermissionKeys.SystemCurrencyEdit,
        ["SysDicCurrencyController.Delete"] = PermissionKeys.SystemCurrencyDelete,
        ["SysDictDataController.GetPaged"] = PermissionKeys.SystemDictionaryView,
        ["SysDictDataController.GetAll"] = PermissionKeys.SystemDictionaryView,
        ["SysDictDataController.GetByCode"] = PermissionKeys.SystemDictionaryView,
        ["SysDictDataController.GetById"] = PermissionKeys.SystemDictionaryView,
        ["SysDictDataController.Create"] = PermissionKeys.SystemDictionaryCreate,
        ["SysDictDataController.Update"] = PermissionKeys.SystemDictionaryEdit,
        ["SysDictDataController.Delete"] = PermissionKeys.SystemDictionaryDelete,
        ["SysDictTypeController.GetAllTypes"] = PermissionKeys.SystemDictionaryView,
        ["SysDictTypeController.GetDictTypeGroupTree"] = PermissionKeys.SystemDictionaryView,
        ["SysDictTypeController.GetPaged"] = PermissionKeys.SystemDictionaryView,
        ["SysDictTypeController.GetById"] = PermissionKeys.SystemDictionaryView,
        ["SysDictTypeController.Create"] = PermissionKeys.SystemDictionaryCreate,
        ["SysDictTypeController.Update"] = PermissionKeys.SystemDictionaryEdit,
        ["SysDictTypeController.Delete"] = PermissionKeys.SystemDictionaryDelete,
        ["SysDicCurrencyExchangeController.GetAll"] = PermissionKeys.SystemExchangeRateView,
        ["SysDicCurrencyExchangeController.GetPaged"] = PermissionKeys.SystemExchangeRateView,
        ["SysDicCurrencyExchangeController.GetById"] = PermissionKeys.SystemExchangeRateView,
        ["SysDicCurrencyExchangeController.Create"] = PermissionKeys.SystemExchangeRateCreate,
        ["SysDicCurrencyExchangeController.Update"] = PermissionKeys.SystemExchangeRateEdit,
        ["SysDicCurrencyExchangeController.Delete"] = PermissionKeys.SystemExchangeRateDelete,
        ["DocumentNoController.GetNextAsync"] = PermissionKeys.SystemNumberingIssue,
    };

    private static readonly Dictionary<string, string> BatchPrefixes = new(StringComparer.Ordinal)
    {
        ["CustomerProductController.BatchSave"] = "Customers.Products",
        ["CustomerController.BatchSave"] = "Customers.Customer",
        ["CustomerPoConfigController.BatchSave"] = "Customers.PoConfig",
        ["BomController.BatchSave"] = "Products.Bom",
        ["ProductController.BatchSave"] = "Products.Product",
        ["ProductCategoryController.BatchSave"] = "Products.Category",
        ["ProductColorController.BatchSave"] = "Products.Color",
        ["ProductMasterController.BatchSaveAsync"] = "Products.Master",
        ["ProductNeutralController.BatchSaveAsync"] = "Products.Neutral",
        ["ProductNeutralManagerController.BatchSaveAsync"] = "Products.Neutral",
        ["SupplierController.BatchSave"] = "Suppliers.Supplier",
        ["SupplierProductController.BatchSave"] = "Suppliers.Products",
        ["SysDicCurrencyController.BatchSave"] = "System.Currency",
        ["SysDictDataController.BatchSave"] = "System.Dictionary",
        ["SysDictTypeController.BatchSave"] = "System.Dictionary",
        ["SysDicCurrencyExchangeController.BatchSave"] = "System.ExchangeRate",
    };

    private static readonly HashSet<string> ExplicitIdentityActions = new(StringComparer.Ordinal)
    {
        "AuthController.Login", "AuthController.Me", "AuthController.ChangePassword",
        "FileController.Download", "ClientVersionController.Get"
    };

    /// <summary>请求进入业务 Action 前检查稳定键；批量保存按每种实际操作分别检查。</summary>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // MVC 默认从 ActionName 去掉 Async；映射固定使用实际控制器方法名，避免合法端点被误判为未登记。
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var action = descriptor is null
            ? string.Empty
            : $"{descriptor.ControllerTypeInfo.Name}.{descriptor.MethodInfo.Name}";
        if (ExplicitIdentityActions.Contains(action) || action.StartsWith("RbacUsersController.", StringComparison.Ordinal)
            || action.StartsWith("RbacRolesController.", StringComparison.Ordinal)
            || action.StartsWith("RbacPermissionsController.", StringComparison.Ordinal))
        {
            await next();
            return;
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        if (!currentUser.IsAuthenticated || userId == Guid.Empty)
        {
            Deny(context, ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
            return;
        }

        if (SingleKeys.TryGetValue(action, out var key))
        {
            if (await permissions.HasPermissionAsync(userId, key, context.HttpContext.RequestAborted))
                await next();
            else
                Deny(context, ServiceErrorCodes.Forbidden, "当前账号缺少此操作权限。");
            return;
        }

        if (BatchPrefixes.TryGetValue(action, out var prefix))
        {
            var request = context.ActionArguments.Values.FirstOrDefault(value =>
                value?.GetType().GetProperty("CreateItems") is not null
                && value.GetType().GetProperty("UpdateItems") is not null
                && value.GetType().GetProperty("DeleteItems") is not null);
            if (request is null)
            {
                Deny(context, ServiceErrorCodes.Validation, "批量请求缺少操作集合。");
                return;
            }
            var hasOperation = false;
            foreach (var (property, operation) in new[] { ("CreateItems", "Create"), ("UpdateItems", "Edit"), ("DeleteItems", "Delete") })
            {
                var items = request.GetType().GetProperty(property)?.GetValue(request) as IEnumerable;
                if (items is null || !items.Cast<object>().Any()) continue;
                hasOperation = true;
                if (!await permissions.HasPermissionAsync(userId, $"{prefix}.{operation}", context.HttpContext.RequestAborted))
                {
                    Deny(context, ServiceErrorCodes.Forbidden, $"当前账号缺少 {operation} 批量操作权限。");
                    return;
                }
            }
            if (!hasOperation)
            {
                Deny(context, ServiceErrorCodes.Validation, "批量请求至少需要一项创建、编辑或删除操作。");
                return;
            }
            await next();
            return;
        }

        if (action == "WarehouseSitesController.SearchCandidatesAsync")
        {
            if (await permissions.HasPermissionAsync(userId, PermissionKeys.ProcurementOrderCreate, context.HttpContext.RequestAborted)
                || await permissions.HasPermissionAsync(userId, PermissionKeys.ProcurementOrderConfirm, context.HttpContext.RequestAborted))
                await next();
            else
                Deny(context, ServiceErrorCodes.Forbidden, "当前账号缺少采购候选查看资格。");
            return;
        }

        // 新 Controller 必须先登记具体权限；旧直接模块接口在阶段 05 物理删除。
        Deny(context, ServiceErrorCodes.Forbidden, "此 API 尚未登记具体权限。");
    }

    /// <summary>沿用 ServiceResult 全局结果过滤器的 HTTP 错误映射。</summary>
    private static void Deny(ActionExecutingContext context, string code, string message) =>
        context.Result = new ObjectResult(ServiceResult<object>.Fail(code, message));
}
