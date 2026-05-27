using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW.CustomerViews.ViewModels;
using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DictViews.ViewModels;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.OrderDecomposeViews.ViewModels;
using SwiftExport.View.FW.OrderDecomposeViews.Views;
using SwiftExport.View.FW.PackingListViews.ViewModels;
using SwiftExport.View.FW.PackingListViews.Views;
using SwiftExport.View.FW.ProductViews.ViewModels;
using SwiftExport.View.FW.ProductViews.Views;
using SwiftExport.View.FW.PurchaseOrderViews.ViewModels;
using SwiftExport.View.FW.PurchaseOrderViews.Views;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.SupplierViews.ViewModels;
using SwiftExport.View.FW.SupplierViews.Views;
using SwiftExport.View.FW.SystemViews.ViewModels;
using SwiftExport.View.FW.SystemViews.Views;
using SwiftExport.View.FW.WarehouseViews.ViewModels;
using SwiftExport.View.FW.WarehouseViews.Views;

namespace SwiftExport.View.FW
{
    public static partial class DependencyInjection
    {
        /// <summary>
        /// 注册所有 ViewModel 和 View（按业务模块分组，成对注册）。
        ///
        /// 规则：
        ///   - 均为 Transient：每次从 DI 取出时创建新实例，状态互不干扰
        ///   - ViewModel 和 View 始终成对出现，新增页面时在对应模块分组末尾追加两行
        ///   - 共享/工具类 ViewModel（如 ProductSelectorViewModel）单独列在"共享组件"分组
        ///
        /// 新增页面示例：
        ///   services.AddTransient&lt;XxxViewModel&gt;();
        ///   services.AddTransient&lt;XxxView&gt;();
        /// </summary>
        private static IServiceCollection AddViewPages(this IServiceCollection services)
        {
            // ================== 共享组件 ==================
            // 跨模块复用的选择器/弹窗 ViewModel，无对应独立 View
            services.AddTransient<ProductSelectorViewModel>();
            services.AddTransient<CustomerSelectorDialogViewModel>();

            // ================== 字典管理模块 ==================
            services.AddTransient<DataDictManagerViewModel>();
            services.AddTransient<DataDictManagerView>();

            services.AddTransient<DictTypeManagerViewModel>();
            services.AddTransient<DictTypeManagerView>();

            services.AddTransient<GenericDictDataManagerViewModel>();
            services.AddTransient<GenericDictDataManagerView>();

            // ================== 系统配置模块 ==================
            services.AddTransient<SysDicCurrencyViewModel>();
            services.AddTransient<SysDicCurrencyView>();

            services.AddTransient<SysViewRouteManagerViewModel>();
            services.AddTransient<SysViewRouteManagerView>();

            // ================== 认证模块：登录 / 注册 ==================
            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginView>();

            services.AddTransient<ResetPasswdViewModel>();
            services.AddTransient<ResetPasswdView>();

            services.AddTransient<RegisterViewModel>();
            services.AddTransient<RegisterView>();

            // ================== 产品模块 ==================
            services.AddTransient<ProductManagerViewModel>();
            services.AddTransient<ProductManagerView>(); 
            services.AddTransient<MainProductManagerViewModel>();
            services.AddTransient<MainProductManagerView>();

            services.AddTransient<ProductColorManagerViewModel>();
            services.AddTransient<ProductColorManagerView>();

            services.AddTransient<ProductCategoryManagerViewModel>();
            services.AddTransient<ProductCategoryManagerView>();

            // ================== 供应商模块 ==================
            services.AddTransient<SupplierManagerViewModel>();
            services.AddTransient<SupplierManagerView>();
            services.AddTransient<SupplierProductManagerView>();

            // ================== 客户模块 ==================
            services.AddTransient<CustomerManagerViewModel>();
            services.AddTransient<CustomerManagerView>();
            services.AddTransient<CustomerProductManagerViewModel>();
            services.AddTransient<CustomerProductManagerView>();

            services.AddTransient<CustomerPoConfigViewModel>();
            services.AddTransient<CustomerPoConfigManagerView>();

            // ================== 销售订单模块 ==================
            services.AddTransient<SalesOrderManagerViewModel>();
            services.AddTransient<SalesOrderDetailViewModel>();
            services.AddTransient<SalesProductSelectorViewModel>();
            services.AddTransient<DependentDemandManagerViewModel>();
            services.AddTransient<SalesOrderManagerView>();
            services.AddTransient<SalesOrderDetailView>();
            services.AddTransient<SalesOrderDetailWindow>();
            services.AddTransient<SalesProductSeletorView>();
            services.AddTransient<DependentDemandManagerView>();

            // ================== 采购订单模块 ==================
            services.AddTransient<PurchaseOrderManagerViewModel>();
            services.AddTransient<PurchaseOrderManagerView>();

            // ================== 出货箱单模块 ==================
            services.AddTransient<PackingListManagerViewModel>();
            services.AddTransient<PackingListManagerView>();
            services.AddTransient<PackingListDetailViewModel>();
            services.AddTransient<PackingListDetailView>();
            services.AddTransient<UnshippedViewModel>();
            services.AddTransient<UnshippedView>();

            // ================== 仓库模块 ==================
            services.AddTransient<ReceiptManagerViewModel>();
            services.AddTransient<ReceiptManagerView>();
            services.AddTransient<ReceiptDetailViewModel>();
            services.AddTransient<ReceiptDetailView>();
            services.AddTransient<AwatingInboundViewModel>();
            services.AddTransient<AwatingInboundView>();
            
            return services;
        }
    }
}
