using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.ViewModels;

namespace SwiftExportClient.Features.Products.Bom.Views;

/// <summary>主从视图仅桥接原生编辑事务和延迟主表选择，业务读写由页面命令负责。</summary>
public partial class BomManagerView : UserControl
{
    private BomManagerViewModel? _model;
    private int _selectionIdentity;
    /// <summary>创建视图和原生 DataGrid，不接管其编辑键盘行为。</summary>
    public BomManagerView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) Attach(); }
    private void Attach()
    {
        Detach(); _model = DataContext as BomManagerViewModel;
        if (_model is not null) _model.CommitPendingEdits = CommitEdits;
    }
    private void Detach()
    {
        _selectionIdentity++;
        if (_model is not null) _model.CommitPendingEdits = null;
        _model = null;
    }
    /// <summary>原生选择完成后请求主从切换；取消时回读模型选择，过期选择不得覆盖新选择。</summary>
    private void OnProductSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ProductGrid) || _model is not { } model || model.IsApplyingSelection
            || ProductGrid.SelectedItem is not BomProductItemModel product || (ReferenceEquals(product, model.SelectedProduct) && !model.IsLoadingBom)) return;
        var identity = ++_selectionIdentity;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            if (!ReferenceEquals(_model, model) || !IsLoaded || identity != _selectionIdentity) return;
            try { await model.SelectProductCommand.ExecuteAsync(product); }
            catch (Exception exception) { System.Diagnostics.Trace.TraceError("BOM 主表切换失败：{0}", exception); }
            finally
            {
                if (ReferenceEquals(_model, model) && identity == _selectionIdentity)
                    ProductGrid.GetBindingExpression(DataGrid.SelectedItemProperty)?.UpdateTarget();
            }
        }));
    }
    /// <summary>按行新建/已保存状态检查操作权限，未知结果核实前禁止继续修改。</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e) =>
        e.Cancel = _model is null || e.Row.Item is not BomLineModel line || !_model.CanEditLine(line) || _model.IsSaveOutcomeUnknown
            || (e.Column == ComponentGrid.Columns[1] && !_model.CanLookupProducts);
    /// <summary>转换错误由原生 DataGrid 保留并提示，失败时阻断保存、切换和关闭。</summary>
    private bool CommitEdits() => ComponentGrid.CommitEdit(DataGridEditingUnit.Cell, true) && ComponentGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
