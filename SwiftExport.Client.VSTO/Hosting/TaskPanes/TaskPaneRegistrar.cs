using System;
using System.Collections.Generic;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>集中登记侧边栏页面配置，禁止重复标识覆盖已有配置。</summary>
    public sealed class TaskPaneRegistrar
    {
        private readonly Dictionary<string, TaskPaneConfig> configs = new Dictionary<string, TaskPaneConfig>(StringComparer.Ordinal);
        /// <summary>登记页面及初始尺寸；不解析依赖、不预加载数据。</summary>
        public void Register(TaskPaneConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            configs.Add(config.Id, config);
        }
        /// <summary>取得已注册页面，未注册标识明确报错。</summary>
        public TaskPaneConfig Get(string id) => configs[id];
        /// <summary>集中注册读取配置、销售与采购列表侧栏；导入仍使用独立窗体。</summary>
        public static TaskPaneRegistrar CreateDefault()
        {
            var registrar = new TaskPaneRegistrar();
            registrar.Register(new TaskPaneConfig("excel-read-profile", "Excel 读取配置",
                typeof(Features.Settings.ExcelReadProfiles.Views.ExcelReadProfileView), 860));
            registrar.Register(new TaskPaneConfig("sales-order-list", "销售订单",
                typeof(Features.Sales.Orders.Views.SalesOrderListView), 860));
            registrar.Register(new TaskPaneConfig("purchase-order-list", "采购订单",
                typeof(Features.Procurement.PurchaseOrders.Views.PurchaseOrderListView), 860));
            return registrar;
        }
    }
}
