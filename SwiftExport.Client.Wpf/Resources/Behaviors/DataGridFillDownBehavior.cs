using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>将原生矩形选区转换为页面填充命令；不写业务属性、不维护撤销历史。</summary>
public sealed class DataGridFillDownBehavior : Behavior<DataGrid>
{
    /// <summary>页面业务填充命令，执行时再次检查当前许可。</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(DataGridFillDownBehavior));

    /// <summary>列的业务填充字段，默认空表示禁止；不从 Binding 或验证元数据推断。</summary>
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field", typeof(string), typeof(DataGridFillDownBehavior));

    /// <summary>仅在宿主加载期间注册的 Ctrl+D 路由入口。</summary>
    public static RoutedUICommand FillDownCommand { get; } = new(
        "向下填充", nameof(FillDownCommand), typeof(DataGridFillDownBehavior),
        new InputGestureCollection { new KeyGesture(Key.D, ModifierKeys.Control) });

    private CommandBinding? _binding;

    /// <summary>获取或设置业务命令。</summary>
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>读取列的显式填充字段。</summary>
    public static string? GetField(DependencyObject element) => (string?)element.GetValue(FieldProperty);

    /// <summary>设置列的显式填充字段；空值禁止填充。</summary>
    public static void SetField(DependencyObject element, string? value) => element.SetValue(FieldProperty, value);

    /// <summary>订阅宿主生命周期，避免缓存页卸载期间仍处理命令。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
        if (AssociatedObject.IsLoaded) AttachCommand();
    }

    /// <summary>只移除本行为拥有的绑定，不干预宿主和编辑器的其他命令。</summary>
    protected override void OnDetaching()
    {
        DetachCommand();
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachCommand();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachCommand();

    private void AttachCommand()
    {
        if (_binding is not null) return;
        _binding = new CommandBinding(FillDownCommand, Execute, CanExecute);
        AssociatedObject.CommandBindings.Add(_binding);
    }

    private void DetachCommand()
    {
        if (_binding is null) return;
        AssociatedObject.CommandBindings.Remove(_binding);
        _binding = null;
    }

    private void CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CreateRequest() is { } request && Command?.CanExecute(request) == true;
        e.Handled = true;
    }

    /// <summary>原生单元格及行提交成功后重新读取选区，转换失败时保留编辑器输入且不填充。</summary>
    private void Execute(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        var grid = AssociatedObject;
        if (CreateRequest() is not { } initial || Command?.CanExecute(initial) != true) return;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        if (CreateRequest() is { } request && Command?.CanExecute(request) == true) Command.Execute(request);
    }

    /// <summary>按当前视图和可见列顺序检查完整矩形；不实现容器扫描或选区状态镜像。</summary>
    private FillDownRequest? CreateRequest()
    {
        var grid = AssociatedObject;
        if (!grid.IsLoaded || !grid.IsEnabled || grid.IsReadOnly || grid.SelectionUnit == DataGridSelectionUnit.FullRow) return null;
        var cells = grid.SelectedCells.Where(cell => cell.IsValid && cell.Item is not null
            && !ReferenceEquals(cell.Item, CollectionView.NewItemPlaceholder) && cell.Column.Visibility == Visibility.Visible).ToArray();
        if (cells.Length == 0) return null;
        var rows = cells.Select(cell => grid.Items.IndexOf(cell.Item)).Distinct().Order().ToArray();
        var visible = grid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex).ToArray();
        var columns = cells.Select(cell => cell.Column).Distinct().OrderBy(column => column.DisplayIndex).ToArray();
        var indexes = columns.Select(column => Array.IndexOf(visible, column)).ToArray();
        if (rows.Length < 2 || rows[0] < 0 || rows[^1] - rows[0] + 1 != rows.Length
            || indexes[^1] - indexes[0] + 1 != indexes.Length || cells.Length != rows.Length * columns.Length) return null;
        var fields = columns.Where(column => !column.IsReadOnly).Select(GetField)
            .Where(field => !string.IsNullOrWhiteSpace(field)).Cast<string>().Distinct().ToArray();
        return fields.Length == 0 ? null : new FillDownRequest(grid.Items[rows[0]], rows.Skip(1).Select(index => grid.Items[index]), fields);
    }
}
