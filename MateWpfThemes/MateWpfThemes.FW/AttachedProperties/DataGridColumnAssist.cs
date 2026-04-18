using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGridColumnAssist 提供用于 DataGridColumn 的附加属性，支持通过数据绑定控制列的可见性。
    /// </summary>
    public class DataGridColumnAssist
    {
        /// <summary>
        /// 可绑定的列可见性附加属性。允许通过数据绑定动态控制 DataGridColumn 的显示或隐藏。
        /// </summary>
        public static readonly DependencyProperty BindableVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "BindableVisibility",
            typeof(bool),
            typeof(DataGridColumnAssist),
            new PropertyMetadata(true, OnBindableVisibilityChanged));

        /// <summary>
        /// 设置 BindableVisibility 附加属性的值。
        /// </summary>
        /// <param name="obj">目标依赖对象，通常为 DataGridColumn。</param>
        /// <param name="value">可见性布尔值，true 为可见，false 为隐藏。</param>
        public static void SetBindableVisibility(DependencyObject obj, bool value)
            => obj.SetValue(BindableVisibilityProperty, value);

        /// <summary>
        /// 获取 BindableVisibility 附加属性的值。
        /// </summary>
        /// <param name="obj">目标依赖对象，通常为 DataGridColumn。</param>
        /// <returns>可见性布尔值。</returns>
        public static bool GetBindableVisibility(DependencyObject obj)
            => (bool)obj.GetValue(BindableVisibilityProperty);

        /// <summary>
        /// 当 BindableVisibility 属性发生变化时的回调方法。
        /// 根据属性值设置 DataGridColumn 的 Visibility。
        /// </summary>
        /// <param name="d">目标依赖对象。</param>
        /// <param name="e">属性变更事件参数。</param>
        private static void OnBindableVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGridColumn column)
            {
                // 根据新值设置列的可见性
                column.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
