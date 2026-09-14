using System.Windows.Controls;
using System.ComponentModel;
using System.Windows;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

namespace SwiftExportClient.Features.Inventory.Inbounds.Views;

/// <summary>保留入库管理布局，转交首次加载、选择及原生编辑提交事件。</summary>
public partial class InboundManagerView : UserControl
{
    private InboundManagerViewModel? _attachedModel;
    /// <summary>仅初始化布局，不创建 ViewModel 或请求旧接口。</summary>
    public InboundManagerView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
        Unloaded += (_, _) => AttachCommitHandler(null);
        DataContextChanged += (_, _) => { if (IsLoaded) AttachCommitHandler(DataContext as InboundManagerViewModel); };
    }

    /// <summary>实际显示后才加载，设计器不发请求；重复 Loaded 由模型的初始化门禁处理。</summary>
    private async void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        AttachCommitHandler(DataContext as InboundManagerViewModel);
        if (!DesignerProperties.GetIsInDesignMode(this) && _attachedModel is { } model)
            await model.InitializeAsync();
    }

    /// <summary>对称订阅当前 ViewModel，页面卸载后不再持有编辑会话。</summary>
    private void AttachCommitHandler(InboundManagerViewModel? model)
    {
        if (ReferenceEquals(_attachedModel, model)) return;
        if (_attachedModel is not null) _attachedModel.CommitEditsRequested -= OnCommitEditsRequested;
        _attachedModel = model;
        if (_attachedModel is not null) _attachedModel.CommitEditsRequested += OnCommitEditsRequested;
    }

    /// <summary>遵循原生 DataGrid 编辑事务；转换错误时禁止发送旧值。</summary>
    private void OnCommitEditsRequested(object? sender, CancelEventArgs e)
    {
        e.Cancel = !LineGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !LineGrid.CommitEdit(DataGridEditingUnit.Row, true)
            || !HeaderGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !HeaderGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (e.Cancel) KeyboardFocusInvalidGrid();
    }

    private void KeyboardFocusInvalidGrid()
    {
        if (Validation.GetHasError(LineGrid)) LineGrid.Focus();
        else HeaderGrid.Focus();
    }

    /// <summary>只响应单头 Grid 自身的选择，不把模板内下拉框的冒泡事件当作切换单据。</summary>
    private async void OnHeaderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && DataContext is InboundManagerViewModel vm)
            await vm.LoadSelectedDetailCommand.ExecuteAsync(null);
    }
}
