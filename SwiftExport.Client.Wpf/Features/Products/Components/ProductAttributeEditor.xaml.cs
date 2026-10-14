using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Resources.Controls.Filter;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>产品详情共用的标签编辑区域；接收页面私有工作区，仅桥接多选弹出层生命周期，不访问缓存或保存 API。</summary>
public partial class ProductAttributeEditor : UserControl
{
    private readonly Dictionary<MultiSelectLookupButton, Popup> _popups = [];
    private readonly Dictionary<Popup, ProductAttributeSelectionModel> _openedWorkspaces = [];

    /// <summary>标识页面独占的选择工作区，候选解析、加载、回滚和释放由页面负责。</summary>
    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace), typeof(ProductAttributeSelectionModel), typeof(ProductAttributeEditor),
        new PropertyMetadata(null, OnWorkspaceChanged));

    /// <summary>标识页面权限与 Busy 状态决定的编辑许可，默认只读。</summary>
    public static readonly DependencyProperty CanEditProperty = DependencyProperty.Register(
        nameof(CanEdit), typeof(bool), typeof(ProductAttributeEditor), new PropertyMetadata(false, OnCanEditChanged));

    /// <summary>标识由宿主执行的标签刷新命令，控件不发起网络请求。</summary>
    public static readonly DependencyProperty RefreshCommandProperty = DependencyProperty.Register(
        nameof(RefreshCommand), typeof(ICommand), typeof(ProductAttributeEditor));

    /// <summary>初始化标签视图；宿主 DataContext 保持原样，通过依赖属性显式传入状态。</summary>
    public ProductAttributeEditor()
    {
        InitializeComponent();
        IsEnabledChanged += OnIsEnabledChanged;
    }

    /// <summary>获取或设置当前详情的私有工作区，切换时取消旧弹出层草稿。</summary>
    public ProductAttributeSelectionModel? Workspace
    {
        get => (ProductAttributeSelectionModel?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    /// <summary>获取或设置编辑许可；撤回许可时关闭多选弹出层，不提交其中的临时选择。</summary>
    public bool CanEdit
    {
        get => (bool)GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    /// <summary>获取或设置刷新命令，命令可执行性与反馈由页面管理。</summary>
    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    private static void OnWorkspaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ProductAttributeEditor)sender).ClosePopups();

    private static void OnCanEditChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue) ((ProductAttributeEditor)sender).ClosePopups();
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue) ClosePopups();
    }

    /// <summary>跟踪各组已加载的多选控件，卸载时对称解除 Popup 事件。</summary>
    private void OnMultiSelectLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MultiSelectLookupButton control || _popups.ContainsKey(control)
            || control.FindName("PART_Popup") is not Popup popup) return;
        _popups.Add(control, popup);
        popup.Opened += OnPopupOpened;
        popup.Closed += OnPopupClosed;
    }

    private void OnMultiSelectUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MultiSelectLookupButton control || !_popups.Remove(control, out var popup)) return;
        popup.Opened -= OnPopupOpened;
        popup.Closed -= OnPopupClosed;
        popup.SetCurrentValue(Popup.IsOpenProperty, false);
        EndPopup(popup);
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || Workspace is not { } workspace
            || _openedWorkspaces.ContainsKey(popup)) return;
        _openedWorkspaces.Add(popup, workspace);
        workspace.BeginPopupEdit();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup popup) EndPopup(popup);
    }

    /// <summary>按打开时的工作区释放状态，防止复用控件后把旧弹出层关闭记到新对象。</summary>
    private void EndPopup(Popup popup)
    {
        if (_openedWorkspaces.Remove(popup, out var workspace)) workspace.EndPopupEdit();
    }

    /// <summary>失去编辑许可或切换对象时仅取消临时勾选；用快照避免关闭引发重建时修改枚举。</summary>
    private void ClosePopups()
    {
        foreach (var popup in _popups.Values.ToArray()) popup.SetCurrentValue(Popup.IsOpenProperty, false);
    }
}
