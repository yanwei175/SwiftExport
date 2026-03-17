using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SwiftExport.UI.Controls.Expand
{
    /// <summary>
    /// 在ResourceDictionary中定义
    /// 引入命名空间 xmlns:beh="clr-namespace:SwiftExport.UI.Controls.Expand;assembly=SwiftExport.UI.Controls"
    ///     <Window.Resources>
    ///   <ResourceDictionary>
    ///   <Style x:Key="CustomerAutoCompleteEditStyle" TargetType="TextBox" BasedOn="{StaticResource DataGridTextBoxStyle}">
    ///     <Setter Property = "beh:AutoCompleteBehavior.ItemsSource" Value="{Binding DataContext.Customers, RelativeSource={RelativeSource AncestorType=DataGrid}}" />
    ///    <Setter Property = "beh:AutoCompleteBehavior.DisplayMemberPath" Value="CustomerCode" />
    /// </Style>
    /// </ResourceDictionary>
    ///   </Window.Resources>
    /// 然后在DataGridTextColumn里直接套用这个EditingElementStyle  
    ///                     <DataGridTextColumn Header="列头" Width="80"                                        
    ///                                   EditingElementStyle="{StaticResource CustomerAutoCompleteEditStyle}" />
    /// 
    /// 在ViewModel里提供一个属性（比如 Customers）来绑定数据源，AutoCompleteBehavior就会自动帮你搞定搜索和弹窗的逻辑
    /// 
    /// 
    /// </summary>
    public static class AutoCompleteBehavior
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.RegisterAttached("ItemsSource", typeof(IEnumerable), typeof(AutoCompleteBehavior), new PropertyMetadata(null, OnItemsSourceChanged));
        public static IEnumerable GetItemsSource(DependencyObject obj) => (IEnumerable)obj.GetValue(ItemsSourceProperty);
        public static void SetItemsSource(DependencyObject obj, IEnumerable value) => obj.SetValue(ItemsSourceProperty, value);

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.RegisterAttached("DisplayMemberPath", typeof(string), typeof(AutoCompleteBehavior), new PropertyMetadata(string.Empty));
        public static string GetDisplayMemberPath(DependencyObject obj) => (string)obj.GetValue(DisplayMemberPathProperty);
        public static void SetDisplayMemberPath(DependencyObject obj, string value) => obj.SetValue(DisplayMemberPathProperty, value);

        private static readonly DependencyProperty PopupProperty = DependencyProperty.RegisterAttached("Popup", typeof(Popup), typeof(AutoCompleteBehavior));
        private static readonly DependencyProperty ListBoxProperty = DependencyProperty.RegisterAttached("ListBox", typeof(ListBox), typeof(AutoCompleteBehavior));
        private static readonly DependencyProperty IsUpdatingTextProperty = DependencyProperty.RegisterAttached("IsUpdatingText", typeof(bool), typeof(AutoCompleteBehavior));

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                tb.Loaded -= Tb_Loaded;
                tb.Unloaded -= Tb_Unloaded;
                if (e.NewValue != null)
                {
                    tb.Loaded += Tb_Loaded;
                    tb.Unloaded += Tb_Unloaded;
                }
            }
        }

        private static void Tb_Loaded(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            tb.Focus(); // 抢夺焦点

            if (tb.GetValue(PopupProperty) == null)
            {
                // ==========================================
                // 把 ListBox 变成幽灵，不准抢焦点！
                // ==========================================
                var listBox = new ListBox
                {
                    MaxHeight = 250,
                    BorderThickness = new Thickness(0),
                    DisplayMemberPath = GetDisplayMemberPath(tb),
                    Margin = new Thickness(5),
                    Background = Brushes.Transparent,
                    Focusable = false // 绝对禁止获得焦点！
                };
                listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                VirtualizingPanel.SetIsVirtualizing(listBox, true);
                VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);

                var border = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),

                    Child = listBox,
                    Effect = new DropShadowEffect { BlurRadius = 10, Opacity = 0.2, ShadowDepth = 2, RenderingBias = RenderingBias.Performance }
                };

                var popup = new Popup
                {
                    PlacementTarget = tb,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    Child = border,
                    Focusable = false, // 弹窗本身也不准抢焦点！
                    MinWidth = tb.ActualWidth > 150 ? tb.ActualWidth : 150
                };

                tb.SetValue(PopupProperty, popup);
                tb.SetValue(ListBoxProperty, listBox);

                tb.TextChanged += Tb_TextChanged;
                tb.PreviewKeyDown += Tb_PreviewKeyDown;

                // ★ 核心修复 2：拦截鼠标按下事件，而不是弹起事件
                listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;


                if (!string.IsNullOrEmpty(tb.Text))
                {
                    // 把光标移到文字最后，防止光标跑到第一个字母前面
                    tb.CaretIndex = tb.Text.Length;
                    // 强行触发一次搜索
                    Tb_TextChanged(tb, null);
                }

            }
        }

        private static void Tb_Unloaded(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            if (tb.GetValue(PopupProperty) is Popup popup) popup.IsOpen = false;
        }

        private static void Tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            if ((bool)tb.GetValue(IsUpdatingTextProperty)) return;

            var popup = (Popup)tb.GetValue(PopupProperty);
            var listBox = (ListBox)tb.GetValue(ListBoxProperty);
            var items = GetItemsSource(tb);

            if (items == null || popup == null) return;

            string searchText = tb.Text?.Trim() ?? string.Empty;
            string displayPath = GetDisplayMemberPath(tb);

            // ==========================================
            // ★ 性能优化 3：异步非阻塞渲染！
            // 使用 Background 优先级，意味着：“先让用户把字打上去，等 UI 线程闲下来了，再去算过滤和开弹窗”
            // ==========================================
            tb.Dispatcher.InvokeAsync(() =>
            {
                var filteredList = new List<object>();
                foreach (var item in items)
                {
                    string val = GetPropertyValue(item, displayPath) ?? string.Empty;
                    if (string.IsNullOrEmpty(searchText) || val.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        filteredList.Add(item);
                    }
                }

                listBox.ItemsSource = filteredList;

                bool shouldOpen = filteredList.Count > 0;
                if (popup.IsOpen != shouldOpen)
                {
                    popup.IsOpen = shouldOpen; // 异步打开，绝对不卡主线程
                }

                if (filteredList.Count > 0) listBox.SelectedIndex = -1;

            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        // ==========================================
        // ★ 核心修复 3：在 TextBox 里“遥控” ListBox，坚决不交出焦点
        // ==========================================
        private static void Tb_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var tb = (TextBox)sender;
            var popup = (Popup)tb.GetValue(PopupProperty);
            var listBox = (ListBox)tb.GetValue(ListBoxProperty);

            if (popup != null && popup.IsOpen)
            {
                if (e.Key == Key.Down)
                {
                    if (listBox.SelectedIndex < listBox.Items.Count - 1) listBox.SelectedIndex++;
                    listBox.ScrollIntoView(listBox.SelectedItem);
                    e.Handled = true; // 拦截事件，防止光标乱跑
                }
                else if (e.Key == Key.Up)
                {
                    if (listBox.SelectedIndex > 0) listBox.SelectedIndex--;
                    listBox.ScrollIntoView(listBox.SelectedItem);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    if (listBox.SelectedItem != null)
                    {
                        CommitSelection(tb, listBox, popup);
                        e.Handled = true; // 拦截第一次回车，只用来选中，不退出单元格编辑！
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                Tb_TextChanged(tb, null); // 如果没开，按向下箭头强行打开搜索
                e.Handled = true;
            }
        }

        // ==========================================
        // ★ 核心修复 4：鼠标按下瞬间锁定数据，拦截焦点转移！
        // ==========================================
        private static void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var listBox = (ListBox)sender;

            // 找到鼠标到底点中了哪一行
            var item = ItemsControl.ContainerFromElement(listBox, (DependencyObject)e.OriginalSource) as ListBoxItem;
            if (item != null)
            {
                listBox.SelectedItem = item.DataContext;

                var border = (Border)listBox.Parent;
                var popup = (Popup)border.Parent;
                var tb = (TextBox)popup.PlacementTarget;

                CommitSelection(tb, listBox, popup);

                // ★ 致命拦截：告诉 WPF“我已经处理了鼠标点击，不准把焦点移出 TextBox！”
                e.Handled = true;
            }
        }

        private static void CommitSelection(TextBox tb, ListBox listBox, Popup popup)
        {
            if (listBox.SelectedItem == null) return;

            string displayPath = GetDisplayMemberPath(tb);
            string selectedValue = GetPropertyValue(listBox.SelectedItem, displayPath);

            // 更新 UI
            tb.SetValue(IsUpdatingTextProperty, true);
            tb.Text = selectedValue;
            tb.CaretIndex = tb.Text.Length;
            tb.SetValue(IsUpdatingTextProperty, false);

            // ★ 暴力推送给 ViewModel！
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            // 关闭弹窗
            popup.IsOpen = false;
        }

        private static string GetPropertyValue(object item, string propertyName)
        {
            if (item == null || string.IsNullOrEmpty(propertyName)) return string.Empty;
            PropertyInfo prop = item.GetType().GetProperty(propertyName);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }
    }
}