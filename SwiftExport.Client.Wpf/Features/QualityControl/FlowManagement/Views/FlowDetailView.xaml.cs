using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Views;

/// <summary>详情视图只负责原生编辑提交、转换错误与焦点，不调用 API 或承担业务保存。</summary>
public partial class FlowDetailView : UserControl
{
    private FlowDetailViewModel? _observed;
    private readonly HashSet<ValidationError> _errors = [];

    /// <summary>初始化视图和当前文化，加载与卸载对称接入视图回调。</summary>
    public FlowDetailView()
    {
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        Validation.AddErrorHandler(this, OnInputError);
        Loaded += (_, _) => Observe(DataContext as FlowDetailViewModel);
        Unloaded += (_, _) => Observe(null);
        DataContextChanged += (_, _) => { if (IsLoaded) Observe(DataContext as FlowDetailViewModel); };
    }

    /// <summary>切换页面模型时解绑旧对象，防止视图卸载后仍接纳焦点请求。</summary>
    private void Observe(FlowDetailViewModel? model)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnModelChanged;
            _observed.CommitPendingEdits = null;
        }
        _observed = model;
        if (model is not null)
        {
            model.CommitPendingEdits = CommitEdits;
            model.HasInputErrors = _errors.Count > 0;
            model.PropertyChanged += OnModelChanged;
        }
    }

    /// <summary>绑定转换失败不允许发送模型里残留的旧数字；业务日期按当前 UI 文化解析。</summary>
    private bool CommitEdits()
    {
        var valid = FlowGrid.CommitEdit(DataGridEditingUnit.Cell, true) && FlowGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (string.IsNullOrWhiteSpace(TestDatePicker.Text))
            TestDatePicker.SetCurrentValue(DatePicker.SelectedDateProperty, null);
        else if (DateTime.TryParse(TestDatePicker.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
            TestDatePicker.SetCurrentValue(DatePicker.SelectedDateProperty, date.Date);
        else
        {
            valid = false;
            _observed?.Header.SetFieldError(nameof(_observed.Header.TestDate), "测试日期格式无效，请重新输入或选择日期。");
        }
        TestDatePicker.GetBindingExpression(DatePicker.SelectedDateProperty)?.UpdateSource();
        if (_observed is not null) _observed.HasInputErrors = !valid || _errors.Count > 0;
        return valid && _errors.Count == 0;
    }

    /// <summary>原生日期解析失败会恢复旧日期；清除选择并提示必填，避免点击保存时误提交旧日期。</summary>
    private void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs args)
    {
        args.ThrowException = false;
        TestDatePicker.SetCurrentValue(DatePicker.SelectedDateProperty, null);
        _observed?.Header.SetFieldError(nameof(_observed.Header.TestDate), "测试日期格式无效，请重新输入或选择日期。");
    }

    /// <summary>保留表格原生 F2、Esc、Tab 行为；只阻止无字段权限的编辑开始。</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs args)
    {
        if (_observed?.CanEdit != true) args.Cancel = true;
    }

    /// <summary>数字和日期格式错误归视图绑定层，错误集合只负责阻断旧值提交。</summary>
    private void OnInputError(object? sender, ValidationErrorEventArgs args)
    {
        // 模型业务错误由保存时重新验证；只累计转换错误，避免跨行条件纠正后仍被旧提示阻断。
        if (args.Error.RuleInError is NotifyDataErrorValidationRule) return;
        if (args.Action == ValidationErrorEventAction.Added) _errors.Add(args.Error);
        else _errors.Remove(args.Error);
        if (_observed is not null) _observed.HasInputErrors = _errors.Count > 0;
    }

    /// <summary>验证失败时把焦点交给可编辑字段或首个错误行，延迟工作重新核对视图存活。</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(FlowDetailViewModel.ValidationSummary) || string.IsNullOrWhiteSpace(_observed?.ValidationSummary)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded || !ReferenceEquals(sender, _observed)) return;
            if (_observed?.ErrorField is "Header" or "Data.MaterialId" or "Data.TestDate")
            {
                if (_observed.Header.MaterialId == Guid.Empty) MaterialEditor.FocusEditor();
                else TestDatePicker.Focus();
            }
            else if (_observed?.SelectedLine is { } line)
            {
                FlowGrid.ScrollIntoView(line);
                FlowGrid.Focus();
            }
        }));
    }
}

