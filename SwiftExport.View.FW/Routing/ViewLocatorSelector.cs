using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.Routing
{
    /// <summary>
    /// ViewLocator 约定映射选择器（DataTemplateSelector）
    ///
    /// 职责：
    ///   给 ContentControl.ContentTemplateSelector 使用，
    ///   根据 ViewModel 类型自动找到对应的 View 类型，动态生成 DataTemplate，
    ///   从而消除 XAML 里手写 DataTemplate DataType 的需要。
    ///
    /// 约定规则（命名空间 + 类名双层映射）：
    ///   命名空间：.ViewModels.  →  .Views.
    ///   类    名：XxxViewModel  →  XxxView
    ///
    /// 示例：
    ///   ProductColorManagerViewModel  →  ProductColorManagerView
    ///   SysDicCurrencyViewModel       →  SysDicCurrencyView
    ///   GenericDictDataManagerViewModel → GenericDictDataManagerView
    ///
    /// 兜底行为：
    ///   若按约定找不到 View 类型，返回 null，
    ///   WPF 会自动尝试 Resources 里显式声明的 DataTemplate（向后兼容）。
    ///
    /// 扩展方式：
    ///   只需新增 ViewModel + View 并按约定命名，无需再改任何 XAML 或注册表。
    /// </summary>
    public class ViewLocatorSelector : DataTemplateSelector
    {
        // ---- 缓存：避免每次选中节点都重复反射 ----
        // Key = ViewModel 类型，Value = 对应的 DataTemplate（找不到则为 null）
        private static readonly ConcurrentDictionary<Type, DataTemplate> _templateCache
            = new ConcurrentDictionary<Type, DataTemplate>();

        /// <summary>
        /// WPF 在渲染 ContentControl.Content 时调用此方法，返回要使用的 DataTemplate。
        /// </summary>
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item == null) return null;

            var vmType = item.GetType();

            // 命中缓存直接返回（null 值也会被缓存，代表"约定找不到"）
            DataTemplate cached;
            if (_templateCache.TryGetValue(vmType, out cached))
                return cached;

            // 未命中：尝试按约定创建
            var template = BuildTemplateByConvention(vmType);

            // 存入缓存（包含 null，防止重复反射）
            _templateCache.TryAdd(vmType, template);

            return template;
        }

        // ----------------------------------------------------------------
        // 私有辅助方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 按命名约定找到 View 类型，并构建一个简单的 DataTemplate。
        /// </summary>
        private static DataTemplate BuildTemplateByConvention(Type vmType)
        {
            var viewType = ResolveViewType(vmType);
            if (viewType == null) return null;

            // 动态创建 DataTemplate：VisualTree 是对应的 View 控件
            // WPF 在实例化此 DataTemplate 时会自动将 DataContext 设置为 ViewModel 实例
            var factory = new FrameworkElementFactory(viewType);
            var template = new DataTemplate(vmType)
            {
                VisualTree = factory
            };
            template.Seal(); // 密封，提升运行时性能

            return template;
        }

        /// <summary>
        /// 将 ViewModel 类型按约定转换为对应的 View 类型。
        ///
        /// 转换规则（两步替换）：
        ///   1. 命名空间：将 ".ViewModels." 替换为 ".Views."
        ///   2. 类    名：将类名后缀 "ViewModel" 替换为 "View"
        ///
        /// 查找范围：ViewModel 所在程序集（View.FW 项目内约定同程序集）。
        /// </summary>
        private static Type ResolveViewType(Type vmType)
        {
            var fullName = vmType.FullName;
            if (string.IsNullOrEmpty(fullName)) return null;

            // 步骤 1：替换命名空间段
            var viewFullName = fullName.Replace(".ViewModels.", ".Views.");

            // 步骤 2：替换类名后缀（只替换末尾的 ViewModel → View）
            // 使用 EndsWith + Substring 而非 Replace，避免类名中间含 ViewModel 时误替换
            if (viewFullName.EndsWith("ViewModel", StringComparison.Ordinal))
            {
                viewFullName = viewFullName.Substring(0, viewFullName.Length - "ViewModel".Length) + "View";
            }

            // 在 ViewModel 所在程序集中查找 View 类型
            return vmType.Assembly.GetType(viewFullName);
        }
    }
}
