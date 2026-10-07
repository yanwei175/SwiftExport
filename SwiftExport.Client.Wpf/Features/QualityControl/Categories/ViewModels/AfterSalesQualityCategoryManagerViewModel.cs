using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.QualityControl.Categories.ViewModels;

/// <summary>独立售后品质分类维护页，拥有加载、编辑准入、批量提交和未保存关闭协议。</summary>
public sealed partial class AfterSalesQualityCategoryManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>维护页的稳定路由；重复打开激活同一页签。</summary>
    public const string RouteKey = "quality-control-after-sales-categories";
    private readonly IAfterSalesQualityCategoryApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _loadCancellation;
    private bool _initialized;
    private bool _disposed;

    /// <summary>注入单一分类 API 与现有客户端基础设施，不复制 Busy 或删除池。</summary>
    public AfterSalesQualityCategoryManagerViewModel(IAfterSalesQualityCategoryApi api, IApiCallRunner runner,
        ICurrentUserService user, IDialogService dialogs)
    {
        _api = api; _runner = runner; _user = user; _dialogs = dialogs;
        Title = "售后品质分类";
        Tree.Lines.ListStateChanged += OnLinesChanged;
        PropertyChanged += OnPageChanged;
        _user.CurrentUserChanged += OnCurrentUserChanged;
        SynchronizePermissions();
        RegisterBusyAwareCommand(AddSiblingCommand);
        RegisterBusyAwareCommand(AddChildCommand);
        RegisterBusyAwareCommand(DeleteCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(MoveUpCommand);
        RegisterBusyAwareCommand(MoveDownCommand);
        RegisterBusyAwareCommand(PromoteCommand);
        RegisterBusyAwareCommand(DemoteCommand);
    }

    /// <summary>树形视图与编辑事实的唯一所有者。</summary>
    public AfterSalesQualityCategoryTree Tree { get; } = new();
    /// <summary>仅编辑事实允许操作，查看权限不自动授予维护权限。</summary>
    public bool CanEdit => !_disposed && IsNotBusy && !NeedsReload && HasAccess && _user.HasPermission(PermissionKeys.AfterSalesMaintain);
    /// <summary>当前账号能否读取分类。</summary>
    public bool HasAccess => _user.HasPermission(PermissionKeys.QualityControlAfterSalesView);
    /// <inheritdoc/>
    public string DocumentTitle => Title;
    /// <inheritdoc/>
    public bool HasUnsavedChanges => IsDirty;
    /// <summary>视图在提交前验证输入转换，避免把旧排序值作为新输入提交。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    [ObservableProperty] public partial AfterSalesQualityCategoryNode? CurrentCategory { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "尚未加载分类。";
    [ObservableProperty] public partial bool NeedsReload { get; private set; }

    partial void OnCurrentCategoryChanged(AfterSalesQualityCategoryNode? value) => NotifyEditCommands();
    partial void OnNeedsReloadChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); NotifyEditCommands(); }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        IsReadOnly = !HasAccess || !_user.HasPermission(PermissionKeys.AfterSalesMaintain);
        if (!HasAccess) { StatusMessage = "当前账号没有品质管理查看权限。"; return; }
        await RefreshAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken) => !IsBusy
        && (!HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭售后品质分类", "分类有未保存修改，是否放弃？", "放弃", "继续编辑", cancellationToken));

    /// <summary>显式刷新检查未保存输入，失败保留原树。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        if (IsDirty && !await _dialogs.ConfirmAsync("刷新分类", "刷新会放弃未保存修改，是否继续？", "刷新")) return;
        if (!CanRefresh()) return;
        await RunBusyAsync(() => LoadCoreAsync(), "正在加载分类...");
    }

    /// <summary>取消旧读取并在回写前核对请求身份；保存后直接调用以免嵌套 Busy 跳过刷新。</summary>
    private async Task LoadCoreAsync()
    {
        _loadCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCancellation = cts;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetAllAsync(cts.Token), cts.Token, "加载分类失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cts)) return;
            if (!result.Success || result.Data is null)
            {
                StatusMessage = result.Error?.Message ?? result.Message;
                ValidationSummary = StatusMessage;
                return;
            }
            var selectedId = CurrentCategory?.Uid;
            Tree.Load(result.Data);
            Select(Tree.Lines.Items.FirstOrDefault(x => x.Uid == selectedId) ?? Tree.Roots.FirstOrDefault());
            NeedsReload = false;
            AcceptChanges();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            StatusMessage = Tree.Roots.Count == 0 ? "暂无分类，可新增一级分类。" : "分类已加载。";
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cts)) _loadCancellation = null;
            cts.Dispose();
        }
    }

    /// <summary>无选择时新建根节点，否则添加同级节点。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddSibling()
    {
        if (!CanEdit) return;
        Select(Tree.Add(CurrentCategory?.ParentId));
    }

    /// <summary>在当前分类下新增子级，父级和末级没有不同业务准入。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrent))]
    private void AddChild()
    {
        if (!CanChangeCurrent()) return;
        var parent = CurrentCategory!;
        Select(Tree.Add(parent.Uid));
        parent.IsExpanded = true;
    }

    /// <summary>确认后把当前子树标记删除，真正引用保护由后端提交时裁决。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrent))]
    private async Task DeleteAsync()
    {
        if (!CanChangeCurrent()) return;
        var node = CurrentCategory!;
        if (!await _dialogs.ConfirmAsync("删除分类", $"删除“{node.CategoryName}”及其子分类？保存后生效。", "删除")) return;
        if (!CanEdit || !Tree.Lines.Items.Contains(node)) return;
        Tree.Remove(node);
        Select(Tree.Roots.FirstOrDefault());
        StatusMessage = "已标记删除，保存后生效。";
    }

    /// <summary>提交编辑字段；成功后先锁住编辑并刷新，避免刷新失败时重复新增。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        if (CommitPendingEdits?.Invoke() == false) { ValidationSummary = "请先修正输入格式。"; return; }
        var validation = Tree.Validate();
        if (validation.Message is not null)
        {
            Select(validation.Node); ValidationSummary = validation.Message; return;
        }
        await RunWriteAsync(async () =>
        {
            var selectedKey = CurrentCategory?.Uid.ToString("N");
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(Tree.CreateRequest(), CancellationToken.None), "保存分类失败");
            if (_disposed) return;
            if (!result.Success || result.Data is null || !result.Data.AllSucceeded)
            {
                ValidationSummary = result.Error?.Message ?? result.Message;
                if (result.Error?.Code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.ServiceUnavailable || result.Success)
                {
                    // 批量接口没有命令回执重试协议，结果不确定时先读当前事实，禁止盲目重发新增。
                    NeedsReload = true;
                    StatusMessage = "保存结果尚未确定，本地输入已保留。请刷新核对服务端分类后再编辑。";
                    return;
                }
                StatusMessage = result.Error?.Code == ServiceErrorCodes.ConcurrencyConflict
                    ? "分类已被其他用户修改，本地输入已保留，请决定是否刷新。" : "保存失败，本地输入已保留。";
                return;
            }
            NeedsReload = true;
            // 服务端可能重分配新增身份；仅对齐选中身份，整棵树仍由随后权威读取替换。
            if (CurrentCategory is not null && result.Data.Items.FirstOrDefault(x => x.Success && x.ClientKey == selectedKey)?.Uid is { } selectedId)
                CurrentCategory.Uid = selectedId;
            AcceptChanges();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            StatusMessage = "分类已保存，正在读取最新版本。";
            await LoadCoreAsync();
            if (NeedsReload) StatusMessage = "分类已保存，但刷新失败。请刷新后继续编辑。";
        }, "正在保存分类...");
    }

    /// <summary>供视图拖放调用，同步复核准入和循环保护。</summary>
    public bool Move(AfterSalesQualityCategoryNode node, Guid? parentId, int index)
    {
        if (!CanEdit || !Tree.Move(node, parentId, index)) return false;
        Select(node);
        if (parentId is { } id) Tree.Lines.Items.Single(x => x.Uid == id).IsExpanded = true;
        StatusMessage = "层级或排序已调整，保存后生效。";
        return true;
    }

    /// <summary>同级上移提供键盘可用的排序操作。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrent))]
    private void MoveUp() { if (CanChangeCurrent()) Move(CurrentCategory!, CurrentCategory!.ParentId, Tree.Siblings(CurrentCategory.ParentId).IndexOf(CurrentCategory) - 1); }
    /// <summary>同级下移提供键盘可用的排序操作。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrent))]
    private void MoveDown() { if (CanChangeCurrent()) Move(CurrentCategory!, CurrentCategory!.ParentId, Tree.Siblings(CurrentCategory.ParentId).IndexOf(CurrentCategory) + 2); }
    /// <summary>提升一级，插入原父节点之后。</summary>
    [RelayCommand(CanExecute = nameof(CanPromote))]
    private void Promote()
    {
        if (!CanPromote()) return;
        var parent = Tree.Lines.Items.Single(x => x.Uid == CurrentCategory!.ParentId);
        Move(CurrentCategory!, parent.ParentId, Tree.Siblings(parent.ParentId).IndexOf(parent) + 1);
    }
    /// <summary>下移一级成为前一个同级节点的子节点。</summary>
    [RelayCommand(CanExecute = nameof(CanDemote))]
    private void Demote()
    {
        if (!CanDemote()) return;
        var siblings = Tree.Siblings(CurrentCategory!.ParentId);
        var parent = siblings[siblings.IndexOf(CurrentCategory) - 1];
        Move(CurrentCategory, parent.Uid, parent.Children.Count);
    }

    private bool CanRefresh() => !_disposed && HasAccess && IsNotBusy;
    private bool CanAdd() => CanEdit;
    private bool CanChangeCurrent() => CanEdit && CurrentCategory is not null && Tree.Lines.Items.Contains(CurrentCategory);
    private bool CanSave() => CanEdit && Tree.Lines.HasChanges;
    private bool CanPromote() => CanChangeCurrent() && CurrentCategory!.ParentId.HasValue;
    private bool CanDemote() => CanChangeCurrent() && Tree.Siblings(CurrentCategory!.ParentId).IndexOf(CurrentCategory) > 0;

    /// <summary>选择节点时同步树选择状态，避免旧节点继续处于选中态。</summary>
    private void Select(AfterSalesQualityCategoryNode? node)
    {
        if (CurrentCategory is not null) CurrentCategory.IsSelected = false;
        CurrentCategory = node;
        if (node is not null) node.IsSelected = true;
    }

    /// <summary>从通用编辑集合镜像页签脏状态并通知命令。</summary>
    private void OnLinesChanged(object? sender, ListStateChangedEventArgs<AfterSalesQualityCategoryNode> e)
    {
        IsDirty = Tree.Lines.HasChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        NotifyEditCommands();
    }
    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsBusy)) OnPropertyChanged(nameof(CanEdit));
    }
    /// <summary>会话权限变化同步基类写入保护和界面；撤销读取时取消当前查询。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        SynchronizePermissions();
        if (!HasAccess) _loadCancellation?.Cancel();
    }
    private void SynchronizePermissions()
    {
        IsReadOnly = !HasAccess || !_user.HasPermission(PermissionKeys.AfterSalesMaintain);
        OnPropertyChanged(nameof(HasAccess));
        OnPropertyChanged(nameof(CanEdit));
        NotifyEditCommands();
        RefreshCommand.NotifyCanExecuteChanged();
    }
    /// <summary>编辑状态变化时刷新全部关联操作的可执行性。</summary>
    private void NotifyEditCommands()
    {
        AddSiblingCommand.NotifyCanExecuteChanged(); AddChildCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged(); MoveDownCommand.NotifyCanExecuteChanged();
        PromoteCommand.NotifyCanExecuteChanged(); DemoteCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc/>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        ValidationSummary = "分类操作未完成，请刷新后重试；未保存输入已保留。";
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadCancellation?.Cancel();
        _user.CurrentUserChanged -= OnCurrentUserChanged;
        Tree.Lines.ListStateChanged -= OnLinesChanged;
        PropertyChanged -= OnPageChanged;
        CommitPendingEdits = null;
        Tree.Lines.Clear(); Tree.Roots.Clear();
    }
}
