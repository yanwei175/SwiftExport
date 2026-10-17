using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using SwiftExportClient.Resources.Converters;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>为原生表格在菜单首项提供独立的查看菜单；只维护列可见性，不访问业务模型或筛选扩展。</summary>
public sealed class DataGridColumnVisibilityBehavior : Behavior<DataGrid>
{
    private ContextMenu? _menu;
    private MenuItem? _columnsMenu;
    private bool _ownsMenu;

    /// <summary>挂接宿主生命周期；已加载时立即添加菜单。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
        AssociatedObject.ContextMenuOpening += OnContextMenuOpening;
        if (AssociatedObject.IsLoaded) EnsureMenu();
    }

    /// <summary>移除自有菜单和订阅，保留宿主原有菜单项。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        AssociatedObject.ContextMenuOpening -= OnContextMenuOpening;
        ReleaseMenu();
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureMenu();
    private void OnUnloaded(object sender, RoutedEventArgs e) => ReleaseMenu();
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => EnsureMenu();

    /// <summary>兼容已有菜单及后续替换；列对象直接绑定，不镜像列状态。</summary>
    private void EnsureMenu()
    {
        if (_menu is not null && ReferenceEquals(_menu, AssociatedObject.ContextMenu)) return;
        ReleaseMenu();
        _menu = AssociatedObject.ContextMenu;
        _ownsMenu = _menu is null;
        if (_menu is null)
        {
            _menu = new ContextMenu();
            AssociatedObject.SetCurrentValue(FrameworkElement.ContextMenuProperty, _menu);
        }
        var style = new Style(typeof(MenuItem), AssociatedObject.TryFindResource(typeof(MenuItem)) as Style);
        style.Setters.Add(new Setter(HeaderedItemsControl.HeaderProperty, new Binding(nameof(DataGridColumn.Header))));
        style.Setters.Add(new Setter(MenuItem.IsCheckableProperty, true));
        style.Setters.Add(new Setter(MenuItem.StaysOpenOnClickProperty, true));
        style.Setters.Add(new Setter(MenuItem.IsCheckedProperty, new Binding(nameof(DataGridColumn.Visibility))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            Converter = new VisibilityToBooleanConverter()
        }));
        _columnsMenu = new MenuItem { Header = "查看", ItemContainerStyle = style };
        _columnsMenu.Click += OnColumnClick;
        // 图标实例由宿主资源提供；卸载菜单时不再动态解析并创建新控件。
        _columnsMenu.Icon = AssociatedObject.TryFindResource("DataGridColumnVisibilityIcon");
        _menu.Items.Insert(0, _columnsMenu);
        var columns = new MultiBinding { Converter = new DataGridColumnMenuItemsConverter() };
        columns.Bindings.Add(new Binding(nameof(DataGrid.Columns)) { Source = AssociatedObject });
        columns.Bindings.Add(new Binding(nameof(ContextMenu.IsOpen)) { Source = _menu });
        _columnsMenu.SetBinding(ItemsControl.ItemsSourceProperty, columns);
    }

    /// <summary>只有原生编辑可提交时才隐藏列；失败恢复勾选并保留输入及原生验证反馈。</summary>
    private void OnColumnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem { DataContext: DataGridColumn } item) return;
        var binding = item.GetBindingExpression(MenuItem.IsCheckedProperty);
        if (binding is null) return;
        e.Handled = true;
        if (AssociatedObject.CommitEdit(DataGridEditingUnit.Cell, true)
            && AssociatedObject.CommitEdit(DataGridEditingUnit.Row, true)) binding.UpdateSource();
        else binding.UpdateTarget();
    }

    /// <summary>卸载时解除外部菜单订阅并释放列引用，仅移除本行为创建的内容。</summary>
    private void ReleaseMenu()
    {
        if (_menu is null) return;
        if (_columnsMenu is not null)
        {
            _columnsMenu.Click -= OnColumnClick;
            BindingOperations.ClearBinding(_columnsMenu, ItemsControl.ItemsSourceProperty);
            _menu.Items.Remove(_columnsMenu);
        }
        if (_ownsMenu && _menu.Items.Count == 0 && ReferenceEquals(AssociatedObject.ContextMenu, _menu))
            AssociatedObject.SetCurrentValue(FrameworkElement.ContextMenuProperty, null);
        _columnsMenu = null;
        _menu = null;
        _ownsMenu = false;
    }
}
