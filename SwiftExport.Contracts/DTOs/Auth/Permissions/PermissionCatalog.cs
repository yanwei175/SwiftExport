using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>定义代码权限目录；数据库权限定义只保存此目录的投影。</summary>
    public static class PermissionCatalog
    {
        private static readonly PermissionDefinition[] Definitions =
        {
            new PermissionDefinition(PermissionKeys.CustomersCustomerCreate, "客户创建", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersCustomerDelete, "客户删除", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersCustomerEdit, "客户编辑", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersCustomerView, "客户查看", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersPoConfigCreate, "客户单头配置创建", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersPoConfigDelete, "客户单头配置删除", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersPoConfigEdit, "客户单头配置编辑", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersPoConfigView, "客户单头配置查看", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersProductsCreate, "客户产品创建", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersProductsDelete, "客户产品删除", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersProductsEdit, "客户产品编辑", "Customers", true),
            new PermissionDefinition(PermissionKeys.CustomersProductsView, "客户产品查看", "Customers", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningMaterialEstablish, "物料需求建立", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningMaterialRollback, "物料需求回退", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage, "DemandPlanning.Material.SupplierSuggestion管理", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningMaterialView, "物料需求查看", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningSourceManualManage, "DemandPlanning.Source.Manual管理", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.DemandPlanningSourceView, "来源需求查看", "DemandPlanning", true),
            new PermissionDefinition(PermissionKeys.InventoryBalanceView, "库存余额与流水查看", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundCreate, "入库单创建", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundDelete, "入库单删除", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundEdit, "入库单编辑", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundPost, "入库单过账", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundReverse, "入库单反向", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryInboundView, "入库单查看", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundCreate, "出库单创建", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundDelete, "出库单删除", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundEdit, "出库单编辑", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundPost, "出库单过账", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundReverse, "出库单反向", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryOutboundView, "出库单查看", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryWarehouseManage, "仓库管理", "Inventory", true),
            new PermissionDefinition(PermissionKeys.InventoryWarehouseView, "仓库查看", "Inventory", true),
            new PermissionDefinition(PermissionKeys.MasterDataEligibilityManage, "主数据执行资格管理", "MasterData", true),
            new PermissionDefinition(PermissionKeys.SalesOrderCancelConfirmation, "销售订单取消确认", "Sales", true),
            new PermissionDefinition(PermissionKeys.SalesOrderConfirm, "销售订单确认", "Sales", true),
            new PermissionDefinition(PermissionKeys.SalesOrderDelete, "销售订单删除", "Sales", true),
            new PermissionDefinition(PermissionKeys.SalesOrderLifecycle, "销售订单生命周期", "Sales", true),
            new PermissionDefinition(PermissionKeys.SalesOrderMaintain, "销售订单草稿维护", "Sales", true),
            new PermissionDefinition(PermissionKeys.ProcurementLookupView, "采购候选查看", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementOrderConfirm, "采购订单确认", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementOrderCreate, "采购订单创建", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementOrderEdit, "采购订单编辑", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementOrderRead, "采购订单读取", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementOrderVoid, "采购订单作废", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementProgressReport, "采购跟进报告", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProcurementProgressView, "采购跟进查看", "Procurement", true),
            new PermissionDefinition(PermissionKeys.ProductsBomCreate, "BOM创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsBomDelete, "BOM删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsBomEdit, "BOM编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsBomView, "BOM查看", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsCategoryCreate, "产品分类创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsCategoryDelete, "产品分类删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsCategoryEdit, "产品分类编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsCategoryView, "产品分类查看", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsColorCreate, "产品颜色创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsColorDelete, "产品颜色删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsColorEdit, "产品颜色编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsColorView, "产品颜色查看", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsMasterCreate, "主型号创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsMasterDelete, "主型号删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsMasterEdit, "主型号编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsMasterView, "主型号查看", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsNeutralCreate, "中性产品创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsNeutralDelete, "中性产品删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsNeutralEdit, "中性产品编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsNeutralView, "中性产品查看", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsProductCreate, "产品创建", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsProductDelete, "产品删除", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsProductEdit, "产品编辑", "Products", true),
            new PermissionDefinition(PermissionKeys.ProductsProductView, "产品查看", "Products", true),
            new PermissionDefinition(PermissionKeys.AfterSalesCancelConfirmation, "售后单取消确认", "QualityControl", true),
            new PermissionDefinition(PermissionKeys.AfterSalesConfirm, "售后单确认", "QualityControl", true),
            new PermissionDefinition(PermissionKeys.AfterSalesLifecycle, "售后单生命周期", "QualityControl", true),
            new PermissionDefinition(PermissionKeys.AfterSalesMaintain, "售后单草稿维护", "QualityControl", true),
            new PermissionDefinition(PermissionKeys.QualityControlAfterSalesView, "售后单查看", "QualityControl", true),
            new PermissionDefinition(PermissionKeys.SalesOrderView, "销售订单查看", "Sales", true),
            new PermissionDefinition(PermissionKeys.ShippingPackingListCreate, "装箱单创建", "Shipping", true),
            new PermissionDefinition(PermissionKeys.ShippingPackingListDelete, "装箱单删除", "Shipping", true),
            new PermissionDefinition(PermissionKeys.ShippingPackingListEdit, "装箱单编辑", "Shipping", true),
            new PermissionDefinition(PermissionKeys.PackingListPost, "装箱单过账", "Shipping", true),
            new PermissionDefinition(PermissionKeys.PackingListReverse, "装箱单反向", "Shipping", true),
            new PermissionDefinition(PermissionKeys.ShippingPackingListView, "装箱单查看", "Shipping", true),
            new PermissionDefinition(PermissionKeys.SuppliersProductsCreate, "供应商产品创建", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersProductsDelete, "供应商产品删除", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersProductsEdit, "供应商产品编辑", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersProductsView, "供应商产品查看", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersSupplierCreate, "供应商创建", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersSupplierDelete, "供应商删除", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersSupplierEdit, "供应商编辑", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SuppliersSupplierView, "供应商查看", "Suppliers", true),
            new PermissionDefinition(PermissionKeys.SystemCurrencyCreate, "币种创建", "System", true),
            new PermissionDefinition(PermissionKeys.SystemCurrencyDelete, "币种删除", "System", true),
            new PermissionDefinition(PermissionKeys.SystemCurrencyEdit, "币种编辑", "System", true),
            new PermissionDefinition(PermissionKeys.SystemCurrencyManage, "币种管理", "System", true),
            new PermissionDefinition(PermissionKeys.SystemCurrencyView, "币种查看", "System", true),
            new PermissionDefinition(PermissionKeys.SystemDictionaryCreate, "字典创建", "System", true),
            new PermissionDefinition(PermissionKeys.SystemDictionaryDelete, "字典删除", "System", true),
            new PermissionDefinition(PermissionKeys.SystemDictionaryEdit, "字典编辑", "System", true),
            new PermissionDefinition(PermissionKeys.SystemDictionaryView, "字典查看", "System", true),
            new PermissionDefinition(PermissionKeys.SystemExcelFieldMappingManage, "Excel字段映射维护", "System", true),
            new PermissionDefinition(PermissionKeys.SystemExchangeRateCreate, "汇率创建", "System", true),
            new PermissionDefinition(PermissionKeys.SystemExchangeRateDelete, "汇率删除", "System", true),
            new PermissionDefinition(PermissionKeys.SystemExchangeRateEdit, "汇率编辑", "System", true),
            new PermissionDefinition(PermissionKeys.SystemExchangeRateView, "汇率查看", "System", true),
            new PermissionDefinition(PermissionKeys.SystemFilesManage, "文件管理", "System", false),
            new PermissionDefinition(PermissionKeys.SystemNumberingIssue, "单号取号", "System", true),
            new PermissionDefinition(PermissionKeys.SystemPermissionsView, "权限定义查看", "System", false),
            new PermissionDefinition(PermissionKeys.SystemRolesManage, "角色管理", "System", false),
            new PermissionDefinition(PermissionKeys.SystemRolesView, "角色查看", "System", false),
            new PermissionDefinition(PermissionKeys.SystemUsersManage, "用户管理", "System", false),
            new PermissionDefinition(PermissionKeys.SystemUsersView, "用户查看", "System", false),
        };

        private static readonly IReadOnlyList<PermissionDefinition> ReadOnlyDefinitions = Array.AsReadOnly(Definitions);

        /// <summary>返回不可修改的稳定权限目录，避免调用方转换为数组后覆盖定义。</summary>
        public static IReadOnlyList<PermissionDefinition> All { get { return ReadOnlyDefinitions; } }

        /// <summary>查找已知权限键；大小写必须与目录完全一致。</summary>
        public static PermissionDefinition Find(string permissionKey)
        {
            return Definitions.FirstOrDefault(item => string.Equals(item.PermissionKey, permissionKey, StringComparison.Ordinal));
        }
    }

    /// <summary>表示一个稳定权限键的显示信息与普通角色可分配边界。</summary>
    public sealed class PermissionDefinition
    {
        /// <summary>创建权限定义；键、名称和分组来自代码目录。</summary>
        public PermissionDefinition(string permissionKey, string displayName, string groupKey, bool isAssignable)
        {
            PermissionKey = permissionKey;
            DisplayName = displayName;
            GroupKey = groupKey;
            IsAssignable = isAssignable;
        }

        /// <summary>稳定权限键。</summary>
        public string PermissionKey { get; }
        /// <summary>界面显示名称。</summary>
        public string DisplayName { get; }
        /// <summary>分组键。</summary>
        public string GroupKey { get; }
        /// <summary>普通角色是否可分配。</summary>
        public bool IsAssignable { get; }
    }
}
