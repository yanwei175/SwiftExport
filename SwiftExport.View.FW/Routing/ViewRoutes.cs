using SwiftExport.View.FW.DictViews.ViewModels;
using SwiftExport.View.FW.ProductViews.ViewModels;
using SwiftExport.View.FW.SystemViews.ViewModels;
using System;
using System.Collections.Generic;

namespace SwiftExport.View.FW.Routing
{
    /// <summary>
    /// 视图路由注册表
    /// 
    /// 职责：维护 TargetViewKey（字符串）→ ViewModel 类型 的映射关系。
    /// 
    /// 使用规则：
    ///   1. 每新增一个可路由的数据字典子页面，在 <see cref="Routes"/> 字典里加一行即可。
    ///   2. 对应的 ViewModel 必须已在 DI_View.cs 中以 Transient 注册，否则运行时报错。
    ///   3. TargetViewKey 与数据库 SysDictType.TargetViewKey 字段值保持一致。
    /// 
    /// ⚠ 不要在此处 new ViewModel —— 实例统一由 IServiceProvider 创建，保证依赖注入正确。
    /// </summary>
    public static class ViewRoutes
    {
        /// <summary>
        /// 路由字典：TargetViewKey → ViewModel 的 Type
        /// 
        /// 扩展方式（只需改这一个地方）：
        ///   ["新Key"] = typeof(新ViewModel),
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Type> Routes =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)  // Key 大小写不敏感
            {
                // ===== 产品相关 =====
                ["ProductColorView"]    = typeof(ProductColorManagerViewModel),
                ["ProductCategoryView"] = typeof(ProductCategoryManagerViewModel),

                // ===== 系统配置相关 =====
                ["SysDicCurrencyView"]  = typeof(SysDicCurrencyViewModel),

                // ===== 通用字典明细页（英/中文字典项） =====
                ["GenericDictDataView"] = typeof(GenericDictDataManagerViewModel),

                // ===== 预留（按需取消注释并在 DI_View.cs 中注册） =====
                // ["SupplierView"]     = typeof(SupplierManagerViewModel),
                // ["CustomerView"]     = typeof(CustomerManagerViewModel),
            };

        /// <summary>
        /// 尝试根据 TargetViewKey 获取对应的 ViewModel 类型。
        /// </summary>
        /// <param name="targetViewKey">路由键</param>
        /// <param name="vmType">命中时输出 ViewModel 类型，未命中时为 null</param>
        /// <returns>是否命中路由</returns>
        public static bool TryResolve(string targetViewKey, out Type vmType)
        {
            vmType = null;
            if (string.IsNullOrWhiteSpace(targetViewKey)) return false;
            return Routes.TryGetValue(targetViewKey, out vmType);
        }
    }
}
