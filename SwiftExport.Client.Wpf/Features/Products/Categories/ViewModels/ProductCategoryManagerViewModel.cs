using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using GongSolutions.Wpf.DragDrop;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace SwiftExportClient.Features.Products.Categories.ViewModels;

/// <summary>维护分类树，并协调分类资料与独立标签工作区的保存、选择及离开门禁。</summary>
public sealed partial class ProductCategoryManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDropTarget, IDocumentTabViewModel, IDisposable
{
    private readonly IProductCategoryApi _categoryApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly List<ProductCategoryTreeItemModel> _deletedNodes = [];
    private bool _isInitialized;
    private bool _disposed;
    private bool _isBuildingTree;
    private bool _isSelecting;
    private bool _isUpdatingHostState;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private IReadOnlyList<ProductCategoryDto> _baseline = [];

    /// <summary>注入分类 API、快速归类及标签定义子工作区，共同管理页面生命周期。</summary>
    public ProductCategoryManagerViewModel(
        IProductCategoryApi categoryApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache,
        ProductCategoryAssignmentViewModel assignment,
        ProductAttributeDefinitionViewModel attributes)
    {
        _categoryApi = categoryApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        Assignment = assignment;
        Attributes = attributes;
        Attributes.PropertyChanged += OnAttributeStateChanged;
        Assignment.PropertyChanged += OnAssignmentStateChanged;
        PropertyChanged += OnOwnStateChanged;

        Title = "产品分类管理";
        RegisterBusyAwareCommand(AddRootCommand);
        RegisterBusyAwareCommand(AddSiblingCommand);
        RegisterBusyAwareCommand(AddChildCommand);
        RegisterBusyAwareCommand(DeleteCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SaveCommand);
    }

    /// <summary>分类树私有编辑节点。</summary>
    public ObservableCollection<ProductCategoryTreeItemModel> CategoryTree { get; } = [];

    /// <summary>原有主型号快速归类工作区。</summary>
    public ProductCategoryAssignmentViewModel Assignment { get; }

    /// <summary>两张标签定义表的独立编辑及保存工作区。</summary>
    public ProductAttributeDefinitionViewModel Attributes { get; }
    /// <summary>宿主关闭或刷新前由视觉层提交定义表的未提交编辑，转换错误时阻止离开。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }

    [ObservableProperty] public partial ProductCategoryTreeItemModel? CurrentCategory { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "尚未加载产品分类。";
    [ObservableProperty] public partial int SelectedTabIndex { get; set; }

    /// <summary>分类树与删除池是否包含草稿。</summary>
    public bool HasChanges => CategoryTree.Any(HasChangedNode) || _deletedNodes.Count > 0;
    /// <inheritdoc/>
    public bool HasUnsavedChanges => HasChanges || Attributes.HasUnsavedChanges;
    /// <inheritdoc/>
    public string DocumentTitle => Title;
    /// <summary>宿主及子页共同冻结交互，阻止写入期间切换或穿透修改。</summary>
    public bool IsInteractionEnabled => !_disposed && !_isSelecting && IsNotBusy && !Attributes.IsBusy && !Assignment.IsBusy;
    /// <summary>分类字段编辑能力，包括共同 Busy 与页面写入状态。</summary>
    public bool CanEditCategories => IsInteractionEnabled && CanWrite;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await LoadAsync();
        _isInitialized = _baseline.Count > 0 || string.IsNullOrWhiteSpace(ValidationSummary);
    }

    /// <summary>树选择变化先处理旧标签草稿；取消或失败恢复旧节点及视觉选中。</summary>
    public async Task SelectCurrentCategoryAsync(ProductCategoryTreeItemModel? category)
    {
        if (_isBuildingTree || ReferenceEquals(CurrentCategory, category)) return;
        var previous = CurrentCategory;
        if (!IsInteractionEnabled || _isSelecting)
        { RestoreSelection(category, previous); return; }
        _isSelecting = true;
        UpdateHostState();
        try
        {
        if (!await Attributes.ResolveUnsavedChangesAsync())
        { RestoreSelection(category, previous); return; }
        CurrentCategory = category;
        await Assignment.ChangeTargetAsync(category, HasChanges, CanWrite && !Attributes.IsBusy);
        }
        catch (OperationCanceledException) when (_disposed) { return; }
        catch (Exception exception)
        {
            CurrentCategory = previous;
            RestoreSelection(category, previous);
            ReportInteractionFailure("切换分类", exception);
        }
        finally { _isSelecting = false; UpdateHostState(); }
        if (SelectedTabIndex == 1) await Attributes.EnsureLoadedAsync();
    }

    /// <summary>标签页按需加载最新定义，不在属性变更 hook 内发起 API。</summary>
    public Task ActivateSelectedTabAsync() => SelectedTabIndex == 1 ? Attributes.EnsureLoadedAsync() : Task.CompletedTask;

    /// <summary>观察视觉事件桥接的意外失败，向用户提供稳定反馈并仅记录必要的异常类别。</summary>
    public void ReportInteractionFailure(string action, Exception exception)
    {
        if (_disposed) return;
        System.Diagnostics.Trace.TraceError("产品分类页面交互失败：{0}；异常类型：{1}", action, exception.GetType().Name);
        StatusMessage = ValidationSummary = $"{action}失败，请稍后重试。";
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RefreshAsync()
    {
        if (!IsInteractionEnabled || !await ResolvePageChangesAsync()) return;
        await LoadAsync();
        if (SelectedTabIndex == 1) await Attributes.EnsureLoadedAsync();
    }

    private Task LoadAsync()
    {
        return RunBusyAsync(() => LoadCoreAsync(), "正在加载产品分类...");
    }

    /// <summary>
    /// 执行实际加载。保存命令已经处于 Busy 作用域中，必须直接调用此方法，
    /// 避免嵌套 RunBusyAsync 因 skipIfBusy=true 跳过服务端刷新。
    /// </summary>
    private async Task LoadCoreAsync(Guid? selectedUid = null)
    {
        _loadCts?.Cancel(); _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        var generation = ++_loadGeneration;
        var result = await _apiCallRunner.RunAsync(
            () => _categoryApi.GetAllAsync(),
            cts.Token,
            "加载产品分类失败");

        if (_disposed || cts.IsCancellationRequested || generation != _loadGeneration) return;

        if (!result.Success)
        {
            StatusMessage = result.Message;
            ValidationSummary = result.Message;
            return;
        }

        _baseline = (result.Data ?? []).Adapt<List<ProductCategoryDto>>();
        BuildTree(_baseline, selectedUid ?? CurrentCategory?.Uid);
        _deletedNodes.Clear();
        AcceptChanges();
        UpdateChangeState("产品分类已加载。");
    }

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRoot()
    {
        if (!CanWriteCommand() || Attributes.HasUnsavedChanges) return;
        var node = CreateNewNode(null, CategoryTree, "新建一级分类");
        CategoryTree.Add(node);
        SelectNode(node);
        UpdateChangeState("已新增一级分类。");
    }

    [RelayCommand(CanExecute = nameof(CanWriteNode))]
    private void AddSibling(ProductCategoryTreeItemModel? node)
    {
        if (!CanWriteCommand()) return;
        if (node is null)
        {
            AddRoot();
            return;
        }

        var siblings = GetParentCollection(CategoryTree, node) ?? CategoryTree;
        var newNode = CreateNewNode(node.ParentId, siblings, "新建分类");
        siblings.Add(newNode);
        SelectNode(newNode);
        UpdateChangeState("已新增同级分类。");
    }

    [RelayCommand(CanExecute = nameof(CanWriteNode))]
    private void AddChild(ProductCategoryTreeItemModel? node)
    {
        if (node is null || !CanWriteNode(node))
        {
            return;
        }

        var newNode = CreateNewNode(node.Uid, node.Children, "新建子分类");
        node.Children.Add(newNode);
        node.IsExpanded = true;
        SelectNode(newNode);
        UpdateChangeState("已新增子分类。");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(ProductCategoryTreeItemModel? node)
    {
        if (node is null || !CanDelete(node))
        {
            return;
        }

        var confirmed = node.State == ProductCategoryNodeState.Added
            || await _dialogService.ConfirmAsync(
                "删除产品分类",
                $"确定要删除“{node.CategoryName}”及其所有子分类吗？",
                "删除");

        if (!confirmed)
        {
            return;
        }

        if (!await Attributes.ResolveUnsavedChangesAsync()) return;
        CollectDeletedNodes(node);
        var parent = GetParentCollection(CategoryTree, node);
        parent?.Remove(node);
        if (CurrentCategory is { } current && IsDescendant(node, current))
        {
            CurrentCategory = CategoryTree.FirstOrDefault();
            if (CurrentCategory is not null)
            {
                CurrentCategory.IsSelected = true;
            }
        }

        UpdateChangeState("分类已标记删除，保存后生效。");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        await RunWriteAsync(async () =>
        {
            var allNodes = FlattenTree(CategoryTree).ToList();
            var validation = ValidateNodes(allNodes);
            if (!string.IsNullOrWhiteSpace(validation))
            {
                ValidationSummary = validation;
                StatusMessage = validation;
                _messageService.ShowWarning("产品分类未保存", validation);
                return;
            }

            var request = new ProductCategoryBatchSaveRequest
            {

                CreateItems = allNodes
                    .Where(node => node.State == ProductCategoryNodeState.Added)
                    .Select(node => new CreateItemRequest<ProductCategoryDto> { ClientKey = node.Uid.ToString("N"), Data = node.ToDto() })
                    .ToList(),

                UpdateItems = allNodes
                    .Where(node => node.State == ProductCategoryNodeState.Modified)
                    .Select(node => new UpdateItemRequest<ProductCategoryDto>
                    {
                        Uid = node.Uid,
                        Version = node.Version,
                        Data = node.ToDto()
                    })
                    .ToList(),
                DeleteItems = _deletedNodes
                    .Where(node => node.State != ProductCategoryNodeState.Added)
                    .Select(node => new DeleteItemRequest
                    {
                        Uid = node.Uid,
                        Version = node.Version,
                        IsSoftDelete = true
                    })
                    .ToList()
            };

            if (!request.HasChanges)
            {
                UpdateChangeState("没有需要保存的分类变更。");
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(
                "保存产品分类",
                $"将新增 {request.CreateItems.Count} 项、修改 {request.UpdateItems.Count} 项、删除 {request.DeleteItems.Count} 项。是否继续？",
                "保存");

            if (!confirmed)
            {
                StatusMessage = "已取消保存。";
                return;
            }

            var result = await _apiCallRunner.RunAsync(
                () => _categoryApi.BatchSaveAsync(request),
                "保存产品分类失败");

            if (!result.Success)
            {
                ValidationSummary = result.Message;
                StatusMessage = result.Message;
                _messageService.ShowError("保存产品分类失败", result.Message);
                return;
            }

            if (result.Data is null || result.Data.Items.Count != request.TotalCount || !result.Data.AllSucceeded
                || result.Data.Items.Any(item => item.Uid is null || item.Uid == Guid.Empty || item.NewVersion is null or <= 0)
                || request.CreateItems.Any(item => result.Data.Items.Count(receipt => receipt.ClientKey == item.ClientKey) != 1))
            {
                StatusMessage = ValidationSummary = "服务器已返回成功，但分类保存回执不完整，请刷新核对结果。";
                return;
            }

            var affectedRows = result.Data?.SuccessCount ?? 0;
            var selectedUid = CurrentCategory?.Uid;
            if (CurrentCategory?.State == ProductCategoryNodeState.Added)
                selectedUid = result.Data?.Items.FirstOrDefault(item => item.ClientKey == selectedUid?.ToString("N"))?.Uid;
            _messageService.ShowSuccess("产品分类已保存", $"已提交 {affectedRows} 条变更。");
            // 服务端明确提交后立即接纳真实身份和版本；随后读取失败也不会重复提交旧草稿。
            var persistedIds = request.CreateItems.ToDictionary(item => item.Data.Uid,
                item => result.Data!.Items.Single(receipt => receipt.ClientKey == item.ClientKey).Uid!.Value);
            var persistedDtos = allNodes.Select(node => node.ToDto()).ToList();
            foreach (var dto in persistedDtos)
            {
                var logicalUid = dto.Uid;
                if (persistedIds.TryGetValue(logicalUid, out var persistedUid)) dto.Uid = persistedUid;
                if (dto.ParentId is { } parentId && persistedIds.TryGetValue(parentId, out var realParentId)) dto.ParentId = realParentId;
                var receipt = result.Data!.Items.FirstOrDefault(item => item.Uid == dto.Uid);
                if (receipt?.NewVersion is { } version) dto.Version = version;
            }
            _baseline = persistedDtos;
            _deletedNodes.Clear();
            BuildTree(_baseline, selectedUid);
            UpdateChangeState();
            // 当前处于 RunWriteAsync 的 Busy 作用域中；调用核心加载方法，确保真正重新
            // 获取服务端生成的 Uid、Version 和审计字段。
            await LoadCoreAsync(selectedUid);
            if (!string.IsNullOrWhiteSpace(ValidationSummary))
                _messageService.ShowWarning("产品分类已保存，但重新加载失败", ValidationSummary);

            var cacheResult = await _referenceDataCache.RefreshProductCategoriesAsync();
            if (!cacheResult.Success)
            {
                _messageService.ShowWarning(
                    "产品分类已保存，但缓存刷新失败",
                    cacheResult.Message);
            }
        }, "正在保存产品分类...");
        if (SelectedTabIndex == 1 && !HasChanges) await Attributes.EnsureLoadedAsync();
    }

    private ProductCategoryTreeItemModel CreateNewNode(
        Guid? parentId,
        ObservableCollection<ProductCategoryTreeItemModel> siblings,
        string name)
    {
        var sortNo = siblings.Count == 0 ? 10 : siblings.Max(item => item.SortNo) + 10;
        var node = new ProductCategoryTreeItemModel
        {
            Uid = Guid.NewGuid(),
            ParentId = parentId,
            CategoryName = name,
            SortNo = sortNo,
            State = ProductCategoryNodeState.Added,
            IsExpanded = true
        };

        SubscribeNode(node);
        return node;
    }

    /// <summary>重建分类树前解除旧节点订阅，并按回执真实身份恢复当前分类。</summary>
    private void BuildTree(IReadOnlyList<ProductCategoryDto> dtos, Guid? selectedUid = null)
    {
        _isBuildingTree = true;
        foreach (var old in FlattenTree(CategoryTree)) UnsubscribeNode(old);
        CategoryTree.Clear();
        var nodes = dtos
            .OrderBy(dto => dto.SortNo)
            .ThenBy(dto => dto.CategoryName)
            .Select(ProductCategoryTreeItemModel.FromDto)
            .ToList();
        var map = nodes.ToDictionary(node => node.Uid);

        foreach (var node in nodes)
        {
            SubscribeNode(node);
            if (node.ParentId is null || node.ParentId == Guid.Empty || !map.TryGetValue(node.ParentId.Value, out var parent))
            {
                CategoryTree.Add(node);
            }
            else
            {
                parent.Children.Add(node);
            }
        }

        ExpandAll(CategoryTree);
        SelectNode(nodes.FirstOrDefault(node => node.Uid == selectedUid) ?? CategoryTree.FirstOrDefault());
        _isBuildingTree = false;
    }

    private void SelectNode(ProductCategoryTreeItemModel? node)
    {
        if (node is null)
        {
            CurrentCategory = null;
            return;
        }

        node.IsSelected = true;
        CurrentCategory = node;
    }

    private void SubscribeNode(ProductCategoryTreeItemModel node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        node.PropertyChanged += OnNodePropertyChanged;
        node.Children.CollectionChanged -= OnChildrenCollectionChanged;
        node.Children.CollectionChanged += OnChildrenCollectionChanged;

        foreach (var child in node.Children)
        {
            SubscribeNode(child);
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductCategoryTreeItemModel.State)
            or nameof(ProductCategoryTreeItemModel.CategoryName)
            or nameof(ProductCategoryTreeItemModel.FakeName)
            or nameof(ProductCategoryTreeItemModel.CategoryCode)
            or nameof(ProductCategoryTreeItemModel.SortNo)
            or nameof(ProductCategoryTreeItemModel.ParentId))
        {
            UpdateChangeState();
        }
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ProductCategoryTreeItemModel node in e.NewItems)
            {
                SubscribeNode(node);
            }
        }

        UpdateChangeState();
    }

    private void UpdateChangeState(string? statusMessage = null)
    {
        IsDirty = HasUnsavedChanges;
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusMessage = statusMessage;
        }
        OnPropertyChanged(nameof(HasChanges));
        UpdateHostState();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string? ValidateNodes(IReadOnlyList<ProductCategoryTreeItemModel> nodes)
    {
        var invalid = nodes.FirstOrDefault(node => string.IsNullOrWhiteSpace(node.CategoryName));
        return invalid is null ? null : "分类名称不能为空。";
    }

    private static bool IsDescendant(
        ProductCategoryTreeItemModel potentialParent,
        ProductCategoryTreeItemModel potentialChild)
    {
        if (ReferenceEquals(potentialParent, potentialChild))
        {
            return true;
        }

        return potentialParent.Children.Any(child => IsDescendant(child, potentialChild));
    }

    private static RelativeInsertPosition GetDynamicPosition(IDropInfo dropInfo)
    {
        if (dropInfo.VisualTargetItem is FrameworkElement itemContainer
            && dropInfo.VisualTarget is UIElement treeView)
        {
            var localPoint = treeView.TranslatePoint(dropInfo.DropPosition, itemContainer);
            var mouseY = localPoint.Y;
            var actualHeight = itemContainer.ActualHeight;

            if (mouseY >= actualHeight * 0.25 && mouseY <= actualHeight * 0.75)
            {
                return RelativeInsertPosition.TargetItemCenter;
            }

            return mouseY < actualHeight * 0.25
                ? RelativeInsertPosition.BeforeTargetItem
                : RelativeInsertPosition.AfterTargetItem;
        }

        return dropInfo.InsertPosition;
    }

    /// <summary>共同写入门禁和标签草稿检查通过后，才允许分类层次及排序拖动。</summary>
    public void DragOver(IDropInfo dropInfo)
    {
        if (!CanWriteCommand() || Attributes.HasUnsavedChanges) { dropInfo.Effects = DragDropEffects.None; return; }
        var sourceItem = dropInfo.Data as ProductCategoryTreeItemModel;
        var targetItem = dropInfo.TargetItem as ProductCategoryTreeItemModel;

        if (sourceItem is null)
        {
            return;
        }

        if (targetItem is null)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        if (ReferenceEquals(sourceItem, targetItem) || IsDescendant(sourceItem, targetItem))
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        dropInfo.Effects = DragDropEffects.Move;
        dropInfo.DropTargetAdorner = GetDynamicPosition(dropInfo) == RelativeInsertPosition.TargetItemCenter
            ? DropTargetAdorners.Highlight
            : DropTargetAdorners.Insert;
    }

    /// <summary>仅调整本地分类树与排序草稿，持久化仍由保存分类命令完成。</summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (!CanWriteCommand() || Attributes.HasUnsavedChanges) return;
        var sourceItem = dropInfo.Data as ProductCategoryTreeItemModel;
        var targetItem = dropInfo.TargetItem as ProductCategoryTreeItemModel;

        if (sourceItem is null || ReferenceEquals(sourceItem, targetItem))
        {
            return;
        }

        var oldParentCollection = GetParentCollection(CategoryTree, sourceItem) ?? CategoryTree;
        var oldIndex = oldParentCollection.IndexOf(sourceItem);
        oldParentCollection.Remove(sourceItem);
        ShuffleSortNo(oldParentCollection);

        if (targetItem is null)
        {
            sourceItem.ParentId = null;
            CategoryTree.Add(sourceItem);
            ShuffleSortNo(CategoryTree);
        }
        else
        {
            var dynamicPosition = GetDynamicPosition(dropInfo);
            if (dynamicPosition == RelativeInsertPosition.TargetItemCenter)
            {
                sourceItem.ParentId = targetItem.Uid;
                targetItem.Children.Add(sourceItem);
                targetItem.IsExpanded = true;
                ShuffleSortNo(targetItem.Children);
            }
            else
            {
                sourceItem.ParentId = targetItem.ParentId;
                var targetCollection = GetParentCollection(CategoryTree, targetItem) ?? CategoryTree;
                var insertIndex = dropInfo.InsertIndex;

                if (oldParentCollection == targetCollection && oldIndex >= 0 && oldIndex < insertIndex)
                {
                    insertIndex--;
                }

                insertIndex = Math.Clamp(insertIndex, 0, targetCollection.Count);
                targetCollection.Insert(insertIndex, sourceItem);
                ShuffleSortNo(targetCollection);
            }
        }

        MarkModified(sourceItem);
        SelectNode(sourceItem);
        UpdateChangeState("分类排序已调整，保存后生效。");
    }

    private static void ShuffleSortNo(ObservableCollection<ProductCategoryTreeItemModel> collection)
    {
        for (var index = 0; index < collection.Count; index++)
        {
            collection[index].SortNo = (index + 1) * 10;
        }
    }

    private static void MarkModified(ProductCategoryTreeItemModel node)
    {
        if (node.State == ProductCategoryNodeState.Unchanged)
        {
            node.State = ProductCategoryNodeState.Modified;
        }
    }

    public void DropHint(IDropHintInfo dropHintInfo)
    {
    }

    public void DragEnter(IDropInfo dropInfo)
    {
    }

    public void DragLeave(IDropInfo dropInfo)
    {
    }

    private static bool HasChangedNode(ProductCategoryTreeItemModel node)
    {
        return node.State is ProductCategoryNodeState.Added or ProductCategoryNodeState.Modified
            || node.Children.Any(HasChangedNode);
    }

    private static ObservableCollection<ProductCategoryTreeItemModel>? GetParentCollection(
        ObservableCollection<ProductCategoryTreeItemModel> currentLevel,
        ProductCategoryTreeItemModel target)
    {
        if (currentLevel.Contains(target))
        {
            return currentLevel;
        }

        foreach (var node in currentLevel)
        {
            var found = GetParentCollection(node.Children, target);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<ProductCategoryTreeItemModel> FlattenTree(IEnumerable<ProductCategoryTreeItemModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenTree(node.Children))
            {
                yield return child;
            }
        }
    }

    private void CollectDeletedNodes(ProductCategoryTreeItemModel node)
    {
        _deletedNodes.Add(node);
        foreach (var child in node.Children)
        {
            CollectDeletedNodes(child);
        }
    }

    private static void ExpandAll(IEnumerable<ProductCategoryTreeItemModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = true;
            ExpandAll(node.Children);
        }
    }

    private bool CanRunCommand()
    {
        return IsInteractionEnabled;
    }

    private bool CanWriteCommand()
    {
        return CanEditCategories && !Attributes.HasUnsavedChanges;
    }

    private bool CanWriteNode(ProductCategoryTreeItemModel? node)
    {
        return CanEditCategories && !Attributes.HasUnsavedChanges && node is not null;
    }

    private bool CanDelete(ProductCategoryTreeItemModel? node)
    {
        return CanEditCategories && node is not null;
    }

    private bool CanSave()
    {
        return CanEditCategories && HasChanges;
    }

    partial void OnCurrentCategoryChanged(ProductCategoryTreeItemModel? value)
    {
        StatusMessage = value is null ? "请选择一个分类。" : string.Empty;
        UpdateHostState();
    }

    /// <summary>共同 Busy 变化只更新子页门禁，不在通知回调里发起保存或查询。</summary>
    private void OnOwnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) UpdateHostState();
    }

    private void OnAssignmentStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IsBusy)) UpdateHostState();
    }

    private void OnAttributeStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsBusy) or nameof(Attributes.HasUnsavedChanges))
        {
            IsDirty = HasUnsavedChanges;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            UpdateHostState();
        }
    }

    /// <summary>宿主向两个子工作区传递窄状态，统一冻结分类树、表单和定义表。</summary>
    private void UpdateHostState()
    {
        if (_isUpdatingHostState) return;
        _isUpdatingHostState = true;
        try
        {
        Attributes.SetHostState(CurrentCategory?.Uid ?? Guid.Empty, CurrentCategory?.CategoryName ?? string.Empty,
            CurrentCategory is { State: not ProductCategoryNodeState.Added }, HasChanges, CanWrite, IsBusy || Assignment.IsBusy);
        Assignment.SetHostState(CurrentCategory, HasChanges, CanWrite && IsNotBusy && !Attributes.IsBusy);
        OnPropertyChanged(nameof(IsInteractionEnabled)); OnPropertyChanged(nameof(CanEditCategories));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        AddRootCommand.NotifyCanExecuteChanged(); AddSiblingCommand.NotifyCanExecuteChanged(); AddChildCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        }
        finally { _isUpdatingHostState = false; }
    }

    /// <summary>用户取消树切换时恢复两个节点的选中状态，视觉层沿现有双向绑定同步。</summary>
    private static void RestoreSelection(ProductCategoryTreeItemModel? attempted, ProductCategoryTreeItemModel? previous)
    {
        if (attempted is not null && !ReferenceEquals(attempted, previous)) attempted.IsSelected = false;
        if (previous is not null) previous.IsSelected = true;
    }

    /// <summary>刷新或关闭前分别处理定义、分类草稿，取消和保存失败都保留页面。</summary>
    private async Task<bool> ResolvePageChangesAsync(CancellationToken cancellationToken = default)
    {
        if (CommitPendingEdits?.Invoke() == false) return false;
        if (!IsInteractionEnabled || !await Attributes.ResolveUnsavedChangesAsync(cancellationToken)) return false;
        if (!HasChanges) return true;
        var decision = await _dialogService.ShowAsync("分类结构有未保存修改。",
            new DialogOptions("分类未保存") { PrimaryButtonText = "保存", SecondaryButtonText = "放弃", CloseButtonText = "取消" },
            cancellationToken: cancellationToken);
        if (_disposed) return false;
        if (decision == DialogResult.Primary)
        {
            await SaveAsync();
            return !HasChanges;
        }
        if (decision != DialogResult.Secondary) return false;
        var selectedUid = CurrentCategory?.Uid;
        _deletedNodes.Clear(); BuildTree(_baseline, selectedUid); UpdateChangeState("分类编辑已放弃。");
        return true;
    }

    /// <inheritdoc/>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => ResolvePageChangesAsync(cancellationToken);

    private void UnsubscribeNode(ProductCategoryTreeItemModel node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        node.Children.CollectionChanged -= OnChildrenCollectionChanged;
    }

    /// <summary>取消分类读取、释放标签子页并解除树和状态事件，旧请求不得重新写回页面。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _loadGeneration++; _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;
        foreach (var node in FlattenTree(CategoryTree).Concat(_deletedNodes).Distinct()) UnsubscribeNode(node);
        PropertyChanged -= OnOwnStateChanged; Attributes.PropertyChanged -= OnAttributeStateChanged;
        Assignment.PropertyChanged -= OnAssignmentStateChanged; Attributes.Dispose();
        CommitPendingEdits = null; Attributes.CommitPendingEdits = null;
        Assignment.CloseAssignmentPanelCommand.Execute(null);
    }
}
