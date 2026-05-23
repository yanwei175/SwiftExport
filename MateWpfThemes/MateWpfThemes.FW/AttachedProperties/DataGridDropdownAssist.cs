using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGrid 内联 TextBox 专用的独立 Popup 下拉联动助手
    /// </summary>
    public static class DataGridDropdownAssist
    {
        // =======================
        // 1. ItemsSource 附加属性
        // =======================
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.RegisterAttached("ItemsSource", typeof(IEnumerable), typeof(DataGridDropdownAssist), new PropertyMetadata(null, OnItemsSourceChanged));
        public static IEnumerable GetItemsSource(DependencyObject obj) => (IEnumerable)obj.GetValue(ItemsSourceProperty);
        public static void SetItemsSource(DependencyObject obj, IEnumerable value) => obj.SetValue(ItemsSourceProperty, value);

        // =======================
        // 2. DisplayMemberPath 附加属性
        // =======================
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.RegisterAttached("DisplayMemberPath", typeof(string), typeof(DataGridDropdownAssist), new PropertyMetadata(string.Empty));
        public static string GetDisplayMemberPath(DependencyObject obj) => (string)obj.GetValue(DisplayMemberPathProperty);
        public static void SetDisplayMemberPath(DependencyObject obj, string value) => obj.SetValue(DisplayMemberPathProperty, value);

        // =======================
        // 3. FilterMemberPaths 附加属性
        // =======================
        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.RegisterAttached("FilterMemberPaths", typeof(string), typeof(DataGridDropdownAssist), new PropertyMetadata(string.Empty));
        public static string GetFilterMemberPaths(DependencyObject obj) => (string)obj.GetValue(FilterMemberPathsProperty);
        public static void SetFilterMemberPaths(DependencyObject obj, string value) => obj.SetValue(FilterMemberPathsProperty, value);

        // =======================
        // 4. RequireItemMatch 附加属性
        // =======================
        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.RegisterAttached("RequireItemMatch", typeof(bool), typeof(DataGridDropdownAssist), new PropertyMetadata(false));
        public static bool GetRequireItemMatch(DependencyObject obj) => (bool)obj.GetValue(RequireItemMatchProperty);
        public static void SetRequireItemMatch(DependencyObject obj, bool value) => obj.SetValue(RequireItemMatchProperty, value);

        // =======================
        // 5. SelectedValuePath 附加属性 (新增：源数据的ID字段，如 "Id")
        // =======================
        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.RegisterAttached("SelectedValuePath", typeof(string), typeof(DataGridDropdownAssist), new PropertyMetadata(string.Empty));
        public static string GetSelectedValuePath(DependencyObject obj) => (string)obj.GetValue(SelectedValuePathProperty);
        public static void SetSelectedValuePath(DependencyObject obj, string value) => obj.SetValue(SelectedValuePathProperty, value);

        // =======================
        // 6. TargetValuePath 附加属性 (新增：目标Model的接收字段，如 "ColorId")
        // =======================
        public static readonly DependencyProperty TargetValuePathProperty =
            DependencyProperty.RegisterAttached("TargetValuePath", typeof(string), typeof(DataGridDropdownAssist), new PropertyMetadata(string.Empty));
        public static string GetTargetValuePath(DependencyObject obj) => (string)obj.GetValue(TargetValuePathProperty);
        public static void SetTargetValuePath(DependencyObject obj, string value) => obj.SetValue(TargetValuePathProperty, value);


        private static Popup _sharedPopup;
        private static ListBox _sharedListBox;
        private static TextBox _currentTextBox;

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                InitializeSharedPopup();
                tb.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
                tb.LostKeyboardFocus -= TextBox_LostKeyboardFocus;
                tb.TextChanged -= TextBox_TextChanged;
                tb.PreviewKeyDown -= TextBox_PreviewKeyDown;

                if (e.NewValue != null)
                {
                    tb.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                    tb.LostKeyboardFocus += TextBox_LostKeyboardFocus;
                    tb.TextChanged += TextBox_TextChanged;
                    tb.PreviewKeyDown += TextBox_PreviewKeyDown;
                }
            }
        }

        private static void InitializeSharedPopup()
        {
            if (_sharedPopup != null) return;

            _sharedListBox = new ListBox
            {
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MaxHeight = 250,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_sharedListBox, ScrollBarVisibility.Disabled);

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            itemStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"))));
            itemStyle.Triggers.Add(hoverTrigger);

            _sharedListBox.ItemContainerStyle = itemStyle;

            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 0),
                Child = _sharedListBox
            };

            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.15,
                BlurRadius = 15,
                ShadowDepth = 4,
                Direction = 270
            };

            _sharedPopup = new Popup
            {
                Child = border,
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade
            };

            _sharedListBox.PreviewMouseLeftButtonUp += SharedListBox_PreviewMouseLeftButtonUp;
        }

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            _currentTextBox = tb;
            _sharedPopup.PlacementTarget = tb;
            _sharedPopup.MinWidth = 150;
            _sharedPopup.Width = tb.ActualWidth > 150 ? tb.ActualWidth : 150;

            var itemsSource = GetItemsSource(tb);
            var displayPath = GetDisplayMemberPath(tb);

            _sharedListBox.ItemsSource = FilterItems(itemsSource, tb.Text, displayPath, GetFilterMemberPaths(tb));
            _sharedListBox.DisplayMemberPath = displayPath;

            if (_sharedListBox.Items.Count > 0) _sharedPopup.IsOpen = true;
        }

        private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb != null) EnsureTextInItems(tb);
            if (_sharedPopup != null && _sharedPopup.IsOpen) _sharedPopup.IsOpen = false;
        }

        private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null || _currentTextBox != tb) return;

            var itemsSource = GetItemsSource(tb);
            _sharedListBox.ItemsSource = FilterItems(itemsSource, tb.Text, GetDisplayMemberPath(tb), GetFilterMemberPaths(tb));

            if (_sharedListBox.Items.Count > 0)
            {
                if (!_sharedPopup.IsOpen) _sharedPopup.IsOpen = true;
            }
            else _sharedPopup.IsOpen = false;
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_sharedPopup == null || !_sharedPopup.IsOpen || _sharedListBox.Items.Count == 0) return;

            if (e.Key == Key.Down)
            {
                if (_sharedListBox.SelectedIndex < _sharedListBox.Items.Count - 1) _sharedListBox.SelectedIndex++;
                _sharedListBox.ScrollIntoView(_sharedListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (_sharedListBox.SelectedIndex > 0) _sharedListBox.SelectedIndex--;
                _sharedListBox.ScrollIntoView(_sharedListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (_sharedListBox.SelectedItem != null)
                {
                    SelectValue(_sharedListBox.SelectedItem);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                _sharedPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private static void SharedListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentTextBox == null) return;
            if (ItemsControl.ContainerFromElement(_sharedListBox, e.OriginalSource as DependencyObject) is ListBoxItem item)
            {
                SelectValue(item.DataContext);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 回填文字并自动写入目标 ID 字段
        /// </summary>
        private static void SelectValue(object selectedItem)
        {
            if (_currentTextBox != null && selectedItem != null)
            {
                string displayPath = GetDisplayMemberPath(_currentTextBox);
                string textValue = selectedItem.ToString();

                if (!string.IsNullOrEmpty(displayPath))
                {
                    var prop = selectedItem.GetType().GetProperty(displayPath);
                    if (prop != null) textValue = prop.GetValue(selectedItem)?.ToString() ?? string.Empty;
                }

                _currentTextBox.Text = textValue;
                _currentTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

                // 【核心增强】回填关联 ID 值
                SyncTargetValue(_currentTextBox, selectedItem);

                _currentTextBox.CaretIndex = _currentTextBox.Text.Length;
                _sharedPopup.IsOpen = false;
            }
        }

        private static void EnsureTextInItems(TextBox tb)
        {
            if (tb == null || !GetRequireItemMatch(tb)) return;

            var text = (tb.Text ?? string.Empty).Trim();
            var bindingExpr = tb.GetBindingExpression(TextBox.TextProperty);

            if (string.IsNullOrEmpty(text))
            {
                bindingExpr?.UpdateSource();
                ClearTargetValue(tb); // 如果清空了文字，也要清空关联的 ID
                return;
            }

            var displayPath = GetDisplayMemberPath(tb);
            var filterPaths = ParseFilterPaths(GetFilterMemberPaths(tb), displayPath);
            object matchedItem;

            if (TryFindExactMatch(GetItemsSource(tb), text, filterPaths, out matchedItem))
            {
                var canonicalText = GetMemberText(matchedItem, displayPath);
                if (!string.IsNullOrEmpty(canonicalText) && !string.Equals(tb.Text, canonicalText, StringComparison.Ordinal))
                {
                    tb.Text = canonicalText;
                }
                SyncTargetValue(tb, matchedItem); // 找到匹配项，回填 ID
            }
            else
            {
                tb.Text = string.Empty;
                ClearTargetValue(tb); // 没匹配到，清空关联的 ID
            }

            bindingExpr?.UpdateSource();
        }

        /// <summary>
        /// 同步值到 Model 的属性（如 ColorId）
        /// </summary>
        private static void SyncTargetValue(TextBox tb, object selectedItem)
        {
            if (tb == null || selectedItem == null) return;

            string sourcePath = GetSelectedValuePath(tb);
            string targetPath = GetTargetValuePath(tb);

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath)) return;

            var sourceProp = selectedItem.GetType().GetProperty(sourcePath);
            if (sourceProp == null) return;

            var dataContext = tb.DataContext;
            if (dataContext == null) return;

            var targetProp = dataContext.GetType().GetProperty(targetPath);
            if (targetProp == null || !targetProp.CanWrite) return;

            try
            {
                var sourceValue = sourceProp.GetValue(selectedItem);
                // 使用 ChangeType 确保类型安全（比如 int 赋值给 int?）
                var safeValue = sourceValue == null ? null : Convert.ChangeType(sourceValue, Nullable.GetUnderlyingType(targetProp.PropertyType) ?? targetProp.PropertyType);
                targetProp.SetValue(dataContext, safeValue);
            }
            catch { /* 忽略转换异常，避免崩溃 */ }
        }

        /// <summary>
        /// 当文本被清空或不匹配时，清空关联的 ID 字段
        /// </summary>
        private static void ClearTargetValue(TextBox tb)
        {
            if (tb == null) return;
            string targetPath = GetTargetValuePath(tb);
            if (string.IsNullOrEmpty(targetPath)) return;

            var dataContext = tb.DataContext;
            if (dataContext == null) return;

            var targetProp = dataContext.GetType().GetProperty(targetPath);
            if (targetProp == null || !targetProp.CanWrite) return;

            try
            {
                // 如果是值类型（如 int），清零；如果是引用类型或可空类型，赋 null
                object defaultValue = targetProp.PropertyType.IsValueType ? Activator.CreateInstance(targetProp.PropertyType) : null;
                targetProp.SetValue(dataContext, defaultValue);
            }
            catch { }
        }

        // ... (FilterItems, ParseFilterPaths, GetMemberText, TryFindExactMatch 方法保持你原有的逻辑不变)
        private static IList FilterItems(IEnumerable source, string keyword, string displayPath, string filterMemberPaths)
        {
            var list = new List<object>();
            if (source == null) return list;
            var filterPaths = ParseFilterPaths(filterMemberPaths, displayPath);
            foreach (var item in source)
            {
                if (item == null) continue;
                bool isMatched = string.IsNullOrEmpty(keyword);
                if (!isMatched)
                {
                    foreach (var path in filterPaths)
                    {
                        string text = GetMemberText(item, path);
                        if (!string.IsNullOrEmpty(text) && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatched = true; break;
                        }
                    }
                }
                if (isMatched) list.Add(item);
            }
            return list;
        }

        private static List<string> ParseFilterPaths(string filterMemberPaths, string displayPath)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(filterMemberPaths))
            {
                var parts = filterMemberPaths.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
                result.AddRange(parts);
            }
            if (result.Count == 0) result.Add(!string.IsNullOrWhiteSpace(displayPath) ? displayPath : string.Empty);
            return result;
        }

        private static string GetMemberText(object item, string memberPath)
        {
            if (item == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(memberPath)) return item.ToString() ?? string.Empty;
            var prop = item.GetType().GetProperty(memberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        private static bool TryFindExactMatch(IEnumerable source, string input, IEnumerable<string> paths, out object matchedItem)
        {
            matchedItem = null;
            if (source == null) return false;
            var normalizedInput = (input ?? string.Empty).Trim();
            foreach (var item in source)
            {
                if (item == null) continue;
                foreach (var path in paths)
                {
                    var text = GetMemberText(item, path);
                    if (string.Equals((text ?? string.Empty).Trim(), normalizedInput, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedItem = item; return true;
                    }
                }
            }
            return false;
        }
    }
}