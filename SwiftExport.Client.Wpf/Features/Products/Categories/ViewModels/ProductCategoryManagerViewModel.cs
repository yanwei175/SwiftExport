using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace SwiftExportClient.Features.Products.Categories.ViewModels;

public sealed class ProductCategoryManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDropTarget
{
    private readonly IProductCategoryApi _categoryApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly List<ProductCategoryTreeItemModel> _deletedNodes = [];
    private ProductCategoryTreeItemModel? _currentCategory;
    private string _statusMessage = "尚未加载产品分类。";
    private bool _isInitialized;

    public ProductCategoryManagerViewModel(
        IProductCategoryApi categoryApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache)
    {
        _categoryApi = categoryApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;

        Title = "产品分类管理";
        AddRootCommand = new RelayCommand(AddRoot, CanWriteCommand);
        AddSiblingCommand = new RelayCommand<ProductCategoryTreeItemModel?>(AddSibling, CanWriteCommand);
        AddChildCommand = new RelayCommand<ProductCategoryTreeItemModel?>(AddChild, CanWriteCommand);
        DeleteCommand = new AsyncRelayCommand<ProductCategoryTreeItemModel?>(DeleteAsync, CanDelete);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunCommand);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);

        RegisterBusyAwareCommand(AddRootCommand);
        RegisterBusyAwareCommand(AddSiblingCommand);
        RegisterBusyAwareCommand(AddChildCommand);
        RegisterBusyAwareCommand(DeleteCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SaveCommand);
    }

    public ObservableCollection<ProductCategoryTreeItemModel> CategoryTree { get; } = [];

    public ProductCategoryTreeItemModel? CurrentCategory
    {
        get => _currentCategory;
        set
        {
            if (SetProperty(ref _currentCategory, value))
            {
                StatusMessage = value is null ? "请选择一个分类。" : string.Empty;
                AddSiblingCommand.NotifyCanExecuteChanged();
                AddChildCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasChanges => CategoryTree.Any(HasChangedNode) || _deletedNodes.Count > 0;

    public IRelayCommand AddRootCommand { get; }

    public IRelayCommand<ProductCategoryTreeItemModel?> AddSiblingCommand { get; }

    public IRelayCommand<ProductCategoryTreeItemModel?> AddChildCommand { get; }

    public IAsyncRelayCommand<ProductCategoryTreeItemModel?> DeleteCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadAsync();
    }

    private Task RefreshAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _categoryApi.GetAllAsync(),
                "加载产品分类失败");

            if (!result.Success)
            {
                StatusMessage = result.Message;
                ValidationSummary = result.Message;
                return;
            }

            BuildTree(result.Data ?? []);
            _deletedNodes.Clear();
            AcceptChanges();
            UpdateChangeState("产品分类已加载。");
        }, "正在加载产品分类...");
    }

    private void AddRoot()
    {
        var node = CreateNewNode(null, CategoryTree, "新建一级分类");
        CategoryTree.Add(node);
        SelectNode(node);
        UpdateChangeState("已新增一级分类。");
    }

    private void AddSibling(ProductCategoryTreeItemModel? node)
    {
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

    private void AddChild(ProductCategoryTreeItemModel? node)
    {
        if (node is null)
        {
            return;
        }

        var newNode = CreateNewNode(node.Uid, node.Children, "新建子分类");
        node.Children.Add(newNode);
        node.IsExpanded = true;
        SelectNode(newNode);
        UpdateChangeState("已新增子分类。");
    }

    private async Task DeleteAsync(ProductCategoryTreeItemModel? node)
    {
        if (node is null)
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

        CollectDeletedNodes(node);
        var parent = GetParentCollection(CategoryTree, node);
        parent?.Remove(node);
        if (ReferenceEquals(CurrentCategory, node))
        {
            CurrentCategory = CategoryTree.FirstOrDefault();
            if (CurrentCategory is not null)
            {
                CurrentCategory.IsSelected = true;
            }
        }

        UpdateChangeState("分类已标记删除，保存后生效。");
    }

    private async Task SaveAsync()
    {
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
                    .Select(node => new CreateItemRequest<ProductCategoryDto> { Data = node.ToDto() })
                    .ToList(),

                UpdateItems = allNodes
                    .Where(node => node.State == ProductCategoryNodeState.Modified)
                    .Select(node => new UpdateItemRequest<ProductCategoryDto> { Uid = node.Uid, Data = node.ToDto() })
                    .ToList(),
                DeleteItems = _deletedNodes
                    .Where(node => node.State != ProductCategoryNodeState.Added)
                    .Select(node => new DeleteItemRequest { Uid = node.Uid, IsSoftDelete = true })
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

            var affectedRows = result.Data?.SuccessCount ?? 0;
            _messageService.ShowSuccess("产品分类已保存", $"已提交 {affectedRows} 条变更。");
            await LoadAsync();
            var cacheResult = await _referenceDataCache.RefreshProductCategoriesAsync();
            if (!cacheResult.Success)
            {
                _messageService.ShowWarning(
                    "产品分类已保存，但缓存刷新失败",
                    cacheResult.Message);
            }
        }, "正在保存产品分类...");
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

    private void BuildTree(IReadOnlyList<ProductCategoryDto> dtos)
    {
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
        SelectNode(CategoryTree.FirstOrDefault());
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
        IsDirty = HasChanges;
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusMessage = statusMessage;
        }

        OnPropertyChanged(nameof(HasChanges));
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

    public void DragOver(IDropInfo dropInfo)
    {
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

    public void Drop(IDropInfo dropInfo)
    {
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
        return IsNotBusy;
    }

    private bool CanWriteCommand()
    {
        return IsNotBusy && CanWrite;
    }

    private bool CanWriteCommand(ProductCategoryTreeItemModel? node)
    {
        return IsNotBusy && CanWrite && node is not null;
    }

    private bool CanDelete(ProductCategoryTreeItemModel? node)
    {
        return IsNotBusy && CanWrite && node is not null;
    }

    private bool CanSave()
    {
        return IsNotBusy && CanWrite && HasChanges;
    }
}
