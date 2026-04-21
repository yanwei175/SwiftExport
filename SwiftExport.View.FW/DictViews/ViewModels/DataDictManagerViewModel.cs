using GalaSoft.MvvmLight.Command;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Routing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    /// <summary>
    /// 中文注释：主窗体 ViewModel
    /// 左侧是字典类型只读导航树，右侧是对应的内容面板（字典明细列表）
    /// 编辑字典类型本身由另一个独立窗体负责
    /// </summary>
    public class DataDictManagerViewModel : ViewModelBaseEx
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ISysDictTypeApi _dictTypeApi;
        private readonly Dispatcher _dispatcher;

        // ============= 树形导航 =============

        /// <summary>
        /// 绑定给 TreeView.ItemsSource 的根节点集合
        /// 类型统一为 DictTypeNavNode，由后端分组树转换而来
        /// </summary>
        public ObservableCollection<DictTypeNavNode> TreeRoots { get; } = new ObservableCollection<DictTypeNavNode>();

        // ============= 搜索 =============

        private string _searchTypeText;
        public string SearchTypeText
        {
            get => _searchTypeText;
            set
            {
                if (SetProperty(ref _searchTypeText, value))
                    OnSearchTextChanged();
            }
        }

        // ============= 右侧内容面板 =============

        private object _currentViewModel;
        public object CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        // ============= 状态 =============

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ============= 命令 =============

        public RelayCommand ExpandAllCommand { get; }
        public RelayCommand CollapseAllCommand { get; }

        /// <summary>
        /// 打开字典类型管理独立窗体（增删改）
        /// </summary>
        public RelayCommand ManageTypesCommand { get; }

        // ============= 构造函数 =============

        public DataDictManagerViewModel(
            IServiceProvider serviceProvider,
            ISysDictTypeApi dictTypeApi,
            IDialogService dialogService)
        {
            _serviceProvider = serviceProvider;
            _dictTypeApi = dictTypeApi;
            _dispatcher = Dispatcher.CurrentDispatcher;

            ExpandAllCommand   = new RelayCommand(() => SetExpandAll(true));
            CollapseAllCommand = new RelayCommand(() => SetExpandAll(false));

            // 打开 DictTypeManagerView 独立窗体
            ManageTypesCommand = new RelayCommand(() =>
            {
                var win=new Window { Width=1080, Height=720, Title="字典类型管理" };
                var ctrl = _serviceProvider.GetRequiredService<DictTypeManagerView>();
                win.Content = ctrl;
                // 窗体关闭后刷新左侧树（字典类型可能已变更）
                win.Closed += (_, __) => _ = LoadDictTypesAsync();
                win.Show();
            });

            _ = LoadDictTypesAsync();
        }

        // ============= 数据加载 =============

        /// <summary>
        /// 从后端加载分组树，转换为统一的 DictTypeNavNode 结构绑定到 TreeView
        /// 后端已完成分组和多层级树形构建，前端只需遍历转换
        /// </summary>
        private async Task LoadDictTypesAsync()
        {
            StatusMessage = "正在加载字典类型树...";
            try
            {
                var rst = await _dictTypeApi.GetDictTypeGroupTreeAsync();

                if (!rst.Success || rst.Data == null)
                {
                    StatusMessage = "加载失败: " + rst.Message;
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    TreeRoots.Clear();

                    // 后端第一层是 DictTypeGroupTreeDto（分组）
                    // 其 Children 是 List<DictTypeTreeDto>（第二层及以上，支持多层级）
                    foreach (var groupDto in rst.Data)
                    {
                        var groupNode = new DictTypeNavNode
                        {
                            DisplayName = groupDto.GroupName,
                            DictCode    = null,   // 分组节点不对应字典明细
                            IsGroup     = true,
                            IsExpanded  = true    // 分组默认展开
                        };

                        // 递归转换分组下的字典类型树
                        foreach (var child in groupDto.Children)
                        {
                            var childNode = ConvertDictTypeTreeNode(child);
                            groupNode.Children.Add(childNode);
                        }

                        TreeRoots.Add(groupNode);
                    }

                    // 统计叶子节点数量（真实字典类型数量）
                    var totalCount = CountLeafNodes(TreeRoots);
                    StatusMessage = $"加载成功，共 {totalCount} 个字典类型";
                });
            }
            catch (Exception ex)
            {
                _dispatcher.Invoke(() => StatusMessage = "加载异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 递归将 DictTypeTreeDto 转换为 DictTypeNavNode
        /// DictTypeTreeDto 天然支持多层级（Children 是同类型）
        /// </summary>
        private DictTypeNavNode ConvertDictTypeTreeNode(DictTypeTreeDto dto)
        {
            var node = new DictTypeNavNode
            {
                DisplayName        = dto.DictName,
                DictCode           = dto.DictCode,
                TargetViewKey      = dto.TargetViewKey,   // 路由键，驱动右侧面板切换
                IsGroup            = false,
                IsExpanded         = false,
                NodeSelectedAction = OnNodeSelected
            };

            // 递归处理子节点
            foreach (var child in dto.Children)
            {
                node.Children.Add(ConvertDictTypeTreeNode(child));
            }

            return node;
        }

        // ============= 节点选中路由 =============
        #region 视图路由
        /// <summary>
        /// 树形节点被选中时，根据 TargetViewKey 路由到右侧对应的管理面板
        /// 
        /// 路由规则（TargetViewKey → ViewModel）：
        ///   "ProductColorView"   → ProductColorManagerViewModel  （产品颜色）
        ///   ——后续按需在此扩展——
        ///   未匹配 / 分组节点    → 不切换右侧面板
        /// </summary>
        private void OnNodeSelected(DictTypeNavNode node)
        {
            // 分组节点不加载内容
            if (node == null || node.IsGroup) return;

            try
            {
                object vm = ResolveViewModel(node.TargetViewKey, node.DictCode);
                CurrentViewModel = vm;
            }
            catch (Exception ex)
            {
                StatusMessage = $"路由失败 [{node.TargetViewKey}]: {ex.Message}";
                CurrentViewModel = null;
            }
        }

        /// <summary>
        /// 根据 TargetViewKey 从 DI 容器解析对应的 ViewModel（Transient，每次新实例）。
        /// 
        /// 路由映射统一维护在 <see cref="ViewRoutes.Routes"/>，
        /// 新增路由只需在那里加一行，此处无需改动。
        /// </summary>
        private object ResolveViewModel(string targetViewKey, string dictCode)
        {
            // 查路由表
            if (!ViewRoutes.TryResolve(targetViewKey, out var vmType))
            {
                StatusMessage = string.IsNullOrWhiteSpace(targetViewKey)
                    ? $"[{dictCode}] 暂未配置 TargetViewKey"
                    : $"未找到视图路由：{targetViewKey}，请在 ViewRoutes.cs 中注册";
                return null;
            }

            // 从 DI 容器创建实例（Transient，状态隔离）
            return _serviceProvider.GetRequiredService(vmType);
        }
        #endregion




        // ============= 搜索过滤 =============

        /// <summary>
        /// 搜索文本变化时，过滤节点显示
        /// 仅过滤字典类型节点（DictType），不过滤分组节点
        /// </summary>
        private void OnSearchTextChanged()
        {
            FilterNodes(TreeRoots, SearchTypeText);
        }

        /// <summary>
        /// 递归过滤节点
        /// 匹配规则：DictName 或 DictCode 包含关键字时显示；父节点有子节点匹配时也显示
        /// </summary>
        private bool FilterNodes(IEnumerable<DictTypeNavNode> nodes, string keyword)
        {
            bool anyMatch = false;

            foreach (var node in nodes)
            {
                bool selfMatch = node.IsGroup
                    ? false
                    : string.IsNullOrWhiteSpace(keyword) ||
                      (node.DisplayName != null && node.DisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                      (node.DictCode    != null && node.DictCode.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                bool childMatch = FilterNodes(node.Children, keyword);

                // 自身匹配或子节点有匹配，则展开并显示
                node.IsExpanded = childMatch;

                anyMatch = anyMatch || selfMatch || childMatch;
            }

            return anyMatch;
        }

        // ============= 展开/折叠 =============

        public void ExpandAllNodes()  => SetExpandAll(true);
        public void CollapseAllNodes() => SetExpandAll(false);

        private void SetExpandAll(bool isExpanded)
        {
            SetExpandNodes(TreeRoots, isExpanded);
            StatusMessage = isExpanded ? "已展开全部字典类型" : "已折叠全部字典类型";
        }

        private void SetExpandNodes(IEnumerable<DictTypeNavNode> nodes, bool isExpanded)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = isExpanded;
                SetExpandNodes(node.Children, isExpanded);
            }
        }

        // ============= 辅助 =============

        /// <summary>
        /// 统计叶子节点（真实字典类型）数量
        /// </summary>
        private int CountLeafNodes(IEnumerable<DictTypeNavNode> nodes)
        {
            int count = 0;
            foreach (var node in nodes)
            {
                if (!node.IsGroup)
                    count++;
                count += CountLeafNodes(node.Children);
            }
            return count;
        }
    }
}

