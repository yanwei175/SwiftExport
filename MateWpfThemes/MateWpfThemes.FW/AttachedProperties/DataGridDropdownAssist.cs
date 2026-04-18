using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGrid 内联 TextBox 专用的独立 Popup 下拉联动助手
    /// 核心优势：外置 Popup + Focusable=False的列表，完美解决原生 ComboBox或内置 Popup 导致的 DataGrid 焦点跳失、编辑提前结束等BUG。
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
        // 支持外部注入多个筛选字段，格式示例："ColorNameZh,ColorNameEn,ColorCode"
        // =======================
        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.RegisterAttached("FilterMemberPaths", typeof(string), typeof(DataGridDropdownAssist), new PropertyMetadata(string.Empty));

        public static string GetFilterMemberPaths(DependencyObject obj) => (string)obj.GetValue(FilterMemberPathsProperty);
        public static void SetFilterMemberPaths(DependencyObject obj, string value) => obj.SetValue(FilterMemberPathsProperty, value);

        // =======================
        // 4. RequireItemMatch 附加属性
        // 为 true 时，TextBox 的值必须命中下拉源中的某项（忽略大小写）
        // =======================
        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.RegisterAttached("RequireItemMatch", typeof(bool), typeof(DataGridDropdownAssist), new PropertyMetadata(false));

        public static bool GetRequireItemMatch(DependencyObject obj) => (bool)obj.GetValue(RequireItemMatchProperty);
        public static void SetRequireItemMatch(DependencyObject obj, bool value) => obj.SetValue(RequireItemMatchProperty, value);

        
        // 全局单例的 Popup 与 ListBox（整个程序中只需一个实例，极大地节省资源）
        private static Popup _sharedPopup;
        private static ListBox _sharedListBox;
        private static TextBox _currentTextBox;

        /// <summary>
        /// 当绑定的数据源改变时，注册 TextBox 的关键事件
        /// </summary>
        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                InitializeSharedPopup();

                // 避免重复注册
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

        /// <summary>
        /// 延迟初始化全局共享的 Popup 与 ListBox
        /// </summary>
        private static void InitializeSharedPopup()
        {
            if (_sharedPopup != null) return;

            _sharedListBox = new ListBox
            {
                // 【核心设定1】使 ListBox 本身不受键盘焦点影响，防止 DataGrid 触发 CommitEdit
                Focusable = false, 
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MaxHeight = 250,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_sharedListBox, ScrollBarVisibility.Disabled);

            // 【核心设定2】重写 ListBoxItem 的样式，让所有的承载项也不能抢夺键盘焦点
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false)); // 禁止项抢夺焦点
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            itemStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            // 为 ListBoxItem 增加一点基础悬浮视觉效果
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            // 这里用了一个浅灰色来表示选中，如果你们项目里有更标准的 Brush 可以替换，比如: {DynamicResource Brush_Control_Hover}
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"))));
            itemStyle.Triggers.Add(hoverTrigger);
            
            _sharedListBox.ItemContainerStyle = itemStyle;

            // 给 ListBox 包一层漂亮的 Border 外壳
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 0),
                Child = _sharedListBox
            };

            // 增加优雅的外阴影效果
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
                StaysOpen = true, // 修改为 true，避免在 DataGrid 中点击进入编辑状态时瞬间触发关闭
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade
            };

            _sharedPopup.Closed += (s, ev) => 
            {
                if (_currentTextBox == null) return;
                // Popup 关闭后，如果文本框没有被选中，就不做啥特殊处理
            };

            // 【核心交互】用户用鼠标点击列表某项时的回填逻辑
            _sharedListBox.PreviewMouseLeftButtonUp += SharedListBox_PreviewMouseLeftButtonUp;
        }

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            _currentTextBox = tb;
            _sharedPopup.PlacementTarget = tb;

            // 设定 Popup 最小宽度。如果 TextBox 很小，Popup 仍然能维持一个合适的阅读宽度。
            _sharedPopup.MinWidth = 150; 

            // 如果 TextBox 的宽度本身足够大，那么让 Popup 保持和它一致的宽度
            if (tb.ActualWidth > 150)
            {
                _sharedPopup.Width = tb.ActualWidth;
            }
            else
            {
                _sharedPopup.Width = 150;
            }

            var itemsSource = GetItemsSource(tb);
            var displayPath = GetDisplayMemberPath(tb);

            // 聚焦时直接进行一次过滤（或者展示全量）
            _sharedListBox.ItemsSource = FilterItems(itemsSource, tb.Text, displayPath, GetFilterMemberPaths(tb));
            _sharedListBox.DisplayMemberPath = displayPath;

            if (_sharedListBox.Items.Count > 0)
            {
                _sharedPopup.IsOpen = true;
            }
        }

        private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb != null)
            {
                EnsureTextInItems(tb);
            }

            // 当焦点离开 TextBox 时关闭 Popup。注意由于我们的 ListBox Focusable=False，点击 ListBox 不会触发文本框失焦！
            if (_sharedPopup != null && _sharedPopup.IsOpen)
            {
                _sharedPopup.IsOpen = false;
            }
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
            else
            {
                _sharedPopup.IsOpen = false;
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_sharedPopup == null || !_sharedPopup.IsOpen || _sharedListBox.Items.Count == 0) return;

            if (e.Key == Key.Down)
            {
                if (_sharedListBox.SelectedIndex < _sharedListBox.Items.Count - 1)
                    _sharedListBox.SelectedIndex++;
                _sharedListBox.ScrollIntoView(_sharedListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (_sharedListBox.SelectedIndex > 0)
                    _sharedListBox.SelectedIndex--;
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
                e.Handled = true; // 可选择是否打断DataGrid自身的Escape
            }
        }

        private static void SharedListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentTextBox == null) return;
            
            // 找到鼠标点击了哪个 ListBoxItem
            if (ItemsControl.ContainerFromElement(_sharedListBox, e.OriginalSource as DependencyObject) is ListBoxItem item)
            {
                SelectValue(item.DataContext);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 值回填到当前 TextBox，并强制更新后端属性
        /// </summary>
        private static void SelectValue(object selectedItem)
        {
            if (_currentTextBox != null && selectedItem != null)
            {
                string displayPath = GetDisplayMemberPath(_currentTextBox);
                string textValue = selectedItem.ToString();

                // 如果设置了映射路径，则通过反射取对应的值
                if (!string.IsNullOrEmpty(displayPath))
                {
                    var prop = selectedItem.GetType().GetProperty(displayPath);
                    if (prop != null)
                    {
                        textValue = prop.GetValue(selectedItem)?.ToString() ?? string.Empty;
                    }
                }

                // 回填文本
                _currentTextBox.Text = textValue;
                
                // ！！！强制更新底层 Binding（这是最核心的，解决失焦前无法写入 ViewModel 的问题）！！！
                var bindingExpr = _currentTextBox.GetBindingExpression(TextBox.TextProperty);
                bindingExpr?.UpdateSource();

                // 游标移到末尾
                _currentTextBox.CaretIndex = _currentTextBox.Text.Length;
                
                // 完毕后隐藏 Popup
                _sharedPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// 内存筛选
        /// </summary>
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
                            isMatched = true;
                            break;
                        }
                    }
                }

                // 包含关键字（忽略大小写），或者在无关键字时显示全部
                if (isMatched)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        private static List<string> ParseFilterPaths(string filterMemberPaths, string displayPath)
        {
            var result = new List<string>();

            if (!string.IsNullOrWhiteSpace(filterMemberPaths))
            {
                var parts = filterMemberPaths
                    .Split(new[] { ',', ';', '|'}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                result.AddRange(parts);
            }

            if (result.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(displayPath))
                {
                    result.Add(displayPath);
                }
                else
                {
                    result.Add(string.Empty);
                }
            }

            return result;
        }

        private static string GetMemberText(object item, string memberPath)
        {
            if (item == null) return string.Empty;

            if (string.IsNullOrWhiteSpace(memberPath))
            {
                return item.ToString() ?? string.Empty;
            }

            var prop = item.GetType().GetProperty(memberPath);
            if (prop == null) return string.Empty;
            return prop.GetValue(item)?.ToString() ?? string.Empty;
        }

        private static void EnsureTextInItems(TextBox tb)
        {
            if (tb == null || !GetRequireItemMatch(tb)) return;

            var text = (tb.Text ?? string.Empty).Trim();
            var bindingExpr = tb.GetBindingExpression(TextBox.TextProperty);

            if (string.IsNullOrEmpty(text))
            {
                bindingExpr?.UpdateSource();
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
            }
            else
            {
                tb.Text = string.Empty;
            }

            bindingExpr?.UpdateSource();
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
                        matchedItem = item;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}