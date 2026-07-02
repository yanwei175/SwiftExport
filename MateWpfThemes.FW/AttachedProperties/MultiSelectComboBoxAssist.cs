using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// MultiSelectComboBoxAssist：给原生 ComboBox 增加“多选筛选”能力。
    /// 典型场景：订单状态筛选，允许同时勾选多个状态后发 API。
    /// 
    /// 使用方式（示例）：
    /// <ComboBox
    ///     ItemsSource="{Binding OrderStatusOptions}"
    ///     mt:MultiSelectComboBoxAssist.Enable="True"
    ///     mt:MultiSelectComboBoxAssist.SelectedItems="{Binding SelectedOrderStatuses, Mode=TwoWay}"/>
    /// </summary>
    public static class MultiSelectComboBoxAssist
    {
        #region Enable：开启多选模式

        /// <summary>
        /// 是否启用多选模式。
        /// </summary>
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(MultiSelectComboBoxAssist),
                new PropertyMetadata(false, OnEnableChanged));

        public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
        public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;

            // 中文注释：先统一解绑，避免重复挂载事件。
            UnwireEvents(combo);

            if ((bool)e.NewValue)
            {
                // 中文注释：开启后强制使用“只读可编辑文本框”展示汇总文本，避免单选行为覆盖展示。
                combo.IsEditable = true;
                combo.IsReadOnly = true;
                combo.StaysOpenOnEdit = true;
                combo.IsTextSearchEnabled = false;

                WireEvents(combo);
                RefreshSelectionVisuals(combo);
                RefreshDisplayText(combo);
            }
            else
            {
                // 中文注释：关闭多选模式时，恢复文本显示。
                combo.Text = string.Empty;
            }
        }

        #endregion

        #region SelectedItems：对外双向绑定的多选集合

        /// <summary>
        /// 对外暴露的已选集合（建议绑定到 ViewModel 的 List/ObservableCollection）。
        /// </summary>
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",
                typeof(IList),
                typeof(MultiSelectComboBoxAssist),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemsChanged));

        public static IList GetSelectedItems(DependencyObject obj) => (IList)obj.GetValue(SelectedItemsProperty);
        public static void SetSelectedItems(DependencyObject obj, IList value) => obj.SetValue(SelectedItemsProperty, value);

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;

            if (!GetEnable(combo)) return;

            // 中文注释：当外部直接替换了集合引用（例如 VM 重置筛选条件）时，刷新 UI 勾选态与文本。
            RefreshSelectionVisuals(combo);
            RefreshDisplayText(combo);
        }

        #endregion

        #region Separator：汇总文本分隔符

        /// <summary>
        /// 多选文本展示时的分隔符，默认使用中文顿号。
        /// </summary>
        public static readonly DependencyProperty SeparatorProperty =
            DependencyProperty.RegisterAttached(
                "Separator",
                typeof(string),
                typeof(MultiSelectComboBoxAssist),
                new PropertyMetadata("、", OnSeparatorChanged));

        public static string GetSeparator(DependencyObject obj) => (string)obj.GetValue(SeparatorProperty);
        public static void SetSeparator(DependencyObject obj, string value) => obj.SetValue(SeparatorProperty, value);

        private static void OnSeparatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;
            if (!GetEnable(combo)) return;

            RefreshDisplayText(combo);
        }

        #endregion

        #region DisplayMemberPath：多选汇总文本使用的显示字段

        /// <summary>
        /// 指定已选项展示字段。
        /// 若未设置，则优先使用 ComboBox.DisplayMemberPath，再回退 ToString()。
        /// </summary>
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.RegisterAttached(
                "DisplayMemberPath",
                typeof(string),
                typeof(MultiSelectComboBoxAssist),
                new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

        public static string GetDisplayMemberPath(DependencyObject obj) => (string)obj.GetValue(DisplayMemberPathProperty);
        public static void SetDisplayMemberPath(DependencyObject obj, string value) => obj.SetValue(DisplayMemberPathProperty, value);

        private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;
            if (!GetEnable(combo)) return;

            RefreshDisplayText(combo);
        }

        #endregion

        #region 事件挂载/解绑

        private static void WireEvents(ComboBox combo)
        {
            combo.Loaded += Combo_Loaded;
            combo.Unloaded += Combo_Unloaded;
            combo.DropDownOpened += Combo_DropDownOpened;

            // 中文注释：通过捕获 ComboBoxItem 的鼠标事件来拦截单选默认行为，实现“点击即切换多选”。
            combo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Combo_PreviewMouseLeftButtonDown), true);
        }

        private static void UnwireEvents(ComboBox combo)
        {
            combo.Loaded -= Combo_Loaded;
            combo.Unloaded -= Combo_Unloaded;
            combo.DropDownOpened -= Combo_DropDownOpened;
            combo.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Combo_PreviewMouseLeftButtonDown));
        }

        private static void Combo_Loaded(object sender, RoutedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;

            EnsureSelectedItemsContainer(combo);
            RefreshSelectionVisuals(combo);
            RefreshDisplayText(combo);
        }

        private static void Combo_Unloaded(object sender, RoutedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;
            UnwireEvents(combo);
        }

        private static void Combo_DropDownOpened(object sender, EventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;
            RefreshSelectionVisuals(combo);
        }

        private static void Combo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || !GetEnable(combo)) return;

            var container = FindAncestor<ComboBoxItem>(e.OriginalSource as DependencyObject);
            if (container == null) return;

            var item = container.DataContext;
            if (item == null) return;

            ToggleSelectedItem(combo, item);

            // 中文注释：拦截默认单选行为，避免下拉框被关闭。
            e.Handled = true;
            combo.IsDropDownOpen = true;
        }

        #endregion

        #region 核心逻辑

        /// <summary>
        /// 中文注释：确保 SelectedItems 至少有一个可用集合。
        /// 如果外部未绑定，则内部创建 List<object> 承载勾选状态。
        /// </summary>
        private static IList EnsureSelectedItemsContainer(ComboBox combo)
        {
            var selected = GetSelectedItems(combo);
            if (selected != null) return selected;

            selected = new List<object>();
            SetSelectedItems(combo, selected);
            return selected;
        }

        private static void ToggleSelectedItem(ComboBox combo, object item)
        {
            var selected = EnsureSelectedItemsContainer(combo);

            var existing = FindExisting(selected, item);
            if (existing != null)
                selected.Remove(existing);
            else
                selected.Add(item);

            RefreshSelectionVisuals(combo);
            RefreshDisplayText(combo);
        }

        private static object FindExisting(IList selected, object item)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var current = selected[i];
                if (ReferenceEquals(current, item) || Equals(current, item))
                    return current;
            }

            return null;
        }

        /// <summary>
        /// 中文注释：刷新下拉项的 IsSelected 状态，仅作为视觉反馈。
        /// 这里不依赖 ComboBox.SelectedItem，避免单选逻辑冲突。
        /// </summary>
        private static void RefreshSelectionVisuals(ComboBox combo)
        {
            var selected = EnsureSelectedItemsContainer(combo);
            foreach (var obj in combo.Items)
            {
                var container = combo.ItemContainerGenerator.ContainerFromItem(obj) as ComboBoxItem;
                if (container == null) continue;

                container.IsSelected = Contains(selected, obj);
            }

            // 中文注释：防止 SelectionBoxItem 显示成单个选中项。
            combo.SelectedItem = null;
        }

        private static bool Contains(IList selected, object item)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var current = selected[i];
                if (ReferenceEquals(current, item) || Equals(current, item))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 中文注释：把多选结果汇总到 ComboBox 文本中，便于筛选栏直接查看当前条件。
        /// </summary>
        private static void RefreshDisplayText(ComboBox combo)
        {
            var selected = EnsureSelectedItemsContainer(combo);
            if (selected.Count == 0)
            {
                combo.Text = string.Empty;
                return;
            }

            var separator = GetSeparator(combo);
            if (string.IsNullOrEmpty(separator)) separator = "、";

            var parts = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                var text = ResolveItemText(combo, selected[i]);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }

            combo.Text = string.Join(separator, parts);
        }

        private static string ResolveItemText(ComboBox combo, object item)
        {
            if (item == null) return string.Empty;

            // 中文注释：优先使用附加属性 DisplayMemberPath，其次使用 ComboBox 自带 DisplayMemberPath。
            var path = GetDisplayMemberPath(combo);
            if (string.IsNullOrWhiteSpace(path))
                path = combo.DisplayMemberPath;

            if (!string.IsNullOrWhiteSpace(path))
            {
                var value = GetPropertyValue(item, path);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return item.ToString();
        }

        private static string GetPropertyValue(object item, string path)
        {
            if (item == null || string.IsNullOrWhiteSpace(path)) return string.Empty;

            var prop = item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null) return string.Empty;

            var value = prop.GetValue(item, null);
            return value?.ToString() ?? string.Empty;
        }

        #endregion

        #region 视觉树工具

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target)
                    return target;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        #endregion
    }
}
