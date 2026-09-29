using System;
using System.Collections.Generic;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>集中登记侧边栏页面配置，禁止重复标识覆盖已有配置。</summary>
    public sealed class TaskPaneRegistrar
    {
        /// <summary>销售订单本地导入页标识。</summary>
        public const string SalesOrderImport = "sales-order-import";
        /// <summary>采购订单导入预览页标识。</summary>
        public const string PurchaseOrderImport = "purchase-order-import-preview";
        private readonly Dictionary<string, TaskPaneConfig> configs = new Dictionary<string, TaskPaneConfig>(StringComparer.Ordinal);
        /// <summary>登记页面及初始尺寸；不解析依赖、不预加载数据。</summary>
        public void Register(TaskPaneConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            configs.Add(config.Id, config);
        }
        /// <summary>取得已注册页面，未注册标识明确报错。</summary>
        public TaskPaneConfig Get(string id) => configs[id];
        /// <summary>插件页面注册的唯一入口；各页宽度可独立调整。</summary>
        public static TaskPaneRegistrar CreateDefault()
        {
            var registrar = new TaskPaneRegistrar();
            registrar.Register(new TaskPaneConfig(SalesOrderImport, "销售订单导入", typeof(SalesOrderImportView), 760));
            registrar.Register(new TaskPaneConfig(PurchaseOrderImport, "采购订单导入预览", typeof(PurchaseOrderImportView), 760));
            return registrar;
        }
    }
}
