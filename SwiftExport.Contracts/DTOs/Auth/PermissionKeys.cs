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
        public const string ProcurementOrderRead = "Procurement.Order.Read";
        public const string ProcurementOrderCreate = "Procurement.Order.Create";
        public const string ProcurementOrderEdit = "Procurement.Order.Edit";
        public const string ProcurementOrderConfirm = "Procurement.Order.Confirm";

        private static readonly string[] AllValues =
        {
            ProcurementOrderRead,
            ProcurementOrderCreate,
            ProcurementOrderEdit,
            ProcurementOrderConfirm
        };

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
