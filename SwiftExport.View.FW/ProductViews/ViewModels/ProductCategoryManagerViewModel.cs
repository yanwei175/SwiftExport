using GalaSoft.MvvmLight.Command;
using GongSolutions.Wpf.DragDrop;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductCategoryManagerViewModel : ViewModelBaseEx, IDropTarget
    {
        protected readonly IProductCategoryApi _api;
        private readonly IDialogService _msg;
        public ObservableCollection<CategoryTreeModel> CategoryTree { get; set; } = new ObservableCollection<CategoryTreeModel>();

        private CategoryTreeModel _currentCategory;
        public CategoryTreeModel CurrentCategory
        {
            get => _currentCategory;
            set
            {
                SetProperty(ref _currentCategory, value);
                ClearStatusMessage();
            }
        }

        private string _statusMessage = "状态显示:";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private void ClearStatusMessage() => StatusMessage = string.Empty;



        // 命令
        public RelayCommand<CategoryTreeModel> AddSiblingCommand { get; private set; }
        public RelayCommand<CategoryTreeModel> AddChildCommand { get; private set; }
        public RelayCommand<CategoryTreeModel> DeleteCommand { get; private set; }
        public ICommand RefreshCommand => new RelayCommand(async () => await LoadDataAsync());

        // ★ API 保存命令
        public RelayCommand SaveCommand { get; private set; }

        private int _tempIdCounter = -1;

        // 垃圾桶，用来装被删除的真实节点ID
        private readonly List<CategoryTreeModel> _deletedNodes = new List<CategoryTreeModel>();

        public ProductCategoryManagerViewModel(IProductCategoryApi api, IDialogService dialogService)
        {
            _msg = dialogService;
            _api = api;
            AddSiblingCommand = new RelayCommand<CategoryTreeModel>(AddSibling);
            AddChildCommand = new RelayCommand<CategoryTreeModel>(AddChild);
            DeleteCommand = new RelayCommand<CategoryTreeModel>(DeleteCategory);

            // 初始化保存命令
            SaveCommand = new RelayCommand(async () => await SaveAsync());

            _ = LoadDataAsync();
        }

        // ==========================================
        // 🚀 后端 API 交互逻辑
        // ==========================================

        private async Task LoadDataAsync()
        {
            try
            {
                // 1. 调用真实接口获取数据
                var result = await _api.GetAllAsync();

                if (result.Success && result.Data != null)
                {
                    var flatList = result.Data
                                            .OrderBy(dto => dto.SortNo)
                                            .Select(dto => new CategoryTreeModel
                                            {
                                                Uid = dto.Uid,
                                                ParentId = dto.ParentId,
                                                CategoryName = dto.CategoryName,
                                                CategoryCode = dto.CategoryCode,
                                                FakeName = dto.FakeName,
                                                SortNo = dto.SortNo,
                                                State = ModelState.Unchanged
                                            }).ToList();

                    BuildTree(flatList);

                    _deletedNodes.Clear();
                    _tempIdCounter = -1;
                }
                else
                {
                    StatusMessage = result.Message ?? "获取数据失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"网络异常: {ex.Message}";
            }
        }

        private async Task SaveAsync()
        {
            // 1. 拍平树结构
            var allNodes = new List<CategoryTreeModel>();
            FlattenTree(CategoryTree, allNodes);

            // 2. 分拣出新增和修改的数据
            var addList = allNodes.Where(x => x.State == ModelState.Added)
                                  .Select(x => new ProductCategoryDto { ParentId = x.ParentId != Guid.Empty ? x.ParentId : null, CategoryName = x.CategoryName, FakeName = x.FakeName, CategoryCode = x.CategoryCode, SortNo = x.SortNo })
                                  .ToList();

            var updateList = allNodes.Where(x => x.State == ModelState.Modified)
                                     .Select(x => new ProductCategoryDto { Uid = x.Uid, ParentId = x.ParentId != Guid.Empty ? x.ParentId : null, CategoryName = x.CategoryName, FakeName = x.FakeName, CategoryCode = x.CategoryCode, SortNo = x.SortNo })
                                     .ToList();


            if (!addList.Any() && !updateList.Any() && !_deletedNodes.Any())
            {
                StatusMessage = "没有检测到任何更改，无需保存。";
                return;
            }



            // 3. 组装标准批量请求
            var deleteIdList = _deletedNodes.Where(x => x.Uid != Guid.Empty).Select(x => x.Uid).ToList();

            var batchReq = new BatchRequestDto<ProductCategoryDto>
            {
                AddList = addList,
                UpdateList = updateList,
                DeleteIdList = deleteIdList
            };

            //把AddList和UpdateList以及DeleteIdList转成带换行的字符串,弹窗给用户确认是否要修改或新增
            string addStr = addList.Any() ? "新增:\n" + string.Join("\n", addList.Select(x => $"[新增] {x.CategoryName} (ParentId: {x.ParentId}, SortNo: {x.SortNo})")) : "";
            addStr += updateList.Any() ? "\n修改:\n" + string.Join("\n", updateList.Select(x => $"[修改] {x.CategoryName} (Uid: {x.Uid}, ParentId: {x.ParentId}, SortNo: {x.SortNo})")) : "";
            addStr += _deletedNodes.Any() ? "\n删除:\n" + string.Join("\n", _deletedNodes.Select(x => $"[删除] {x.CategoryName} (Uid: {x.Uid}, ParentId: {x.ParentId}, SortNo: {x.SortNo})")) : "";

            var ok = await _msg.ShowConfirmAsync("确认操作!");
            if (!ok)
            {
                StatusMessage = "用户已取消保存。";
                return;
            }

            try
            {
                // 4. 发送保存请求
                var result = await _api.BatchSaveAsync(batchReq);
                if (result.Success)
                {
                    StatusMessage = "保存成功！";
                    // 5. 重新拉取最新数据（刷新真实ID）
                    await LoadDataAsync();
                }
                else
                {
                    StatusMessage = $"保存失败：{result.Message}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存出错：{ex.Message}";
            }
        }

        // ==========================================
        // 🚀 右键菜单逻辑
        // ==========================================
        private void AddSibling(CategoryTreeModel node)
        {
            // 1. 确定要把新节点塞进哪个集合里，以及它的爹是谁
            ObservableCollection<CategoryTreeModel> targetCollection;
            Guid? targetParentId;

            if (node == null)
            {
                // 【场景 A：顶部全局按钮】没有传入参照节点，说明是要新建“顶级/一级分类”
                targetCollection = CategoryTree; // 目标集合就是最外层的大树
                targetParentId = null;           // 顶级分类没有爹
            }
            else
            {
                // 【场景 B：右键菜单】传入了参照节点，说明是要新建“同级分类”
                targetCollection = GetParentCollection(CategoryTree, node);
                targetParentId = node.ParentId;  // 跟参照节点认同一个爹

                // 防呆：如果莫名其妙找不到老家，直接中止
                if (targetCollection == null) return;
            }

            // 2. 自动计算排序号 (拿到目标集合里目前最大的 SortNo + 10，如果是空的就从 10 开始)
            var newSortNo = targetCollection.Any() ? targetCollection.Max(x => x.SortNo) + 10 : 10;

            // 3. 实例化新节点
            var newNode = new CategoryTreeModel
            {
                Uid = Guid.Empty,
                ParentId = targetParentId,
                // 做个小细节：根据场景给个不同的默认名字
                CategoryName = node == null ? "新建一级分类" : "新建分类",
                SortNo = newSortNo,
                State = ModelState.Added
            };

            // 4. 塞进对应的集合，并触发 UI 选中
            targetCollection.Add(newNode);
            CurrentCategory = newNode;
            newNode.IsSelected = true;
        }

        private void AddChild(CategoryTreeModel node)
        {
            if (node == null) return;
            var newSortNo = node.Children.Any() ? node.Children.Max(x => x.SortNo) + 10 : 10;
            var newNode = new CategoryTreeModel { Uid = Guid.Empty, ParentId = node.Uid, CategoryName = "新建子分类", SortNo = newSortNo, State = ModelState.Added };
            node.Children.Add(newNode);
            node.IsExpanded = true;
            CurrentCategory = newNode;
            newNode.IsSelected = true;
        }

        private async void DeleteCategory(CategoryTreeModel node)
        {
            if (node == null) return;
            if (node.Uid != Guid.Empty)
            {
                var ok = await _msg.ShowConfirmAsync($"确定要删除【{node.CategoryName}】及所有子分类吗？", "警告");
                if (!ok) { return; }
            }

            CollectDeletedIds(node);
            var parentCollection = GetParentCollection(CategoryTree, node);
            parentCollection?.Remove(node);
            if (CurrentCategory == node) CurrentCategory = null;
        }

        // ==========================================
        // 🚀 完整恢复的拖拽洗牌逻辑 (绝对不省略！)
        // ==========================================
        // ★ 防呆：防止把老子拖进儿子里
        // ==========================================

        private bool IsDescendant(CategoryTreeModel potentialParent, CategoryTreeModel potentialChild)
        {
            if (potentialParent == null || potentialChild == null) return false;
            if (potentialParent == potentialChild) return true;
            foreach (var child in potentialParent.Children)
            {
                if (IsDescendant(child, potentialChild)) return true;
            }
            return false;
        }

        // ★ 核心魔法引擎：利用 WPF 底层矩阵转换，获取极其精确的局部坐标比例！
        private RelativeInsertPosition GetDynamicPosition(IDropInfo dropInfo)
        {
            // 确保我们拿到了当前的节点容器 (TreeViewItem) 和 整棵树 (TreeView)
            if (dropInfo.VisualTargetItem is FrameworkElement itemContainer && dropInfo.VisualTarget is UIElement treeView)
            {
                // 1. 神库只提供相对于整棵树的全局坐标 (DropPosition)
                // 2. 核心操作：用 WPF 原生的 TranslatePoint 把全局坐标转换为当前节点的局部坐标！
                Point localPoint = treeView.TranslatePoint(dropInfo.DropPosition, itemContainer);
                double mouseY = localPoint.Y;
                double actualHeight = itemContainer.ActualHeight; // 拿到节点在屏幕上渲染的真实高度

                // 中间的 50% 区域全部判定为“变儿子”
                if (mouseY >= actualHeight * 0.25 && mouseY <= actualHeight * 0.75)
                {
                    return RelativeInsertPosition.TargetItemCenter;
                }
                // 上下 25% 的边缘地带判定为“排序兄弟”
                return mouseY < actualHeight * 0.25 ? RelativeInsertPosition.BeforeTargetItem : RelativeInsertPosition.AfterTargetItem;
            }
            return dropInfo.InsertPosition; // 兜底
        }

        public void DragOver(IDropInfo dropInfo)
        {
            var sourceItem = dropInfo.Data as CategoryTreeModel;
            var targetItem = dropInfo.TargetItem as CategoryTreeModel;

            if (sourceItem != null)
            {
                if (targetItem != null)
                {
                    // 防呆：不能拖给自己，不能拖给自己的子孙
                    if (sourceItem == targetItem || IsDescendant(sourceItem, targetItem))
                    {
                        dropInfo.Effects = DragDropEffects.None;
                        return;
                    }

                    dropInfo.Effects = DragDropEffects.Move;

                    // ★ 利用动态比例引擎决定画什么特效
                    var dynamicPos = GetDynamicPosition(dropInfo);
                    if (dynamicPos == RelativeInsertPosition.TargetItemCenter)
                    {
                        dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight; // 中间画蓝框
                    }
                    else
                    {
                        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert; // 边缘画蓝线
                    }
                }
                else
                {
                    dropInfo.Effects = DragDropEffects.Move;
                    dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                }
            }
        }

        public void Drop(IDropInfo dropInfo)
        {
            var sourceItem = dropInfo.Data as CategoryTreeModel;
            var targetItem = dropInfo.TargetItem as CategoryTreeModel;

            if (sourceItem == null || sourceItem == targetItem) return;

            var oldParentCollection = GetParentCollection(CategoryTree, sourceItem) ?? CategoryTree;
            int oldIndex = oldParentCollection.IndexOf(sourceItem);
            oldParentCollection.Remove(sourceItem);
            ShuffleSortNo(oldParentCollection);

            if (targetItem == null)
            {
                // 拖到了底部空白处 -> 变成顶级兄弟节点
                sourceItem.ParentId = null;
                CategoryTree.Add(sourceItem);
                ShuffleSortNo(CategoryTree);
            }
            else
            {
                // ★ 拖放瞬间，利用同样的动态比例引擎决定命运！
                var dynamicPos = GetDynamicPosition(dropInfo);

                if (dynamicPos == RelativeInsertPosition.TargetItemCenter)
                {
                    // 【绝对命中变儿子逻辑】
                    sourceItem.ParentId = targetItem.Uid;
                    targetItem.Children.Add(sourceItem);
                    targetItem.IsExpanded = true;
                    ShuffleSortNo(targetItem.Children);
                }
                else
                {
                    // 【变兄弟逻辑】
                    sourceItem.ParentId = targetItem.ParentId;
                    var targetCollection = GetParentCollection(CategoryTree, targetItem) ?? CategoryTree;

                    int insertIndex = dropInfo.InsertIndex;

                    if (oldParentCollection == targetCollection && oldIndex >= 0 && oldIndex < insertIndex)
                    {
                        insertIndex--;
                    }

                    if (insertIndex < 0) insertIndex = 0;
                    if (insertIndex > targetCollection.Count) insertIndex = targetCollection.Count;

                    targetCollection.Insert(insertIndex, sourceItem);
                    ShuffleSortNo(targetCollection);
                }
            }

            sourceItem.State = ModelState.Modified;
            CurrentCategory = sourceItem;
            sourceItem.IsSelected = true;
            ClearStatusMessage();
        }

        private void ShuffleSortNo(ObservableCollection<CategoryTreeModel> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                collection[i].SortNo = (i + 1) * 10;
            }
        }

        // ==========================================
        // 🚀 辅助算法
        // ==========================================

        private ObservableCollection<CategoryTreeModel> GetParentCollection(ObservableCollection<CategoryTreeModel> currentLevel, CategoryTreeModel target)
        {
            if (currentLevel.Contains(target)) return currentLevel;
            foreach (var node in currentLevel)
            {
                var found = GetParentCollection(node.Children, target);
                if (found != null) return found;
            }
            return null;
        }

        private void FlattenTree(IEnumerable<CategoryTreeModel> nodes, List<CategoryTreeModel> result)
        {
            foreach (var node in nodes)
            {
                result.Add(node);
                FlattenTree(node.Children, result);
            }
        }

        private void CollectDeletedIds(CategoryTreeModel node)
        {
            if (node.Uid != Guid.Empty)
            {
                _deletedNodes.Add(node);
            }
            foreach (var child in node.Children)
            {
                CollectDeletedIds(child);
            }
        }

        private void BuildTree(List<CategoryTreeModel> flatList)
        {
            CategoryTree.Clear();
            var dict = flatList.ToDictionary(x => x.Uid);

            foreach (var item in flatList)
            {
                if (item.ParentId == null || item.ParentId == Guid.Empty || !dict.ContainsKey(item.ParentId.Value))
                {
                    CategoryTree.Add(item);
                }
                else
                {
                    dict[item.ParentId.Value].Children.Add(item);
                }
            }

            if (CategoryTree.Any())
            {
                CurrentCategory = CategoryTree.First();
                CurrentCategory.IsSelected = true;
                ExpandAll(CategoryTree);
            }
        }

        private void ExpandAll(IEnumerable<CategoryTreeModel> nodes) { foreach (var n in nodes) { n.IsExpanded = true; ExpandAll(n.Children); } }

        public void DropHint(IDropHintInfo dropHintInfo) { }
        public void DragEnter(IDropInfo dropInfo) { }
        public void DragLeave(IDropInfo dropInfo) { }
    }
}