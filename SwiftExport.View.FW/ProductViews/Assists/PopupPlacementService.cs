using MateWpfThemes.FW.AttachedProperties;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SwiftExport.View.FW.ProductViews.Assists
{
    /// <summary>
    /// 附加属性服务：自动将 PopupEx 的 PlacementTarget 设置到最近的 DataGridCell，
    /// 使弹窗跟随单元格位置并在窗口移动/缩放时自动刷新。
    /// </summary>
    public static class PopupPlacementService
    {
        #region Attach 附加属性

        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(PopupPlacementService),
                new PropertyMetadata(false, OnAttachChanged));

        public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);
        public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

        #endregion

        /// <summary>
        /// 当 Attach=True 时，PopupEx 被加载到视觉树后自动向上查找 DataGridCell 作为定位目标
        /// </summary>
        private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is PopupEx popup)) return;

            if ((bool)e.NewValue)
            {
                popup.Loaded -= Popup_Loaded;
                popup.Loaded += Popup_Loaded;
            }
            else
            {
                popup.Loaded -= Popup_Loaded;
            }
        }

        // Popup 被加载时，向上查找 DataGridCell 并设置 PlacementTarget
        private static void Popup_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is PopupEx popup)) return;

            // 从 Popup 的逻辑父级（通常是 DataGridCell 内部的容器）向上搜索
            var cell = FindAncestor<DataGridCell>(popup.PlacementTarget as DependencyObject)
                    ?? FindAncestor<DataGridCell>(popup);

            if (cell != null)
            {
                popup.PlacementTarget = cell;
                popup.Placement = PlacementMode.Bottom;
            }
        }

        /// <summary>
        /// 沿视觉树向上查找指定类型的祖先元素
        /// </summary>
        private static T FindAncestor<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T target) return target;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }
    }
}
