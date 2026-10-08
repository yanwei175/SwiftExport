
namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 集中定义前后端共享的稳定权限键；角色名称与权限键属于不同契约维度。
    /// </summary>
    public static class PermissionKeys
    {
        /// <summary>销售订单列表、详情和候选读取权限。</summary>
        public const string SalesOrderView = "Sales.Order.View";
        /// <summary>销售草稿保存权限，独立于删除能力。</summary>
        public const string SalesOrderMaintain = "Sales.Order.Maintain";
        /// <summary>销售草稿删除权限，独立于保存能力。</summary>
        public const string SalesOrderDelete = "Sales.Order.Delete";
        /// <summary>销售确认权限。</summary>
        public const string SalesOrderConfirm = "Sales.Order.Confirm";
        /// <summary>销售取消确认权限。</summary>
        public const string SalesOrderCancelConfirmation = "Sales.Order.CancelConfirmation";
        /// <summary>销售关闭权限。</summary>
        public const string SalesOrderLifecycle = "Sales.Order.Lifecycle";
        /// <summary>物料需求整批建立权限，独立于模块访问及回退权限。</summary>
        public const string DemandPlanningMaterialEstablish = "DemandPlanning.Material.Establish";
        /// <summary>物料需求整批回退权限，独立于模块访问及建立权限。</summary>
        public const string DemandPlanningMaterialRollback = "DemandPlanning.Material.Rollback";
        /// <summary>手工来源创建、修改和关闭权限，独立于需求模块访问。</summary>
        public const string DemandPlanningSourceManualManage = "DemandPlanning.Source.Manual.Manage";
        /// <summary>物料需求推荐采购供应商修改权限，独立于需求模块访问。</summary>
        public const string DemandPlanningMaterialSupplierSuggestionManage = "DemandPlanning.Material.SupplierSuggestion.Manage";
        /// <summary>装箱单预览和过账能力，不包含反向或手工库存权限。</summary>
        public const string PackingListPost = "Shipping.PackingList.Post";
        /// <summary>装箱单整单反过账能力，独立于正向过账分配。</summary>
        public const string PackingListReverse = "Shipping.PackingList.Reverse";
        /// <summary>售后创建、保存和合法补充权限。</summary>
        public const string AfterSalesMaintain = "QualityControl.AfterSales.Maintain";
        /// <summary>售后实物需求确认权限。</summary>
        public const string AfterSalesConfirm = "QualityControl.AfterSales.Confirm";
        /// <summary>售后取消实物需求确认权限。</summary>
        public const string AfterSalesCancelConfirmation = "QualityControl.AfterSales.CancelConfirmation";
        /// <summary>售后结案、重开和作废权限。</summary>
        public const string AfterSalesLifecycle = "QualityControl.AfterSales.Lifecycle";

        public const string ProcurementOrderRead = "Procurement.Order.Read";
        /// <summary>采购供应商跟进查询权限，独立于订单读取。</summary>
        public const string ProcurementProgressView = "Procurement.Progress.View";
        /// <summary>采购供应商跟进报告权限，独立于订单编辑。</summary>
        public const string ProcurementProgressReport = "Procurement.Progress.Report";
        public const string ProcurementOrderCreate = "Procurement.Order.Create";
        public const string ProcurementOrderEdit = "Procurement.Order.Edit";
        public const string ProcurementOrderConfirm = "Procurement.Order.Confirm";
        /// <summary>采购整单作废权限，独立分配，不由采购模块访问自动扩展。</summary>
        public const string ProcurementOrderVoid = "Procurement.Order.Void";
        /// <summary>供应商虚拟仓新增、改名、停用和删除权限。</summary>
        public const string InventoryWarehouseManage = "Inventory.Warehouse.Manage";

        /// <summary>入库预览与正式过账权限，不包含反过账。</summary>
        public const string InventoryInboundPost = "Inventory.Inbound.Post";
        /// <summary>人工入库整单反过账权限，与正向过账独立分配。</summary>
        public const string InventoryInboundReverse = "Inventory.Inbound.Reverse";
        /// <summary>普通库存出库预览与正向过账，不包含反向或负库存放行。</summary>
        public const string InventoryOutboundPost = "Inventory.Outbound.Post";
        /// <summary>手工库存出库整单反过账，不包含自动出库。</summary>
        public const string InventoryOutboundReverse = "Inventory.Outbound.Reverse";

        /// <summary>客户创建权限。</summary>
        public const string CustomersCustomerCreate = "Customers.Customer.Create";
        /// <summary>客户删除权限。</summary>
        public const string CustomersCustomerDelete = "Customers.Customer.Delete";
        /// <summary>客户编辑权限。</summary>
        public const string CustomersCustomerEdit = "Customers.Customer.Edit";
        /// <summary>客户查看权限。</summary>
        public const string CustomersCustomerView = "Customers.Customer.View";
        /// <summary>客户单头配置创建权限。</summary>
        public const string CustomersPoConfigCreate = "Customers.PoConfig.Create";
        /// <summary>客户单头配置删除权限。</summary>
        public const string CustomersPoConfigDelete = "Customers.PoConfig.Delete";
        /// <summary>客户单头配置编辑权限。</summary>
        public const string CustomersPoConfigEdit = "Customers.PoConfig.Edit";
        /// <summary>客户单头配置查看权限。</summary>
        public const string CustomersPoConfigView = "Customers.PoConfig.View";
        /// <summary>客户产品创建权限。</summary>
        public const string CustomersProductsCreate = "Customers.Products.Create";
        /// <summary>客户产品删除权限。</summary>
        public const string CustomersProductsDelete = "Customers.Products.Delete";
        /// <summary>客户产品编辑权限。</summary>
        public const string CustomersProductsEdit = "Customers.Products.Edit";
        /// <summary>客户产品查看权限。</summary>
        public const string CustomersProductsView = "Customers.Products.View";
        /// <summary>物料需求查看权限。</summary>
        public const string DemandPlanningMaterialView = "DemandPlanning.Material.View";
        /// <summary>来源需求查看权限。</summary>
        public const string DemandPlanningSourceView = "DemandPlanning.Source.View";
        /// <summary>库存余额与流水查看权限。</summary>
        public const string InventoryBalanceView = "Inventory.Balance.View";
        /// <summary>入库单创建权限。</summary>
        public const string InventoryInboundCreate = "Inventory.Inbound.Create";
        /// <summary>入库单删除权限。</summary>
        public const string InventoryInboundDelete = "Inventory.Inbound.Delete";
        /// <summary>入库单编辑权限。</summary>
        public const string InventoryInboundEdit = "Inventory.Inbound.Edit";
        /// <summary>入库单查看权限。</summary>
        public const string InventoryInboundView = "Inventory.Inbound.View";
        /// <summary>出库单创建权限。</summary>
        public const string InventoryOutboundCreate = "Inventory.Outbound.Create";
        /// <summary>出库单删除权限。</summary>
        public const string InventoryOutboundDelete = "Inventory.Outbound.Delete";
        /// <summary>出库单编辑权限。</summary>
        public const string InventoryOutboundEdit = "Inventory.Outbound.Edit";
        /// <summary>出库单查看权限。</summary>
        public const string InventoryOutboundView = "Inventory.Outbound.View";
        /// <summary>仓库查看权限。</summary>
        public const string InventoryWarehouseView = "Inventory.Warehouse.View";
        /// <summary>主数据执行资格管理权限。</summary>
        public const string MasterDataEligibilityManage = "MasterData.Eligibility.Manage";
        /// <summary>采购候选查看权限。</summary>
        public const string ProcurementLookupView = "Procurement.Lookup.View";
        /// <summary>BOM创建权限。</summary>
        public const string ProductsBomCreate = "Products.Bom.Create";
        /// <summary>BOM删除权限。</summary>
        public const string ProductsBomDelete = "Products.Bom.Delete";
        /// <summary>BOM编辑权限。</summary>
        public const string ProductsBomEdit = "Products.Bom.Edit";
        /// <summary>BOM查看权限。</summary>
        public const string ProductsBomView = "Products.Bom.View";
        /// <summary>产品分类创建权限。</summary>
        public const string ProductsCategoryCreate = "Products.Category.Create";
        /// <summary>产品分类删除权限。</summary>
        public const string ProductsCategoryDelete = "Products.Category.Delete";
        /// <summary>产品分类编辑权限。</summary>
        public const string ProductsCategoryEdit = "Products.Category.Edit";
        /// <summary>产品分类查看权限。</summary>
        public const string ProductsCategoryView = "Products.Category.View";
        /// <summary>产品颜色创建权限。</summary>
        public const string ProductsColorCreate = "Products.Color.Create";
        /// <summary>产品颜色删除权限。</summary>
        public const string ProductsColorDelete = "Products.Color.Delete";
        /// <summary>产品颜色编辑权限。</summary>
        public const string ProductsColorEdit = "Products.Color.Edit";
        /// <summary>产品颜色查看权限。</summary>
        public const string ProductsColorView = "Products.Color.View";
        /// <summary>主型号创建权限。</summary>
        public const string ProductsMasterCreate = "Products.Master.Create";
        /// <summary>主型号删除权限。</summary>
        public const string ProductsMasterDelete = "Products.Master.Delete";
        /// <summary>主型号编辑权限。</summary>
        public const string ProductsMasterEdit = "Products.Master.Edit";
        /// <summary>主型号查看权限。</summary>
        public const string ProductsMasterView = "Products.Master.View";
        /// <summary>中性产品创建权限。</summary>
        public const string ProductsNeutralCreate = "Products.Neutral.Create";
        /// <summary>中性产品删除权限。</summary>
        public const string ProductsNeutralDelete = "Products.Neutral.Delete";
        /// <summary>中性产品编辑权限。</summary>
        public const string ProductsNeutralEdit = "Products.Neutral.Edit";
        /// <summary>中性产品查看权限。</summary>
        public const string ProductsNeutralView = "Products.Neutral.View";
        /// <summary>产品创建权限。</summary>
        public const string ProductsProductCreate = "Products.Product.Create";
        /// <summary>产品删除权限。</summary>
        public const string ProductsProductDelete = "Products.Product.Delete";
        /// <summary>产品编辑权限。</summary>
        public const string ProductsProductEdit = "Products.Product.Edit";
        /// <summary>产品查看权限。</summary>
        public const string ProductsProductView = "Products.Product.View";
        /// <summary>售后单查看权限。</summary>
        public const string QualityControlAfterSalesView = "QualityControl.AfterSales.View";
        /// <summary>流量测试组查询权限。</summary>
        public const string QualityControlFlowView = "QualityControl.Flow.View";
        /// <summary>流量测试组新增权限。</summary>
        public const string QualityControlFlowCreate = "QualityControl.Flow.Create";
        /// <summary>流量测试组修改权限。</summary>
        public const string QualityControlFlowEdit = "QualityControl.Flow.Edit";
        /// <summary>流量测试组删除权限。</summary>
        public const string QualityControlFlowDelete = "QualityControl.Flow.Delete";
        /// <summary>装箱单创建权限。</summary>
        public const string ShippingPackingListCreate = "Shipping.PackingList.Create";
        /// <summary>装箱单删除权限。</summary>
        public const string ShippingPackingListDelete = "Shipping.PackingList.Delete";
        /// <summary>装箱单编辑权限。</summary>
        public const string ShippingPackingListEdit = "Shipping.PackingList.Edit";
        /// <summary>装箱单查看权限。</summary>
        public const string ShippingPackingListView = "Shipping.PackingList.View";
        /// <summary>供应商产品创建权限。</summary>
        public const string SuppliersProductsCreate = "Suppliers.Products.Create";
        /// <summary>供应商产品删除权限。</summary>
        public const string SuppliersProductsDelete = "Suppliers.Products.Delete";
        /// <summary>供应商产品编辑权限。</summary>
        public const string SuppliersProductsEdit = "Suppliers.Products.Edit";
        /// <summary>供应商产品查看权限。</summary>
        public const string SuppliersProductsView = "Suppliers.Products.View";
        /// <summary>供应商创建权限。</summary>
        public const string SuppliersSupplierCreate = "Suppliers.Supplier.Create";
        /// <summary>供应商删除权限。</summary>
        public const string SuppliersSupplierDelete = "Suppliers.Supplier.Delete";
        /// <summary>供应商编辑权限。</summary>
        public const string SuppliersSupplierEdit = "Suppliers.Supplier.Edit";
        /// <summary>供应商查看权限。</summary>
        public const string SuppliersSupplierView = "Suppliers.Supplier.View";
        /// <summary>币种创建权限。</summary>
        public const string SystemCurrencyCreate = "System.Currency.Create";
        /// <summary>币种删除权限。</summary>
        public const string SystemCurrencyDelete = "System.Currency.Delete";
        /// <summary>币种编辑权限。</summary>
        public const string SystemCurrencyEdit = "System.Currency.Edit";
        /// <summary>币种管理权限。</summary>
        public const string SystemCurrencyManage = "System.Currency.Manage";
        /// <summary>币种查看权限。</summary>
        public const string SystemCurrencyView = "System.Currency.View";
        /// <summary>字典创建权限。</summary>
        public const string SystemDictionaryCreate = "System.Dictionary.Create";
        /// <summary>字典删除权限。</summary>
        public const string SystemDictionaryDelete = "System.Dictionary.Delete";
        /// <summary>字典编辑权限。</summary>
        public const string SystemDictionaryEdit = "System.Dictionary.Edit";
        /// <summary>字典查看权限。</summary>
        public const string SystemDictionaryView = "System.Dictionary.View";
        /// <summary>Excel 地址读取配置维护权限；保留历史键值以兼容已有角色授权。</summary>
        public const string SystemExcelFieldMappingManage = "System.ExcelFieldMapping.Manage";
        /// <summary>汇率创建权限。</summary>
        public const string SystemExchangeRateCreate = "System.ExchangeRate.Create";
        /// <summary>汇率删除权限。</summary>
        public const string SystemExchangeRateDelete = "System.ExchangeRate.Delete";
        /// <summary>汇率编辑权限。</summary>
        public const string SystemExchangeRateEdit = "System.ExchangeRate.Edit";
        /// <summary>汇率查看权限。</summary>
        public const string SystemExchangeRateView = "System.ExchangeRate.View";
        /// <summary>文件管理权限。</summary>
        public const string SystemFilesManage = "System.Files.Manage";
        /// <summary>单号取号权限。</summary>
        public const string SystemNumberingIssue = "System.Numbering.Issue";
        /// <summary>权限定义查看权限。</summary>
        public const string SystemPermissionsView = "System.Permissions.View";
        /// <summary>角色管理权限。</summary>
        public const string SystemRolesManage = "System.Roles.Manage";
        /// <summary>角色查看权限。</summary>
        public const string SystemRolesView = "System.Roles.View";
        /// <summary>用户管理权限。</summary>
        public const string SystemUsersManage = "System.Users.Manage";
        /// <summary>用户查看权限。</summary>
        public const string SystemUsersView = "System.Users.View";
    }
}
