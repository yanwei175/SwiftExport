using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Products.ProductList.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>包装表格原生事务桥；业务增删改、候选查询及验证均属于子 ViewModel。</summary>
public partial class ProductPackagingEditorView : UserControl
{
    private ProductPackagingEditorViewModel? _attached;
    /// <summary>创建包装表格视觉。</summary>
    public ProductPackagingEditorView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) { if (IsLoaded) Attach(); }
    private void Attach()
    { Detach(); _attached = DataContext as ProductPackagingEditorViewModel; if (_attached is not null) { _attached.CommitPendingEdits = Commit; _attached.CancelPendingEdits = Cancel; } }
    private void Detach()
    { if (_attached?.CommitPendingEdits == Commit) { _attached.CommitPendingEdits = null; _attached.CancelPendingEdits = null; } _attached = null; }
    /// <summary>提交当前单元格和行，转换错误时保持焦点及输入。</summary>
    private bool Commit() => PackageGrid.CommitEdit(DataGridEditingUnit.Cell, true) && PackageGrid.CommitEdit(DataGridEditingUnit.Row, true);
    /// <summary>用户明确撤销时取消原生事务，避免旧缓冲反写撤销后的行。</summary>
    private void Cancel() { PackageGrid.CancelEdit(DataGridEditingUnit.Cell); PackageGrid.CancelEdit(DataGridEditingUnit.Row); }
}
