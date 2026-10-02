using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.Products.Components;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Views;

/// <summary>售后服务单详情视图，展示投诉事实、实际补件及业务命令入口。</summary>
public partial class AfterSalesDetailView : UserControl
{
    private AfterSalesDetailViewModel? _observed;
    private readonly HashSet<ValidationError> _inputErrors=[];
    /// <summary>初始化售后详情视图。</summary>
    public AfterSalesDetailView()
    {
        InitializeComponent();
        Validation.AddErrorHandler(this,OnInputError);
        Loaded += (_, _) => Observe(DataContext as AfterSalesDetailViewModel);
        Unloaded += (_, _) => Observe(null);
        DataContextChanged += (_, _) => { if(IsLoaded) Observe(DataContext as AfterSalesDetailViewModel); };
    }

    // 仅负责错误焦点，不在视图中执行校验、保存或业务状态转换。
    private void Observe(AfterSalesDetailViewModel? model)
    {
        if(_observed is not null)
        {
            _observed.PropertyChanged -= OnValidationChanged;
            _observed.CommitPendingEdits = null;
        }
        _observed=model;
        if(_observed is not null)
        {
            _observed.PropertyChanged += OnValidationChanged;
            _observed.CommitPendingEdits = CommitEdits;
            _observed.HasInputErrors=_inputErrors.Count>0;
        }
    }

    // 只提交原生编辑事务，业务保存仍由页面命令负责。
    private bool CommitEdits() => ReplacementGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && ReplacementGrid.CommitEdit(DataGridEditingUnit.Row, true);

    /// <summary>已确认仅备注可编辑；标准补件单位来自产品快照，非标才允许手工填写。</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs args)
    {
        if (DataContext is not AfterSalesDetailViewModel model || !model.CanEditSupplement || model.IsBusy
            || (!model.CanEditFacts && args.Column.SortMemberPath != "Remark")
            || (args.Column.SortMemberPath == "UnitSnapshot" && args.Row.Item is SwiftExportClient.Features.QualityControl.AfterSales.Models.AfterSalesLineModel { IsNonStandard: false })) args.Cancel = true;
    }

    // 模板编辑开始后把焦点交给产品输入框，不接管表格原生导航键。
    private void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs args)
    {
        FindProductEditor(args.EditingElement)?.FocusEditor();
    }

    private static ProductLookupTextBox? FindProductEditor(DependencyObject element)
    {
        if (element is ProductLookupTextBox editor) return editor;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var result = FindProductEditor(VisualTreeHelper.GetChild(element, i));
            if (result is not null) return result;
        }
        return null;
    }

    // 转换错误属于视图绑定层，只向页面模型传递阻断标记，不把无效文本当旧值提交。
    private void OnInputError(object? sender,ValidationErrorEventArgs args)
    {
        if(args.Action==ValidationErrorEventAction.Added) _inputErrors.Add(args.Error);
        else _inputErrors.Remove(args.Error);
        if(_observed is not null) _observed.HasInputErrors=_inputErrors.Count>0;
    }

    private void OnValidationChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(AfterSalesDetailViewModel.ValidationSummary)) return;
        var summary=_observed?.ValidationSummary;
        if(string.IsNullOrWhiteSpace(summary)) return;
        var separator=summary.IndexOf('：');
        if(separator<0) return;
        var field=summary[..separator];
        if(field.StartsWith("Lines[",StringComparison.Ordinal))
        {
            var end=field.IndexOf("].",StringComparison.Ordinal);
            if(end<0) return;
            field="SelectedLine."+field[(end+2)..];
        }
        else if(!field.StartsWith("Header.",StringComparison.Ordinal)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(() =>
        {
            if(!IsLoaded || !ReferenceEquals(sender,_observed)) return;
            if (field.StartsWith("SelectedLine.", StringComparison.Ordinal) && _observed?.SelectedLine is { } line)
            {
                var column = ReplacementGrid.Columns.FirstOrDefault(x => x.SortMemberPath == field[13..]);
                if (column is not null)
                {
                    ReplacementGrid.ScrollIntoView(line, column);
                    ReplacementGrid.CurrentCell = new DataGridCellInfo(line, column);
                    ReplacementGrid.Focus();
                    ReplacementGrid.BeginEdit();
                }
                return;
            }
            var target=FindField(this,field);
            target?.BringIntoView();
            target?.Focus();
        }));
    }

    // 控件Tag与契约路径对应；选中补件由ViewModel按稳定Uid定位。
    private static FrameworkElement? FindField(DependencyObject parent,string field)
    {
        if(parent is FrameworkElement element && Equals(element.Tag,field)) return element;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var found=FindField(VisualTreeHelper.GetChild(parent,i),field);
            if(found is not null) return found;
        }
        return null;
    }
}
