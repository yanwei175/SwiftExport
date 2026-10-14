using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Components;

/// <summary>复用字典明细表格，仅桥接原生编辑、内置字段保护和视觉生命周期。</summary>
public partial class DictionaryDataEditor : UserControl
{
    private DictionaryDataEditorViewModel? _model;
    private readonly Func<bool> _commit;
    private readonly Action _cancel;

    /// <summary>数据上下文由宿主提供，各页面不共享编辑草稿。</summary>
    public DictionaryDataEditor()
    { InitializeComponent(); _commit = CommitPendingEdits; _cancel = CancelPendingEdits; }

    /// <summary>提交单元格和行，转换错误时保留缓冲并阻止后续操作。</summary>
    public bool CommitPendingEdits() => DictionaryGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && DictionaryGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private void CancelPendingEdits()
    { DictionaryGrid.CancelEdit(DataGridEditingUnit.Cell); DictionaryGrid.CancelEdit(DataGridEditingUnit.Row); }
    private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    { Detach(); if (IsLoaded) Attach(); }
    private void Attach()
    {
        if (DataContext is not DictionaryDataEditorViewModel model || ReferenceEquals(model, _model)) return;
        Detach(); _model = model; model.CommitPendingEdits = _commit; model.CancelPendingEdits = _cancel;
    }
    private void Detach()
    {
        if (_model is not { } model) return;
        if (ReferenceEquals(model.CommitPendingEdits, _commit)) model.CommitPendingEdits = null;
        if (ReferenceEquals(model.CancelPendingEdits, _cancel)) model.CancelPendingEdits = null;
        _model = null;
    }

    /// <summary>F2、鼠标及键盘编辑统一保护内置代码与启用状态。</summary>
    private void OnDictionaryBeginningEdit(object sender, DataGridBeginningEditEventArgs args)
    {
        if (args.Row.Item is DictionaryDataLineModel line)
        {
            var requires = line.IsNew ? "Create" : "Edit";
            if (_model?.CanEditRow(requires) != true) { args.Cancel = true; return; }
            if (line.IsBuiltIn && args.Column is DataGridBoundColumn { Binding: Binding binding }
                && binding.Path?.Path is nameof(DictionaryDataLineModel.DataNameEn) or nameof(DictionaryDataLineModel.IsEnabled))
                args.Cancel = true;
        }
    }
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (FindButton(args.OriginalSource as DependencyObject) is { Command: not null } button && !PrepareAction(button)) args.Handled = true;
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.Enter or Key.Space && FindButton(args.OriginalSource as DependencyObject) is { Command: not null } button
            && !PrepareAction(button)) args.Handled = true;
    }
    private bool PrepareAction(ButtonBase button)
    {
        if (ReferenceEquals(button.Command, _model?.RejectCommand)) { CancelPendingEdits(); return true; }
        return CommitPendingEdits();
    }
    private static ButtonBase? FindButton(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ButtonBase button) return button;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
