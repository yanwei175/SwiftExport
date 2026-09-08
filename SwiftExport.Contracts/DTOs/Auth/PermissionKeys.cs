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
        /// <summary>采购模块访问权限。</summary>
        public const string ModuleProcurementAccess = "Module.Procurement.Access";
        /// <summary>库存模块访问权限。</summary>
        public const string ModuleInventoryAccess = "Module.Inventory.Access";
        /// <summary>出货模块访问权限。</summary>
        public const string ModuleShippingAccess = "Module.Shipping.Access";
        /// <summary>品质与售后模块访问权限。</summary>
        public const string ModuleQualityControlAccess = "Module.QualityControl.Access";
        /// <summary>仅由服务端为 Admin 生成的设置管理能力。</summary>
        public const string SystemSettingsManage = "System.Settings.Manage";

        public const string ProcurementOrderRead = "Procurement.Order.Read";
        public const string ProcurementOrderCreate = "Procurement.Order.Create";
        public const string ProcurementOrderEdit = "Procurement.Order.Edit";
        public const string ProcurementOrderConfirm = "Procurement.Order.Confirm";

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
            .Concat(new[] { SystemSettingsManage })
            .Concat(ProcurementOperationValues)
            .ToArray();

        /// <summary>获取允许管理员直接分配给普通用户的一级模块键。</summary>
        public static IReadOnlyList<string> Modules
        {
            get { return ModuleValues; }
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
