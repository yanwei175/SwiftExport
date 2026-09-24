using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 集中定义前后端共享的稳定权限键；角色名称与权限键属于不同契约维度。
    /// </summary>
    public static class PermissionKeys
    {
        /// <summary>产品模块访问权限。</summary>
        public const string ModuleProductsAccess = "Module.Products.Access";
        /// <summary>客户模块访问权限。</summary>
        public const string ModuleCustomersAccess = "Module.Customers.Access";
        /// <summary>供应商模块访问权限。</summary>
        public const string ModuleSuppliersAccess = "Module.Suppliers.Access";
        /// <summary>销售模块访问权限。</summary>
        public const string ModuleSalesAccess = "Module.Sales.Access";
        /// <summary>需求计划模块访问权限。</summary>
        public const string ModuleDemandPlanningAccess = "Module.DemandPlanning.Access";
        /// <summary>物料需求整批建立权限，独立于模块访问及回退权限。</summary>
        public const string DemandPlanningMaterialEstablish = "DemandPlanning.Material.Establish";
        /// <summary>物料需求整批回退权限，独立于模块访问及建立权限。</summary>
        public const string DemandPlanningMaterialRollback = "DemandPlanning.Material.Rollback";
        /// <summary>手工来源创建、修改和关闭权限，独立于需求模块访问。</summary>
        public const string DemandPlanningSourceManualManage = "DemandPlanning.Source.Manual.Manage";
        /// <summary>物料需求推荐采购供应商修改权限，独立于需求模块访问。</summary>
        public const string DemandPlanningMaterialSupplierSuggestionManage = "DemandPlanning.Material.SupplierSuggestion.Manage";
        /// <summary>采购模块访问权限。</summary>
        public const string ModuleProcurementAccess = "Module.Procurement.Access";
        /// <summary>库存模块访问权限。</summary>
        public const string ModuleInventoryAccess = "Module.Inventory.Access";
        /// <summary>出货模块访问权限。</summary>
        public const string ModuleShippingAccess = "Module.Shipping.Access";
        /// <summary>装箱单预览和过账能力，不包含反向或手工库存权限。</summary>
        public const string PackingListPost = "Shipping.PackingList.Post";
        /// <summary>装箱单整单反过账能力，独立于正向过账分配。</summary>
        public const string PackingListReverse = "Shipping.PackingList.Reverse";
        /// <summary>品质与售后模块访问权限。</summary>
        public const string ModuleQualityControlAccess = "Module.QualityControl.Access";
        /// <summary>仅由服务端为 Admin 生成的设置管理能力。</summary>
        public const string SystemSettingsManage = "System.Settings.Manage";

        public const string ProcurementOrderRead = "Procurement.Order.Read";
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

        private static readonly string[] ModuleValues =
        {
            ModuleProductsAccess,
            ModuleCustomersAccess,
            ModuleSuppliersAccess,
            ModuleSalesAccess,
            ModuleDemandPlanningAccess,
            ModuleProcurementAccess,
            ModuleInventoryAccess,
            ModuleShippingAccess,
            ModuleQualityControlAccess
        };

        private static readonly string[] ProcurementOperationValues =
        {
            ProcurementOrderRead,
            ProcurementOrderCreate,
            ProcurementOrderEdit,
            ProcurementOrderConfirm
        };

        private static readonly string[] AllValues = ModuleValues
            .Concat(new[] { PackingListPost, PackingListReverse })
            .Concat(new[] { SystemSettingsManage, ProcurementOrderVoid })
            .Concat(ProcurementOperationValues)
            .Concat(new[] { InventoryWarehouseManage, InventoryInboundPost, InventoryInboundReverse, InventoryOutboundPost, InventoryOutboundReverse })
            .Concat(new[] { DemandPlanningMaterialEstablish, DemandPlanningMaterialRollback, DemandPlanningSourceManualManage, DemandPlanningMaterialSupplierSuggestionManage })
            .ToArray();

        private static readonly string[] UserAssignableValues = ModuleValues
            .Concat(new[] { PackingListPost, PackingListReverse })
            .Concat(new[] { ProcurementOrderVoid })
            .Concat(new[] { InventoryWarehouseManage, InventoryInboundPost, InventoryInboundReverse, InventoryOutboundPost, InventoryOutboundReverse })
            .Concat(new[] { DemandPlanningMaterialEstablish, DemandPlanningMaterialRollback, DemandPlanningSourceManualManage, DemandPlanningMaterialSupplierSuggestionManage })
            .ToArray();

        /// <summary>获取允许管理员直接分配给普通用户的一级模块键。</summary>
        public static IReadOnlyList<string> Modules
        {
            get { return ModuleValues; }
        }

        /// <summary>获取管理员可直接分配给普通用户的模块访问权限与独立业务能力。</summary>
        public static IReadOnlyList<string> UserAssignable
        {
            get { return UserAssignableValues; }
        }

        /// <summary>获取需要继续兼容的采购操作权限键。</summary>
        public static IReadOnlyList<string> ProcurementOperations
        {
            get { return ProcurementOperationValues; }
        }

        /// <summary>获取当前公开给客户端的全部稳定权限键。</summary>
        public static IReadOnlyList<string> All
        {
            get { return AllValues; }
        }

        /// <summary>
        /// 从当前数据库分配名称中提取稳定权限键；Admin 角色映射为全部已知权限。
        /// </summary>
        public static List<string> ExtractPermissions(IEnumerable<string> assignedNames)
        {
            var names = (assignedNames ?? Enumerable.Empty<string>()).ToList();
            if (names.Any(name => string.Equals(name, "Admin", StringComparison.OrdinalIgnoreCase)))
            {
                return AllValues.ToList();
            }

            return AllValues.Where(permission => names.Any(name =>
                string.Equals(name, permission, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        /// <summary>
        /// 从当前数据库分配名称中提取业务角色，并排除已识别的权限键。
        /// </summary>
        public static List<string> ExtractRoles(IEnumerable<string> assignedNames)
        {
            return (assignedNames ?? Enumerable.Empty<string>())
                .Where(name => !AllValues.Any(permission =>
                    string.Equals(name, permission, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
