using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    /// <summary>
    /// 产品管理视图模型，处理产品主表及 BOM 子表的增删改查。
    /// </summary>
    public class ProductManagerViewModel : ViewModelBaseEx
    {
        #region 构造函数与核心联动机制 (Constructor & Core Events)

        public ProductManagerViewModel(IProductApi productApi, IBomApi bomApi, IProductCategoryApi productCategoryApi, IProductColorApi productColorApi, IDictCacheService dictCacheService, SwiftExport.Contracts.UiInterface.IDialogService dialogService, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _msg = dialogService;
            _productApi = productApi;
            _bomApi = bomApi;
            _productColorApi = productColorApi;
            _productCategoryApi = productCategoryApi;
            _dictCacheService = dictCacheService;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };// 800毫秒的输入防抖，用户停止输入800ms后才会触发搜索
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            AllProductModels = new ObservableCollection<ProductModel>();// 主表格的数据集合
            ItemsView = CollectionViewSource.GetDefaultView(AllProductModels);// 绑定到主表格的数据视图

            _bomBySku = new Dictionary<string, List<BomItemModel>>(StringComparer.OrdinalIgnoreCase);
            _allCategoryTreeTemplates = new List<CategoryTreeModel>();

            _currentAllBoms = new List<BomItemModel>();
            CurrentBomItems = new ObservableCollection<BomItemModel>();

            BomTreeNodes = new ObservableCollection<BomTreeNodeModel>();

            CategoryFilterItems = new ObservableCollection<string>();
            CategoryTreeFilterNodes = new ObservableCollection<CategoryTreeModel>();
            CustomerFilterItems = new ObservableCollection<string>();
            ProductFinishList = new ObservableCollection<ProductColorDto>();
            SupplyTypeList = new ObservableCollection<string>();
            ProductCategoryList = new ObservableCollection<ProductCategoryDto>();
            CustomerCodeList = new ObservableCollection<string>();
            SupplierCodeList = new ObservableCollection<string>();

            // 重新绑定命令并增加可执行验证
            LoadCommand = new RelayCommand(ExecuteRefresh);
            AddNewRowCommand = new RelayCommand(AddNewRow);
            BatchSaveCommand = new RelayCommand(async () => await BatchSaveAsync(), () => HasChanges);
            DeleteCheckedCommand = new RelayCommand(ExecuteDeleteChecked, () => HasSelectedItems);
            InvertCheckCommand = new RelayCommand(ExecuteInvertCheck, () => AllProductModels != null && AllProductModels.Count > 0);
            ItemCheckedChangedCommand = new RelayCommand(UpdateSelectionState);
            SingleDeleteCommand = new RelayCommand<ProductModel>(ExecuteSingleDelete);
            QuickAddFinishCommand = new RelayCommand(ExecuteQuickAddFinish);
            QuickAddCategoryCommand = new RelayCommand(ExecuteQuickAddCategory);
            ExpandAllCategoryCommand = new RelayCommand(() => ToggleCategoryTreeExpansion(true));
            CollapseAllCategoryCommand = new RelayCommand(() => ToggleCategoryTreeExpansion(false));
            ClearOtherSearchFiltersCommand = new RelayCommand(ExecuteClearOtherSearchFilters);



            // 中文注释：恢复 BOM 区域 Popup 的搜索与提交命令初始化（与主表搜索无关）。
            SearchPdForBomPopupCommand = new RelayCommand<string>(async keyword => await SearchPdForBomPopupAsync(keyword));
            CommitPdForBomPopupCommand = new RelayCommand<object>(CommitPdForBomPopup);

            AllProductModels.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (INotifyPropertyChanged item in e.NewItems)
                        item.PropertyChanged += Item_PropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (INotifyPropertyChanged item in e.OldItems)
                        item.PropertyChanged -= Item_PropertyChanged;
                }
                EvaluateChanges();
            };

            // BOM 区域命令：仅当选中产品拥有有效 BOM 号时才可执行
            SaveBomCommand = new RelayCommand(SaveBomAsync, () => HasActiveBom && HasBomChanges);

            AddBomItemCommand = new RelayCommand(AddBomItem, () => HasActiveBom);
            RemoveBomItemCommand = new RelayCommand(RemoveBomItem, () => HasActiveBom && SelectedBomItem != null);
            // 中文注释：删除/撤销删除改为行级命令（带参数），避免必须依赖 SelectedBomItem。
            DeleteBomItemCommand = new RelayCommand<BomItemModel>(DeleteBomItem, row => HasActiveBom && row != null && row.State != ModelState.Deleted);
            CancelDeleteBomItemCommand = new RelayCommand<BomItemModel>(CancelDeleteBomItem, row => row != null && row.State == ModelState.Deleted);

            OpenProductSelectorCommand = new RelayCommand(ExecuteOpenProductSelector, () => HasActiveBom);

            // 新建BOM命令：仅当选中产品但尚无BOM号时可执行
            CreateBomCommand = new RelayCommand(async () => await ExecuteCreateBomAsync(), () => SelectedProduct != null && !HasActiveBom);

            // 初始化基于本地字典的静态化数据
            SeedStaticLookups();
            // 在UI上初次构建一棵树(此时可能是空数据)
            BuildCategoryTree();

            // 启动异步初始化，确保获取真实树结构和其他字典
            _ = InitializeAsync();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IBaseModel.IsChecked)) return;

            // 监听名称被前端修改，提取名称并直接在已加载的缓存列表中查找赋值ID
            if (sender is ProductModel model)
            {
                // 分类名称被修改了 -> 联动修改分类ID
                if (e.PropertyName == nameof(ProductModel.CategoryName))
                {
                    if (!string.IsNullOrWhiteSpace(model.CategoryName))
                    {
                        var categoryNode = FindCategoryNodeByName(_allCategoryTreeTemplates, model.CategoryName);
                        model.CategoryId = categoryNode != null ? categoryNode.Id : 0;
                    }
                    else
                    {
                        model.CategoryId = 0;
                    }
                }

                // 颜色名称被修改了 -> 联动修改ColorID
                if (e.PropertyName == nameof(ProductModel.Finish))
                {
                    if (!string.IsNullOrWhiteSpace(model.Finish))
                    {
                        var colorObj = ProductFinishList.FirstOrDefault(x =>
                            string.Equals(x.ColorNameZh, model.Finish, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.ColorNameEn, model.Finish, StringComparison.OrdinalIgnoreCase));

                        model.ColorId = colorObj != null ? colorObj.Id : 0;
                    }
                    else
                    {
                        model.ColorId = 0;
                    }
                }
            }

            EvaluateChanges();
        }

        private void TriggerSearchDebounced()
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            if (_isInitializing) return;

            // 中文注释：当当前页不是第一页时，先重置到第一页。
            // 这里会手动触发一次查询，因此通过抑制标记避免 PageCustomerTableIndex setter 重复查询。
            if (PageIndex != 1)
            {
                _suppressNextPageIndexLoad = true;
                PageIndex = 1;
            }

            _ = LoadDataAsync();
        }

        private void EvaluateChanges()
        {
            if (AllProductModels == null) return;
            HasChanges = AllProductModels.Any(x => x.State == ModelState.Added || x.State == ModelState.Modified);
            // 刷新命令的检查状态，使得绑定到 HasChanges 的命令（如保存按钮）能及时变更可用状态
            BatchSaveCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 统一评估 BOM 区域是否存在变更，并刷新相关命令状态。
        /// </summary>
        private void EvaluateBomChanges()
        {
            HasBomChanges = _currentAllBoms.Any(x => x.State == ModelState.Added || x.State == ModelState.Modified || x.State == ModelState.Deleted);
            SaveBomCommand.RaiseCanExecuteChanged();
            DeleteBomItemCommand.RaiseCanExecuteChanged();
            CancelDeleteBomItemCommand.RaiseCanExecuteChanged();
        }

        public void UpdateSelectionState()
        {
            if (AllProductModels == null) return;
            SelectedCount = AllProductModels.Count(x => x.IsChecked);
            HasSelectedItems = SelectedCount > 0;
            _isAllChecked = SelectedCount == AllProductModels.Count && AllProductModels.Count > 0;
            RaisePropertyChanged(nameof(IsAllChecked));

            // 刷新命令使其根据当前选中状态更新是否启用
            DeleteCheckedCommand.RaiseCanExecuteChanged();
            InvertCheckCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region 服务与私有字段 (Services & Private Fields)

        private readonly IServiceProvider _serviceProvider;
        private readonly IProductApi _productApi;
        private readonly IBomApi _bomApi;
        private readonly IProductColorApi _productColorApi;
        private readonly IProductCategoryApi _productCategoryApi;
        private readonly IDictCacheService _dictCacheService;
        private readonly IDialogService _msg;
        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly List<CategoryTreeModel> _allCategoryTreeTemplates;

        // MVVM 初始化防风暴锁
        private bool _isInitializing = true;
        // 中文注释：分页重置时的内部抑制标记。
        // 用于“先改页码再手动触发一次查询”的场景，避免 PageCustomerTableIndex setter 再触发一次重复加载。
        private bool _suppressNextPageIndexLoad;

        // BOM 本地缓存
        private readonly Dictionary<string, List<BomItemModel>> _bomBySku;
        private readonly List<BomItemModel> _currentAllBoms;
        private bool _isSyncingBomItems;

        // 中文注释：BOM加载版本号。
        // 每次触发 LoadBomListAsync 都递增，旧请求返回后若版本不匹配則直接丟弃，避免並發加載導致重複灌入。
        private int _bomLoadVersion;

        // 中文注释：清空筛选条件时的内部保护标记。
        // 作用：避免在一次批量赋值中触发多个 setter 各自发起查询，导致重复 API 调用。
        private bool _isClearingQuickFilters;

        #endregion

        // ==================== 主表 (Master) ====================

        #region 主表 - 集合 (Master - Collections)

        public ObservableCollection<ProductModel> AllProductModels { get; set; }// 绑定到主表格的数据集合
        public ICollectionView ItemsView { get; private set; }// 主表格的视图，用于支持排序、过滤等功能
        public ObservableCollection<ProductColorDto> ProductFinishList { get; private set; }// 产品表面处理的下拉列表数据集合
        public ObservableCollection<ProductCategoryDto> ProductCategoryList { get; private set; }// 产品分类的下拉列表数据集合
        public ObservableCollection<CategoryTreeModel> CategoryTreeFilterNodes { get; private set; }// 分类树形过滤的节点集合
        public ObservableCollection<string> CategoryFilterItems { get; private set; }// 分类过滤的下拉列表数据集合
        public ObservableCollection<string> CustomerFilterItems { get; private set; }// 客户过滤的下拉列表数据集合
        public ObservableCollection<string> SupplyTypeList { get; private set; }// 供应类型的下拉列表数据集合
        public ObservableCollection<string> CustomerCodeList { get; private set; }// 客户编码的下拉列表数据集合
        public ObservableCollection<string> SupplierCodeList { get; private set; }// 供应商编码的下拉列表数据集合

        #endregion

        #region 主表 - 状态与过滤属性 (Master - State & Filter Properties)

        private bool _hasChanges;// 用于跟踪主表格数据是否有未保存的变动，控制保存按钮的启用状态
        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
        }

        private string _selectedColorFilterText;
        public string SelectedColorFilterText
        {
            get { return _selectedColorFilterText; }
            set
            {
                if (SetProperty(ref _selectedColorFilterText, value))
                {
                    if (_isInitializing || _isClearingQuickFilters) return;
                    if (PageIndex != 1) PageIndex = 1;
                }
            }
        }

        private string _selectedColorCodeUnique;
        public string SelectedColorCodeUnique
        {
            get { return _selectedColorCodeUnique; }
            set
            {
                if (SetProperty(ref _selectedColorCodeUnique, value))
                {
                    if (_isInitializing || _isClearingQuickFilters) return;
                    if (PageIndex != 1) PageIndex = 1;
                }
            }
        }

        private ProductModel _selectedProduct;// 当前选中的产品，用于显示和编辑其BOM信息
        public ProductModel SelectedProduct
        {
            get { return _selectedProduct; }
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                {
                    // 中文注释：选中产品变化时，先刷新 BOM 区域启用状态。
                    RaisePropertyChanged(nameof(HasActiveBom));
                    RaiseBomCommandsCanExecuteChanged();

                    // 中文注释：在 CellOrRowHeader 模式下，DataGrid 会出现瞬时 null 抖动。
                    // 若这里直接触发 CheckAndLoadBom()，会清空并中断正在返回的 BOM 数据。
                    // 因此 null 场景仅刷新状态，不执行 BOM 重载。
                    if (value == null)
                    {
                        return;
                    }

                    CheckAndLoadBom();
                }
            }
        }

        private string _categoryKeyword;// 分类树的搜索关键字，绑定到UI的搜索框，用户输入时会触发树的重建以实现过滤效果
        public string CategoryKeyword
        {
            get { return _categoryKeyword; }
            set
            {
                if (SetProperty(ref _categoryKeyword, value))
                {
                    BuildCategoryTree();
                }
            }
        }

        private string _selectedCustomerFilter = "全部客户";// 当前选中的客户过滤条件，默认为"全部客户"，当用户选择不同客户时会触发数据重新加载以应用过滤
        public string SelectedCustomerFilter
        {
            get { return _selectedCustomerFilter; }
            set
            {
                if (SetProperty(ref _selectedCustomerFilter, value))
                {
                    if (_isInitializing || _isClearingQuickFilters) return;
                    if (PageIndex != 1) PageIndex = 1;
                    //频繁的查库,我们取消它
                    //else _ = LoadDataAsync();
                }
            }
        }

        private string _selectedCategoryFilter = "全部分类";// 当前选中的分类过滤条件，默认为"全部分类"，当用户选择不同分类时会触发数据重新加载以应用过滤
        public string SelectedCategoryFilter
        {
            get { return _selectedCategoryFilter; }
            set
            {
                if (SetProperty(ref _selectedCategoryFilter, value))
                {
                    if (_isInitializing) return;
                    if (PageIndex != 1) PageIndex = 1;
                    else _ = LoadDataAsync();
                }
            }
        }

        private int _pageIndex = 1;// 当前页码，绑定到UI的分页控件，用户切换页码时会触发数据重新加载以显示对应页的数据
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (SetProperty(ref _pageIndex, value))
                {
                    if (_isInitializing) return;

                    // 中文注释：当调用方明确声明“本次由外部手动触发查询”时，跳过 setter 内的自动查询。
                    if (_suppressNextPageIndexLoad)
                    {
                        _suppressNextPageIndexLoad = false;
                        return;
                    }

                    _ = LoadDataAsync();
                }
            }
        }

        private int _pageSize = 50;// 每页行数，绑定到UI的分页控件，用户修改每页行数时会触发数据重新加载以应用新的分页设置
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    if (_isInitializing) return;
                    if (PageIndex == 1) _ = LoadDataAsync();
                    else PageIndex = 1;
                }
            }
        }

        private int _totalCount;// 数据总条数，绑定到UI的分页控件，用于显示总页数和当前数据范围等信息，由数据加载方法在获取数据后设置
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private string _searchText;// 主表格的搜索关键字，绑定到UI的搜索框，用户输入时会触发防抖机制以减少频繁查询，并在停止输入后重新加载数据以应用搜索过滤
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    if (_isInitializing || _isClearingQuickFilters) return;
                    TriggerSearchDebounced();
                }
            }
        }

        private string _statusMessage = "准备就绪";// UI底部的状态提示信息，绑定到UI的状态栏，用于显示当前操作状态、错误信息等，由各个操作方法在执行过程中设置以反馈给用户
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _hasSelectedItems;// 是否有选中的行，绑定到UI的删除按钮等操作的启用状态，当用户勾选或取消勾选行时会更新此属性以控制相关操作的可用性
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            set => SetProperty(ref _hasSelectedItems, value);
        }

        private int _selectedCount;// 当前选中的行数，绑定到UI的状态提示等位置，用于显示选中数量等信息，当用户勾选或取消勾选行时会更新此属性以反馈给用户
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        private bool _isAllChecked;// 是否全选，绑定到UI的表头全选复选框，当用户勾选或取消勾选全选框时会更新此属性并联动更新所有行的选中状态，同时当用户单独勾选或取消勾选行时也会更新此属性以反映当前是否所有行都被选中
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (SetProperty(ref _isAllChecked, value) && AllProductModels != null)
                {
                    foreach (var item in    AllProductModels) item.IsChecked = value;
                    UpdateSelectionState();
                }
            }
        }

        #endregion

        #region 主表 - 命令 (Master - Commands)

        public RelayCommand LoadCommand { get; private set; }// 刷新数据的命令，绑定到UI的刷新按钮，当用户点击刷新时会执行此命令以重新加载数据
        public RelayCommand AddNewRowCommand { get; private set; }// 添加新行的命令，绑定到UI的添加按钮，当用户点击添加时会执行此命令在表格顶部插入一行新的空数据以供编辑
        public RelayCommand BatchSaveCommand { get; private set; }// 批量保存的命令，绑定到UI的保存按钮，当用户点击保存时会执行此命令將所有新增或修改的行提交到後端進行保存，並在成功後刷新數據以顯示最新狀態
        public RelayCommand DeleteCheckedCommand { get; private set; }// 删除选中行的命令，绑定到UI的删除按钮，当用户点击删除时会执行此命令删除所有被勾选的行，先删除未保存的新增行，再对已保存的行进行确认后调用API删除，并在成功后刷新数据以反映变动
        public RelayCommand InvertCheckCommand { get; private set; }// 反选的命令，绑定到UI的反选按钮，当用户点击反选时会执行此命令将所有行的选中状态取反，并更新相关的选中数量和全选状态以反馈给用户
        public RelayCommand ItemCheckedChangedCommand { get; private set; }// 行选中状态改变的命令，绑定到每行的选中复选框，当用户勾选或取消勾选某行时会执行此命令更新选中数量、全选状态以及删除按钮等相关操作的启用状态以反映当前的选择情况
        public RelayCommand<ProductModel> SingleDeleteCommand { get; private set; }// 单行删除的命令，绑定到每行的删除按钮，当用户点击某行的删除按钮时会执行此命令将该行标记为选中状态并调用批量删除命令以统一处理删除逻辑
        public RelayCommand QuickAddFinishCommand { get; private set; }// 快速添加表面处理的命令，绑定到UI的快速添加表面处理按钮，当用户点击时会打开一个新的窗口用于管理表面处理选项，窗口关闭后会刷新表面处理列表以反映新增或修改的选项
        public RelayCommand QuickAddCategoryCommand { get; private set; }// 快速添加分类的命令，绑定到UI的快速添加分类按钮，当用户点击时会打开一个新的窗口用于管理产品分类，窗口关闭后会刷新分类树和相关列表以反映新增或修改的分类选项
        public RelayCommand ExpandAllCategoryCommand { get; private set; }// 展开所有分类树节点的命令，绑定到UI的展开按钮，当用户点击时会执行此命令将分类树中的所有节点展开以显示完整的树结构
        public RelayCommand CollapseAllCategoryCommand { get; private set; }// 折叠所有分类树节点的命令，绑定到UI的折叠按钮，当用户点击时会执行此命令将分类树中的所有节点折叠以隐藏子节点只显示顶层节点
        public RelayCommand ClearOtherSearchFiltersCommand { get; private set; }// 清空客户、颜色、模糊搜索等非分类条件，便于用户快速按分类筛选

        #endregion

        // ==================== 从表 BOM (Detail) ====================

        #region 从表(BOM) - 集合 (BOM - Collections)

        public ObservableCollection<BomItemModel> CurrentBomItems { get; private set; }// 绑定到BOM子表格的数据集合
        public ObservableCollection<BomTreeNodeModel> BomTreeNodes { get; private set; }// 绑定到BOM树形控件的数据集合

        public ObservableCollection<ProductModel> BomPopItemSource { get; private set; } = new ObservableCollection<ProductModel>();// BOM条目中用于搜索产品的下拉列表数据集合

        private ProductModel _bomPopSelectedItem;
        public ProductModel BomPopSelectedItem
        {
            get => _bomPopSelectedItem;
            set => SetProperty(ref _bomPopSelectedItem, value);
        }

        // Removed old BomSkuSearchResults

        #endregion

        #region 从表(BOM) - 状态属性 (BOM - State Properties)

        /// <summary>
        /// 当前选中产品是否拥有有效的 BOM 号（非空白字符串）。
        /// 用于控制 BOM 区域按钮（获取/保存/删除/添加子件）的启用/隐藏状态。
        /// </summary>
        public bool HasActiveBom
        {
            get { return SelectedProduct != null && !string.IsNullOrWhiteSpace(SelectedProduct.BomNo); }
        }

        private bool _hasBomChanges;// 用于跟踪主表格数据是否有未保存的变动，控制保存按钮的启用状态
        public bool HasBomChanges
        {
            get => _hasBomChanges;
            set => SetProperty(ref _hasBomChanges, value);
        }

        /// <summary>
        /// 统一刷新所有 BOM 区域命令的 CanExecute 状态。
        /// 在选中产品切换、BOM 号变更等场景下调用。
        /// </summary>
        private void RaiseBomCommandsCanExecuteChanged()
        {
            AddBomItemCommand.RaiseCanExecuteChanged();
            SaveBomCommand.RaiseCanExecuteChanged();
            RemoveBomItemCommand.RaiseCanExecuteChanged();
            OpenProductSelectorCommand.RaiseCanExecuteChanged();
            CreateBomCommand.RaiseCanExecuteChanged();
        }

        private void CheckAndLoadBom()
        {
            if (SelectedProduct != null && !IsBomAreaCollapsed && SelectedProduct.SupplyType == SupplyType.按BOM拆分 && !string.IsNullOrWhiteSpace(SelectedProduct.BomNo))
            {
                LoadBomListAsync(SelectedProduct);
            }
            else
            {
                LoadBomListAsync(null);
            }
        }

        private bool _isBomAreaCollapsed;
        public bool IsBomAreaCollapsed
        {
            get { return _isBomAreaCollapsed; }
            set
            {
                if (SetProperty(ref _isBomAreaCollapsed, value))
                {
                    if (!_isBomAreaCollapsed)
                    {
                        CheckAndLoadBom();
                    }
                }
            }
        }

        private BomItemModel _selectedBomItem;// 当前选中的BOM项，用于启用删除按钮等操作
        public BomItemModel SelectedBomItem
        {
            get { return _selectedBomItem; }
            set
            {
                if (SetProperty(ref _selectedBomItem, value))
                {
                    RemoveBomItemCommand.RaiseCanExecuteChanged();
                    DeleteBomItemCommand.RaiseCanExecuteChanged();
                    CancelDeleteBomItemCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private string _bomSearchText;// BOM树的搜索关键字，绑定到UI的搜索框，用户输入时会触发BOM列表的过滤以实现搜索效果
        public string BomSearchText
        {
            get { return _bomSearchText; }
            set
            {
                if (SetProperty(ref _bomSearchText, value))
                {
                    ApplyBomFilters();
                }
            }
        }

        #endregion

        #region 从表(BOM) - 命令 (BOM - Commands)

        public RelayCommand AddBomItemCommand { get; private set; }// 添加BOM项的命令，绑定到BOM子表的添加按钮，当用户点击添加时会执行此命令在当前选中产品的BOM列表中插入一行新的空数据以供编辑
        public RelayCommand SaveBomCommand { get; private set; }// 保存BOM的命令，绑定到BOM子表的保存按钮，当用户点击保存时会执行此命令将当前选中产品的BOM列表提交到后端进行保存，并在成功后刷新BOM列表以显示最新状态
        public RelayCommand RemoveBomItemCommand { get; private set; }// 删除BOM项的命令，绑定到BOM子表每行的删除按钮，当用户点击某行的删除按钮时会执行此命令将该行从当前选中产品的BOM列表中移除，并在保存时一并提交变动到后端
        public RelayCommand<BomItemModel> DeleteBomItemCommand { get; private set; }// 删除BOM项的命令（行级参数），用于将指定行标记为待删除
        public RelayCommand<BomItemModel> CancelDeleteBomItemCommand { get; private set; }// 取消删除BOM项的命令（行级参数），用于将指定行从待删除状态恢复

        public RelayCommand<string> SearchPdForBomPopupCommand { get; private set; }//Bom条目中搜索产品的命令，绑定到BOM编辑界面中用于选择产品的搜索框，当用户输入搜索关键字并执行搜索时会触发此命令调用API获取匹配的产品列表以供用户选择添加到BOM中
        public RelayCommand<object> CommitPdForBomPopupCommand { get; private set; }//Bom条目中提交选择的产品的命令，绑定到BOM编辑界面中用于选择产品的确认按钮，当用户选中一个产品并点击确认时会触发此命令將選中的產品信息添加到當前BOM列表中作為新的BOM項
        public RelayCommand OpenProductSelectorCommand { get; private set; }// 打开产品选择器窗口，用户选中产品后批量追加到当前BOM列表
        public RelayCommand CreateBomCommand { get; private set; }// 为没有BOM号的产品新建BOM的命令

        #endregion

        #region 主表数据操作 (Master Data Operations)

        /// <summary>
        /// 视图模型初始化，负责加载全量依赖（字典、类目树）以及第一页表格数据
        /// </summary>

        //快速添加表面处理的命令响应方法，打开表面处理管理窗口，窗口关闭后刷新表面处理列表以反映变动
        private async void ExecuteQuickAddFinish()
        {
            var vm = (ProductColorManagerViewModel)_serviceProvider.GetService(typeof(ProductColorManagerViewModel));
            var win = new Views.ProductColorManagerView(vm);
            win.ShowDialog();
            // 窗体关闭后重新拉取字典等数据以刷新下拉列表
            await LoadFilterDataAsync();
        }
        //快速添加分类的命令响应方法，打开分类管理窗口，窗口关闭后刷新分类树和相关列表以反映变动
        private async void ExecuteQuickAddCategory()
        {
            var vm = (ProductCategoryManagerViewModel)_serviceProvider.GetService(typeof(ProductCategoryManagerViewModel));
            var win = new Views.ProductCategoryManagerView(vm);
            win.ShowDialog();
            // 窗体关闭后重新拉取字典/分类等数据刷新树和下拉列表
            await LoadFilterDataAsync();
        }

        /// 初始化方法，负责在视图模型构造完成后加载必要的字典数据和第一页的表格数据
        private async Task InitializeAsync()
        {
            _isInitializing = true;
            // 首次进来先拉取产品分类树、客户列表等前置依赖
            await LoadFilterDataAsync();

            _isInitializing = false;
            // 等依赖项就绪后再加载第一页查询数据
            await LoadDataAsync();
        }

        /// <summary>
        /// 用户手动点击【刷新】按钮的响应事件
        /// </summary>
        private async void ExecuteRefresh()
        {
            _isInitializing = true;
            // 显式刷新时，有必要重新拉取全量树结构
            await LoadFilterDataAsync();

            _isInitializing = false;
            // 随后重新查询商品列表
            _ = LoadDataAsync();
        }

        /// <summary>
        /// 拉取产品数据的核心方法，根据当前的搜索关键字、客户过滤、分类过滤以及分页设置构建查询条件，
        /// 调用API获取数据后更新UI绑定的集合和状态提示等信息。此方法会在多个场景被调用，
        /// 如初始加载、用户修改过滤条件、分页切换等，因此需要做好状态管理以避免重复查询或不必要的查询。
        /// 注意，此方法不再负责加载树结构数据，树结构的数据加载已被移除到 LoadFilterDataAsync 方法中，
        /// 以避免在树节点选中变化时触发重复查询导致的性能问题和潜在的UI崩溃风险。
        /// </summary>
        private async Task LoadDataAsync()
        {
            // 做基本保护：如果API实例未注入，直接返回
            if (_productApi == null) return;

            // 更新UI底部的文本状态提示
            StatusMessage = "正在加载数据...";
            try
            {
                // 【Bug已修复】: 移除这里的 await LoadFilterDataAsync();
                // 因为在树节点选中变化触发 SelectedCategoryFilter 变更时，
                // 会执行 LoadDataAsync()。如果继续调用 LoadFilterDataAsync()，
                // 那一瞬间将会清空树结构重新查询 API，这会丢失选中状态并且当API报错时树被清空导致崩溃。

                // 构建用于提交给后端的查询条件 DTO
                var search = new ProductSearchDto
                {
                    // 获取搜索关键字
                    Keyword = SearchText,
                    //// 如果选中的是"全部客户"，则不传过滤条件
                    //CustomerCode = SelectedCustomerFilter == "全部客户" ? null : SelectedCustomerFilter,
                    //// 按分类名筛选（支持包含子集）
                    //CategoryNames = GetCategoryNamesIncludeChildren(SelectedCategoryFilter),
                    ColorCodeUnique = string.IsNullOrWhiteSpace(SelectedColorCodeUnique) ? null : SelectedColorCodeUnique,
                    // 绑定到当前页面索引
                    PageIndex = PageIndex,
                    // 绑定到当前的页面行数(PageCustomerTableSize)
                    PageSize = PageSize
                };

                var result = await _productApi.GetProductPagedListAsync(search);

                DispatcherProvider.Invoke(() =>
                {
                    if (result.Success && result.Data != null)
                    {
                        AllProductModels.Clear();

                        foreach (var dto in result.Data.Items)
                        {
                            var model = dto.Adapt<ProductModel>();
                            model.State = ModelState.Unchanged;
                            AllProductModels.Add(model);
                        }

                        TotalCount = result.Data.TotalCount;
                        HasChanges = false;
                        IsAllChecked = false;
                        UpdateSelectionState();
                        StatusMessage = $"加载成功，共 {TotalCount} 条。";
                    }
                    else
                    {
                        StatusMessage = "加载失败: " + result.Message;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常: " + ex.Message;
            }
        }
        // 添加新行的方法，构建一个默认值的 ProductModel 插入到 AllProductModels 集合的顶部，并将其状态标记为 Added 以供后续保存时识别
        private void AddNewRow()
        {
            var nextId = AllProductModels.Count == 0 ? 1 : AllProductModels.Max(x => x.Id) + 1;
            var product = new SwiftExport.View.FW.ProductViews.Models.ProductModel
            {
                Id = 0,
                MasterSku = string.Empty,
                Sku = "NEW" + nextId.ToString("000000"),
                Description = "新建产品",
                State = ModelState.Added
            };

            AllProductModels.Insert(0, product);
            StatusMessage = "已添加新行，请录入数据。";
            SelectedProduct = product;
        }
        // 批量保存的方法，首先收集所有新增或修改的行进行校验，如果存在不合法的数据则提示用户修正；如果数据合法，则构建批量请求对象并调用 API 进行保存，最后根据结果更新状态和刷新数据
        private async Task BatchSaveAsync()
        {
            var dirtyModels = AllProductModels.Where(x => x.State == ModelState.Added || x.State == ModelState.Modified).ToList();
            if (!dirtyModels.Any())
            {
                await _msg.ShowInfoAsync("没有需要保存的变动，所有数据均已是最新状态。");
                return;
            }

            // 【修改点】：将校验范围从 dirtyModels 扩大到 AllProductModels
            // 这样无论数据是初次加载还是手动修改，只要列表里有不合法的数据（哪怕你没修改它），都会拦截并提示。
            var invalidModels = AllProductModels.Where(x => !string.IsNullOrEmpty(x.Error)).ToList();
            if (invalidModels.Any())
            {
                // 取第一条错误信息展示给用户
                var firstError = invalidModels.First();
                string errorMsg = "";
                if (!string.IsNullOrEmpty(firstError[nameof(firstError.Sku)])) errorMsg = firstError[nameof(firstError.Sku)];
                else if (!string.IsNullOrEmpty(firstError[nameof(firstError.Description)])) errorMsg = firstError[nameof(firstError.Description)];

                await _msg.ShowErrorAsync($"抱歉，列表内存在必填项未填写或格式错误的数据，请修正后再保存。\n提示信息：{errorMsg}");
                return;
            }

            StatusMessage = "正在保存...";

            var request = new BatchRequestGeneric<ProductDto>
            {
                AddList = dirtyModels.Where(x => x.State == ModelState.Added).Select(x => x.Adapt<ProductDto>()).ToList(),
                UpdateList = dirtyModels.Where(x => x.State == ModelState.Modified).Select(x => x.Adapt<ProductDto>()).ToList(),
                DeleteIdList = new List<int>()
            };

            try
            {
                var rst = await _productApi.BatchSaveAsync(request);
                if (rst.Success)
                {
                    foreach (var item in dirtyModels) item.State = ModelState.Unchanged;
                    HasChanges = false;
                    await _msg.ShowInfoAsync("保存成功！");
                    _ = LoadDataAsync();
                }
                else
                {
                    await _msg.ShowErrorAsync("保存失败：" + rst.Message);
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }
        // 删除选中行的方法，首先收集所有被勾选的行，如果没有选中任何行则直接返回；对于未保存的新行，直接从集合中移除；对于已保存的行，先弹出确认框询问用户是否确定删除，如果用户确认则调用 API 进行删除，最后根据结果更新状态和刷新数据
        private async void ExecuteDeleteChecked()
        {
            var checkedItems = AllProductModels.Where(x => x.IsChecked).ToList();
            if (checkedItems.Count <= 0) return;

            StatusMessage = "正在删除...";
            try
            {
                var unsavedItems = checkedItems.Where(x => x.State == ModelState.Added).ToList();
                foreach (var item in unsavedItems) AllProductModels.Remove(item);

                var dbItems = checkedItems.Where(x => x.State != ModelState.Added).ToList();
                if (dbItems.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync($"确定要永久删除这 {dbItems.Count} 行数据吗？此操作不可逆！")) return;

                    bool isSuccess = false;
                    string errMsg = "";

                    if (dbItems.Count == 1)
                    {
                        var rst = await _productApi.DeleteAsync(dbItems.First().Id);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    else
                    {
                        var req = new BatchRequestGeneric<ProductDto> { DeleteIdList = dbItems.Select(x => x.Id).ToList() };
                        var rst = await _productApi.BatchSaveAsync(req);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }

                    if (isSuccess)
                    {
                        foreach (var item in dbItems) AllProductModels.Remove(item);
                        await _msg.ShowInfoAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + errMsg);
                        return;
                    }
                }
            }
            catch (Exception ex) { await _msg.ShowErrorAsync("删除异常：" + ex.Message); }
            finally
            {
                StatusMessage = "准备就绪";
                UpdateSelectionState();
            }
        }
        // 单行删除的方法，接收一个 ProductModel 作为参数，将其标记为选中状态并调用批量删除方法以统一处理删除逻辑
        private void ExecuteSingleDelete(ProductModel model)
        {
            if (model == null) return;
            model.IsChecked = true;
            ExecuteDeleteChecked();
        }
        // 反选的方法，遍历所有行将其选中状态取反，并更新相关的选中数量和全选状态以反馈给用户
        private void ExecuteInvertCheck()
        {
            foreach (var item in AllProductModels) item.IsChecked = !item.IsChecked;
            UpdateSelectionState();
        }

        #endregion

        #region 字典与下拉数据加载 (Dictionary & Lookups)

        private async Task LoadFilterDataAsync()
        {
            await _dictCacheService.PreloadAsync(new[] { DictCodes.SupplyType, DictCodes.CustomerType, DictCodes.ProductType });

            SeedStaticLookups();
            await LoadCustomersAsync();
            await LoadCategoryTreeAsync();
            await LoadFinishAsync();
        }

        /// 加载产品表面处理列表，填充 ProductFinishList 
        private async Task LoadFinishAsync()
        {
            try
            {
                var result = await _productColorApi.GetAllAsync();
                if (result == null || !result.Success || result.Data == null) return;

                ProductFinishList.Clear();
                foreach (var item in result.Data)
                {
                    ProductFinishList.Add(item);
                }

            }
            catch
            {
                // 后端未就绪时继续使用默认数据
            }
        }

        /// 加载客户列表，填充 CustomerCodeList 和 CustomerFilterItems
        private async Task LoadCustomersAsync()
        {
            try
            {
                var result = await _productApi.GetAllCustomerCodeListAsync();
                if (result == null || !result.Success || result.Data == null) return;

                CustomerCodeList.Clear();
                CustomerFilterItems.Clear();
                CustomerFilterItems.Add("全部客户");

                foreach (var code in result.Data.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                {
                    CustomerCodeList.Add(code);
                    CustomerFilterItems.Add(code);
                }
            }
            catch
            {
                // 后端未就绪时继续使用默认数据
            }
        }

        /// 加载产品分类树，填充 ProductCategoryList、CategoryFilterItems 和内部的 _allCategoryTreeTemplates
        private async Task LoadCategoryTreeAsync()
        {
            try
            {
                var result = await _productCategoryApi.GetAllAsync();
                if (result == null || !result.Success || result.Data == null) return;

                _allCategoryTreeTemplates.Clear();

                var allDtos = result.Data.OrderBy(x => x.SortNo).ToList();
                var byId = allDtos.ToDictionary(x => x.Id, x => new CategoryTreeModel
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    CategoryName = x.CategoryName,
                    CategoryCode = x.CategoryCode,
                    FakeName = x.FakeName,
                    SortNo = x.SortNo
                });

                foreach (var dto in allDtos)
                {
                    CategoryTreeModel node;
                    if (!byId.TryGetValue(dto.Id, out node)) continue;

                    if (dto.ParentId.HasValue && byId.ContainsKey(dto.ParentId.Value))
                    {
                        byId[dto.ParentId.Value].Children.Add(node);
                    }
                    else
                    {
                        _allCategoryTreeTemplates.Add(node);
                    }
                }

                ProductCategoryList.Clear();
                ProductCategoryList.Add(new ProductCategoryDto { Id = 0, CategoryName = "未分类" });

                foreach (var dto in allDtos.Where(x => !string.IsNullOrWhiteSpace(x.CategoryName)).OrderBy(x => x.CategoryName))
                {
                    ProductCategoryList.Add(dto);
                }

                CategoryFilterItems.Clear();
                CategoryFilterItems.Add("全部分类");
                foreach (var name in allDtos.Select(x => x.CategoryName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x))
                {
                    CategoryFilterItems.Add(name);
                }

                BuildCategoryTree();
            }
            catch
            {
                // 后端未就绪时继续使用默认数据
            }
        }
        // 根据选中的分类名称，递归查找其对应的节点，并收集该节点及其所有子节点的名称列表，以供查询时使用
        private List<string> GetCategoryNamesIncludeChildren(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || categoryName == "全部分类") return null;

            var result = new List<string>();

            // 在所有的树中找
            var node = FindCategoryNodeByName(_allCategoryTreeTemplates, categoryName);
            if (node != null)
            {
                CollectCategoryNames(node, result);
            }
            else
            {
                // 如果没找到对应的节点，至少加进去它自己
                result.Add(categoryName);
            }

            return result;
        }
        // 递归查找树节点的方法，根据名称匹配，如果找到则返回该节点，否则继续在子节点中查找，直到找到或遍历完所有节点
        private CategoryTreeModel FindCategoryNodeByName(IEnumerable<CategoryTreeModel> nodes, string name)
        {
            if (nodes == null) return null;
            foreach (var node in nodes)
            {
                if (node.CategoryName == name) return node;
                var found = FindCategoryNodeByName(node.Children, name);
                if (found != null) return found;
            }
            return null;
        }

        // 递归收集节点名称的方法，将当前节点的名称加入列表，并继续对其子节点进行同样的操作，直到遍历完所有子节点
        private void CollectCategoryNames(CategoryTreeModel node, List<string> list)
        {
            if (node == null) return;
            if (!list.Contains(node.CategoryName)) list.Add(node.CategoryName);
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                CollectCategoryNames(child, list);
            }
        }

        // 加载静态字典数据的方法，从字典缓存服务中获取相关的字典值并填充到对应的 ObservableCollection 中，以供UI绑定使用，同时清理掉原有的假数据以避免干扰
        private void SeedStaticLookups()
        {
            SupplyTypeList.Clear();
            var supplyTypes = _dictCacheService.GetValues(DictCodes.SupplyType);
            if (supplyTypes != null && supplyTypes.Any())
            {
                foreach (var item in supplyTypes) SupplyTypeList.Add(item);
            }

            // 移除原本的硬编码假数据（分类、客户、供应商等将由各API动态填充或预留为空）
            ProductCategoryList.Clear();
            ProductCategoryList.Add(new ProductCategoryDto { Id = 0, CategoryName = "未分类" });

            CategoryFilterItems.Clear();
            CategoryFilterItems.Add("全部分类");

            CustomerCodeList.Clear();
            CustomerFilterItems.Clear();
            CustomerFilterItems.Add("全部客户");

            SupplierCodeList.Clear();

            // 【Bug修复】不要在这里直接清理模板树
            // 否则在API 404 或者失败时，原始模板数据会被无端抹除
        }

        // 构建分类树的方法，首先清空当前用于UI绑定的树节点集合，
        // 然后创建一个"全部分类"的根节点并订阅其属性变更事件以监听选中状态，
        // 接着克隆一份完整的树结构模板并根据搜索关键字进行过滤，最后将符合条件的节点添加到UI绑定的集合中，并根据当前选中的分类名称设置对应节点的选中状态
        private void BuildCategoryTree()
        {
            CategoryTreeFilterNodes.Clear();

            var allNode = new CategoryTreeModel { Id = 1, CategoryName = "全部分类", IsExpanded = true };
            SubscribeCategoryNode(allNode);
            CategoryTreeFilterNodes.Add(allNode);

            var clonedRoots = _allCategoryTreeTemplates
                .Select(CloneCategoryNode)
                .Where(x => IsNodeVisibleByKeyword(x, CategoryKeyword))
                .ToList();

            bool forceExpand = !string.IsNullOrWhiteSpace(CategoryKeyword);

            foreach (var node in clonedRoots)
            {
                node.IsExpanded = forceExpand || true;
                if (forceExpand) SetNodeExpansion(node, true);

                // Keep the selection if it matches
                if (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) && SelectedCategoryFilter != "全部分类")
                {
                    var selectedChild = FindCategoryNodeByName(new[] { node }, SelectedCategoryFilter);
                    if (selectedChild != null)
                        selectedChild.IsSelected = true;
                }

                SubscribeCategoryNode(node);
                CategoryTreeFilterNodes.Add(node);
            }

            if (string.IsNullOrWhiteSpace(SelectedCategoryFilter) || SelectedCategoryFilter == "全部分类")
            {
                allNode.IsSelected = true;
            }
        }

        // 展开或折叠分类树的所有节点的方法，接收一个布尔值参数表示是展开还是折叠，然后遍历当前UI绑定的树节点集合，对每个节点调用 SetNodeExpansion 方法进行递归设置
        private void ToggleCategoryTreeExpansion(bool isExpanded)
        {
            foreach (var node in CategoryTreeFilterNodes)
            {
                SetNodeExpansion(node, isExpanded);
            }
        }

        // 递归设置节点展开状态的方法，首先检查当前节点是否为 null，如果是则直接返回；
        // 否则将当前节点的 IsExpanded 属性设置为传入的值，然后如果该节点有子节点，则对每个子节点继续调用 SetNodeExpansion 方法进行同样的设置，直到遍历完所有子节点
        private void SetNodeExpansion(CategoryTreeModel node, bool isExpanded)
        {
            if (node == null) return;
            node.IsExpanded = isExpanded;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    SetNodeExpansion(child, isExpanded);
                }
            }
        }

        // 克隆分类树节点的方法，接收一个源节点作为参数，创建一个新的节点并复制源节点的基本属性（如 Id、ParentId、CategoryName 等），
        private static CategoryTreeModel CloneCategoryNode(CategoryTreeModel source)
        {
            var clone = new CategoryTreeModel
            {
                Id = source.Id,
                ParentId = source.ParentId,
                CategoryName = source.CategoryName,
                CategoryCode = source.CategoryCode,
                FakeName = source.FakeName,
                SortNo = source.SortNo
            };
            foreach (var child in source.Children) clone.Children.Add(CloneCategoryNode(child));
            return clone;
        }

        // 判断节点是否应该在当前搜索关键字过滤下可见的方法，首先检查关键字是否为空或仅包含空白字符，如果是则认为所有节点都可见返回 true；
        private static bool IsNodeVisibleByKeyword(CategoryTreeModel node, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            if (Contains(node.CategoryName, keyword)) return true;
            return node.Children.Any(child => IsNodeVisibleByKeyword(child, keyword));
        }

        // 订阅分类树节点属性变更事件的方法，接收一个节点作为参数，对该节点的 PropertyChanged 事件进行订阅，指定事件处理方法为 CategoryNode_PropertyChanged；
        private void SubscribeCategoryNode(CategoryTreeModel node)
        {
            node.PropertyChanged += CategoryNode_PropertyChanged;
            foreach (var child in node.Children) SubscribeCategoryNode(child);
        }

        /// <summary>
        /// 类目节点的属性变更事件处理 (主要用于监听 IsSelected 变化)
        /// </summary>
        private void CategoryNode_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 我们只关注 IsSelected 这个属性的跳变，如果不是它更改，直接忽略
            if (e.PropertyName != nameof(CategoryTreeModel.IsSelected)) return;

            // 将事件发送者显式转换为类目节点对应的 Model
            var node = sender as CategoryTreeModel;

            // 做基本为空保护，且仅处理"变为选中"的情况，忽略取消选中的触发
            if (node == null || !node.IsSelected) return;

            // 检查当前视图主查询条件中的类目过滤名，是否真的和选中节点的类目名称不一样
            if (SelectedCategoryFilter != node.CategoryName)
            {
                // 如果不同，将当前条件进行赋值。
                // 注意：一旦给它赋值，就会触发 SelectedCategoryFilter setter，最终再次调用 _ = LoadDataAsync() 刷新 DataGrid！
                SelectedCategoryFilter = node.CategoryName;
            }
        }

        #endregion

        #region BOM操作 (BOM Operations)

        private async void LoadBomListAsync(ProductModel product)
        {
            // 中文注释：进入一次新的BOM加载流程，生成当前请求版本号。
            var currentLoadVersion = ++_bomLoadVersion;

            DispatcherProvider.Invoke(() =>
            {
                DetachBomItemHandlers();
                _currentAllBoms.Clear();
                CurrentBomItems.Clear();
                BomTreeNodes.Clear();
                SelectedBomItem = null;
                BomPopSelectedItem = null;
                IsPopupOpen = false;
                BomPopItemSource.Clear();
                // 中文注释：切换产品或清空 BOM 时，立即重置变更状态，避免保存按钮状态残留。
                EvaluateBomChanges();
            });

            if (product == null || string.IsNullOrWhiteSpace(product.Sku))
            {
                DispatcherProvider.Invoke(() =>
                {
                    AddBomItemCommand.RaiseCanExecuteChanged();
                    SaveBomCommand.RaiseCanExecuteChanged();
                    RemoveBomItemCommand.RaiseCanExecuteChanged();
                    OpenProductSelectorCommand.RaiseCanExecuteChanged();
                });
                return;
            }

            // 中文注释：优先尝试从API加载，返回的是快照列表（不在方法内部直接写UI集合）。
            var apiSnapshot = await TryLoadBomFromApiAsync(product.Sku);

            // 中文注释：若当前请求已过期（期间用户又选了别的行），直接丢弃本次结果。
            if (currentLoadVersion != _bomLoadVersion) return;

            if (apiSnapshot != null)
            {
                foreach (var item in apiSnapshot)
                {
                    AddBomItemToCurrent(item);
                }
            }
            else
            {
                List<BomItemModel> bomItems;
                if (_bomBySku.TryGetValue(product.Sku, out bomItems))
                {
                    foreach (var item in bomItems)
                    {
                        AddBomItemToCurrent(new BomItemModel
                        {
                            Id = item.Id,
                            BomNo = item.BomNo,
                            SubSku = item.SubSku,
                            Description = item.Description,
                            Numerator = item.Numerator,
                            Denominator = item.Denominator,
                            BomSubType = item.BomSubType,
                            Note = item.Note,
                            State = item.State
                        });
                    }
                }
            }

            // 中文注释：再次校验版本，避免 ApplyBomFilters 被过期请求覆盖。
            if (currentLoadVersion != _bomLoadVersion) return;

            ApplyBomFilters();

            DispatcherProvider.Invoke(() =>
            {
                AddBomItemCommand.RaiseCanExecuteChanged();
                SaveBomCommand.RaiseCanExecuteChanged();
                RemoveBomItemCommand.RaiseCanExecuteChanged();
                OpenProductSelectorCommand.RaiseCanExecuteChanged();
            });
        }

        private async Task<List<BomItemModel>> TryLoadBomFromApiAsync(string sku)
        {
            if (_bomApi == null) return null;

            try
            {
                var result = await _bomApi.GetBomTreeAsync(sku);
                if (result == null || !result.Success || result.Data == null) return null;

                var roots = result.Data.Where(x => x != null).ToList();
                var flatNodes = FlattenBomTree(roots).ToList();
                var hasTreeShape = roots.Any(x => x.Children != null && x.Children.Count > 0);

                var sourceNodes = hasTreeShape
                    ? flatNodes.Where(x => !string.Equals(x.SubSku, sku, StringComparison.OrdinalIgnoreCase))
                    : flatNodes;

                // 中文注释：构造快照列表并返回给调用方统一入仓，避免方法内部直接修改共享集合导致并发重复。
                var snapshot = new List<BomItemModel>();
                foreach (var dto in sourceNodes)
                {
                    snapshot.Add(new BomItemModel
                    {
                        Id = dto.Id,
                        BomNo = dto.BomNo,
                        SubSku = dto.SubSku,
                        Description = dto.Description,
                        Numerator = dto.Numerator <= 0 ? 1 : dto.Numerator,
                        Denominator = dto.Denominator <= 0 ? 1 : dto.Denominator,
                        BomSubType = dto.BomSubType,
                        MainImage = dto.MainImage,
                        Note = dto.Note,
                        State = ModelState.Unchanged
                    });
                }

                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<BomTreeDto> FlattenBomTree(IEnumerable<BomTreeDto> nodes)
        {
            if (nodes == null) yield break;

            foreach (var node in nodes)
            {
                if (node == null) continue;

                yield return node;

                if (node.Children == null || node.Children.Count == 0) continue;

                foreach (var child in FlattenBomTree(node.Children))
                {
                    yield return child;
                }
            }
        }

        private void ApplyBomFilters()
        {
            IEnumerable<BomItemModel> query = _currentAllBoms;
            if (!string.IsNullOrWhiteSpace(BomSearchText))
            {
                var keyword = BomSearchText.Trim();
                query = query.Where(x =>
                    Contains(x.SubSku, keyword) ||
                    Contains(x.Description, keyword) ||
                    Contains(x.BomSubType, keyword));
            }

            var snapshot = query.ToList();

            DispatcherProvider.Invoke(() =>
            {
                _isSyncingBomItems = true;
                try
                {
                    CurrentBomItems.Clear();
                    foreach (var item in snapshot) CurrentBomItems.Add(item);
                }
                finally
                {
                    _isSyncingBomItems = false;
                }

                BuildBomTreeCore();
            });
        }

        private void BuildBomTree()
        {
            DispatcherProvider.Invoke(BuildBomTreeCore);
        }

        /// <summary>
        /// 真正执行 BOM 树构建的核心逻辑。
        /// 调用方请确保已在 UI 线程（建议通过 DispatcherProvider.Invoke 调用）。
        /// </summary>
        private void BuildBomTreeCore()
        {
            BomTreeNodes.Clear();
            if (SelectedProduct == null) return;

            var root = new BomTreeNodeModel { ChildCode = SelectedProduct.Sku, ChildName = SelectedProduct.Description, QtyText = "1/1" };
            foreach (var item in CurrentBomItems)
            {
                root.Children.Add(new BomTreeNodeModel
                {
                    ChildCode = item.SubSku,
                    ChildName = item.Description,
                    QtyText = item.Numerator + "/" + item.Denominator
                });
            }
            BomTreeNodes.Add(root);
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrWhiteSpace(source) && source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddBomItem()
        {
            if (SelectedProduct == null) return;

            var nextId = _currentAllBoms.Count == 0 ? 1 : _currentAllBoms.Max(x => x.Id) + 1;
            var item = new BomItemModel
            {
                Id = nextId,
                SubSku = "NEW-ITEM-" + nextId,
                Description = "新子件",
                Numerator = 1,
                Denominator = 1,
                Note = string.Empty,
                MainImage = string.Empty,
                State = ModelState.Added
            };

            AddBomItemToCurrent(item);
            SaveBomToCache();
            ApplyBomFilters();
            EvaluateBomChanges();
            SelectedBomItem = item;
        }

        private void RemoveBomItem()
        {
            if (SelectedProduct == null || SelectedBomItem == null) return;

            SelectedBomItem.PropertyChanged -= BomItem_PropertyChanged;
            _currentAllBoms.Remove(SelectedBomItem);
            SaveBomToCache();
            ApplyBomFilters();
            EvaluateBomChanges();
            SelectedBomItem = null;
        }

        private void DeleteBomItem(BomItemModel row)
        {
            var target = row ?? SelectedBomItem;
            if (target == null) return;

            target.State = ModelState.Deleted;
            EvaluateBomChanges();
        }

        private void CancelDeleteBomItem(BomItemModel row)
        {
            var target = row ?? SelectedBomItem;
            if (target == null) return;

            // 中文注释：若该行原本是新增行，撤销删除后应回到 Added；否则回到 Unchanged。
            target.State = target.Id <= 0 ? ModelState.Added : ModelState.Unchanged;
            EvaluateBomChanges();
        }

        private async void SaveBomAsync()
        {

            if ( _bomApi == null) return;

            var dirtyModels = _currentAllBoms.Where(x => x.State == ModelState.Added || x.State == ModelState.Modified || x.State == ModelState.Deleted).ToList();
            if (!dirtyModels.Any())
            {
                await _msg.ShowInfoAsync("没有需要保存的变动，所有数据均已是最新状态。");
                return;
            }

            // 【修改点】：将校验范围从 dirtyModels 扩大到 _currentAllBoms
            // 这样无论数据是初次加载还是手动修改，只要列表里有不合法的数据（哪怕你没修改它），都会拦截并提示。
            var invalidModels = _currentAllBoms.Where(x => !string.IsNullOrEmpty(x.Error)).ToList();
            if (invalidModels.Any())
            {
                // 取第一条错误信息展示给用户
                var firstError = invalidModels.First();
                string errorMsg = "";
                if (!string.IsNullOrEmpty(firstError[nameof(firstError.BomNo)])) errorMsg = firstError[nameof(firstError.BomNo)];
                else if (!string.IsNullOrEmpty(firstError[nameof(firstError.SubSku)])) errorMsg = firstError[nameof(firstError.SubSku)];
                else if (!string.IsNullOrEmpty(firstError[nameof(firstError.Denominator)])) errorMsg = firstError[nameof(firstError.Denominator)];
                else if (!string.IsNullOrEmpty(firstError[nameof(firstError.Numerator)])) errorMsg = firstError[nameof(firstError.Numerator)];
                else if (!string.IsNullOrEmpty(firstError[nameof(firstError.BomSubType)])) errorMsg = firstError[nameof(firstError.BomSubType)];

                await _msg.ShowErrorAsync($"抱歉，列表内存在必填项未填写或格式错误的数据，请修正后再保存。\n提示信息：{errorMsg}");
                return;
            }

            StatusMessage = "正在保存...";

            var request = new BatchRequestGeneric<BomDto>
            {
                AddList = dirtyModels.Where(x => x.State == ModelState.Added).Select(x => x.Adapt<BomDto>()).ToList(),
                UpdateList = dirtyModels.Where(x => x.State == ModelState.Modified).Select(x => x.Adapt<BomDto>()).ToList(),
                DeleteIdList = dirtyModels.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList()
            };

            try
            {
                var rst = await _bomApi.BatchSaveAsync(request);
                if (rst.Success)
                {
                    // 中文注释：已删除项从当前列表移除，其余项恢复为未变更状态。
                    foreach (var item in dirtyModels.ToList())
                    {
                        if (item.State == ModelState.Deleted)
                        {
                            item.PropertyChanged -= BomItem_PropertyChanged;
                            _currentAllBoms.Remove(item);
                        }
                        else
                        {
                            item.State = ModelState.Unchanged;
                        }
                    }

                    HasBomChanges = false;
                    await _msg.ShowInfoAsync("保存成功！");

                    // 中文注释：保存成功后，以后端数据为准重新加载当前 BOM。
                    // 不再调用 LoadDataAsync()（那是主表查询，不会刷新 BOM 明细）。
                    if (SelectedProduct != null && !string.IsNullOrWhiteSpace(SelectedProduct.Sku))
                    {
                        _bomBySku.Remove(SelectedProduct.Sku);
                    }

                    CheckAndLoadBom();
                }
                else
                {
                    await _msg.ShowErrorAsync("保存失败：" + rst.Message);
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 保存当前 BOM 列表到内存缓存字典中，以便在“尚未点击保存但切换了产品”时恢复编辑状态。
        /// 注意：保存成功后会清理对应缓存并回源刷新，避免与后端状态不一致。
        /// </summary>
        private void SaveBomToCache()
        {
            if (SelectedProduct == null || string.IsNullOrWhiteSpace(SelectedProduct.Sku)) return;

            var target = new List<BomItemModel>();
            foreach (var item in _currentAllBoms)
            {
                target.Add(new BomItemModel
                {
                    Id = item.Id,
                    BomNo= item.BomNo,
                    SubSku = item.SubSku,
                    Description = item.Description,
                    Numerator = item.Numerator,
                    Denominator = item.Denominator,
                    BomSubType = item.BomSubType,
                    Note = item.Note,
                    MainImage = item.MainImage,
                    State = item.State
                });
            }
            _bomBySku[SelectedProduct.Sku] = target;
        }

        /// <summary>
        /// 打开产品选择器对话框，将用户确认的产品批量追加为当前 SKU 的 BOM 子件。
        /// 直接使用 ProductSelectorWindow（Dialog 模式），不再依赖 IProductSelectorService 间接层。
        /// </summary>
        private void ExecuteOpenProductSelector()
        {
            if (SelectedProduct == null) return;

            // 直接创建 ViewModel 和窗口实例（无需通过 DI / 服务抽象层）
            var vm = new ProductSelectorViewModel(_productApi);
            var window = new Views.ProductSelectorWindow(vm);
            var result = window.ShowDialog();

            // 用户确认后，将选中的产品批量追加为 BOM 子件
            if (result == true && window.SelectedProducts != null)
            {
                foreach (var p in window.SelectedProducts)
                {
                    // 避免重复添加同一子件
                    if (_currentAllBoms.Any(x => string.Equals(x.SubSku, p.Sku, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    AddBomItemToCurrent(new BomItemModel
                    {
                        Id = 0,
                        SubSku = p.Sku,
                        Description = p.Description,
                        Numerator = 1,
                        Denominator = 1,
                        Note = string.Empty,
                        MainImage = p.MainImage ?? string.Empty,
                        State = ModelState.Added
                    });
                }

                SaveBomToCache();
                ApplyBomFilters();
                EvaluateBomChanges();
                SelectedBomItem = null;
            }
        }

        /// <summary>
        /// 为没有BOM号的产品新建BOM：先调用 API 校验是否已存在同名 BOM，
        /// 若已存在则提示用户先删除旧 BOM，否则生成新 BOM 号并初始化空 BOM 列表。
        /// </summary>
        private async Task ExecuteCreateBomAsync()
        {
            if (SelectedProduct == null) return;

            // 以产品 Sku 作为 BOM 号
            var bomNo = SelectedProduct.Sku;

            try
            {
                // 调用后端校验该 BomNo 是否已在数据库中存在
                var result = await _bomApi.ValidateBomAsync(bomNo);
                if (result != null && result.Data)
                {
                    // BOM 已存在，提示用户先删除再新建
                    await _msg.ShowWarningAsync($"BOM号 [{bomNo}] 已在系统中存在，请先删除旧的BOM再新建。");
                    return;
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync($"校验BOM时发生异常：{ex.Message}");
                return;
            }

            // 设置 BOM 号到当前产品
            SelectedProduct.BomNo = bomNo;

            // 初始化本地空 BOM 列表
            _bomBySku[SelectedProduct.Sku] = new List<BomItemModel>();
            DetachBomItemHandlers();
            _currentAllBoms.Clear();
            CurrentBomItems.Clear();

            // 刷新 BOM 区域启用状态
            RaisePropertyChanged(nameof(HasActiveBom));
            RaiseBomCommandsCanExecuteChanged();
            EvaluateBomChanges();

            await _msg.ShowInfoAsync($"已为产品 [{SelectedProduct.Sku}] 创建新的BOM，请在下方添加子件并保存。");
        }

        // Removed old SearchBomSubSkuAsync and OnBomSubSkuItemSelected

        #endregion

        #region BOM区域的Popup
        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetProperty(ref _isPopupOpen, value);
        }
        private bool _isPopupOpen;

        /// <summary>
        /// 根据用户输入搜索可作为 BOM 子件的产品，并刷新共享 Popup 数据源。
        /// </summary>
        private async Task SearchPdForBomPopupAsync(string keyword)
        {
            if (_productApi == null) return;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                DispatcherProvider.Invoke(() =>
                {
                    BomPopSelectedItem = null;
                    BomPopItemSource.Clear();
                    IsPopupOpen = false;
                });
                return;
            }

            try
            {
                var productSearchDto = new ProductSearchDto
                {
                    Keyword = keyword,
                    PageIndex = 1,
                    PageSize = 20
                };
                var result = await _productApi.GetProductPagedListAsync(productSearchDto);

                DispatcherProvider.Invoke(() =>
                {
                    BomPopItemSource.Clear();
                    BomPopSelectedItem = null;

                    if (!result.Success || result.Data == null)
                    {
                        IsPopupOpen = false;
                        return;
                    }

                    foreach (var dto in result.Data.Items)
                    {
                        var model = dto.Adapt<ProductModel>();
                        model.State = ModelState.Unchanged;
                        BomPopItemSource.Add(model);
                    }

                    BomPopSelectedItem = BomPopItemSource.FirstOrDefault();
                    IsPopupOpen = BomPopSelectedItem != null;
                });
            }
            catch
            {
                DispatcherProvider.Invoke(() =>
                {
                    BomPopSelectedItem = null;
                    BomPopItemSource.Clear();
                    IsPopupOpen = false;
                });
            }
        }

        private void CommitPdForBomPopup(object rowObj)
        {
            DispatcherProvider.Invoke(() =>
            {
                var row = rowObj as BomItemModel ?? SelectedBomItem;
                if (row == null) return;

                var selectedProduct = BomPopSelectedItem;
                if (selectedProduct == null) return;
                row.BomNo = SelectedProduct.BomNo;
                row.SubSku = selectedProduct.Sku;
                row.Description = selectedProduct.Description;
                row.BomSubType = BomSubType.常规;
                row.Numerator = 1;
                row.Denominator = 1;
                row.Note = string.Empty;
                row.MainImage = selectedProduct.MainImage;

                SelectedBomItem = row;
                IsPopupOpen = false;
            });
        }

        #endregion

        /// <summary>
        /// 当 BOM 行字段被编辑时，同步刷新树结构与本地缓存。
        /// 这样无论是手工输入还是 Popup 回填，ViewModel 内部状态都会保持一致。
        /// </summary>
        private void BomItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isSyncingBomItems) return;
            if (e.PropertyName == nameof(IBaseModel.IsChecked)) return;

            EvaluateBomChanges();
            SaveBomToCache();

            // 中文注释：当行状态变化（例如删除/撤销删除）时，刷新树即可，无需走关键字过滤逻辑。
            if (e.PropertyName == nameof(BomItemModel.State))
            {
                BuildBomTree();
                return;
            }

            // 当筛选关键字生效且修改了参与筛选的字段时，重新套用过滤，保证当前表格与树保持一致。
            if (!string.IsNullOrWhiteSpace(BomSearchText) &&
                (e.PropertyName == nameof(BomItemModel.SubSku) ||
                 e.PropertyName == nameof(BomItemModel.Description) ||
                 e.PropertyName == nameof(BomItemModel.BomSubType)))
            {
                ApplyBomFilters();
                return;
            }

            BuildBomTree();
        }

        /// <summary>
        /// 解绑当前 BOM 集合中的行事件，避免重复订阅与内存泄漏。
        /// </summary>
        private void DetachBomItemHandlers()
        {
            foreach (var item in _currentAllBoms)
            {
                item.PropertyChanged -= BomItem_PropertyChanged;
            }
        }

        private void AddBomItemToCurrent(BomItemModel item)
        {
            if (item == null) return;

            item.PropertyChanged -= BomItem_PropertyChanged;
            item.PropertyChanged += BomItem_PropertyChanged;
            _currentAllBoms.Add(item);
        }

        /// <summary>
        /// 清空“其他搜索条件”（客户、颜色、模糊搜索），保留分类筛选。
        /// 并且只触发一次查询，避免重复 API 调用。
        /// </summary>
        private void ExecuteClearOtherSearchFilters()
        {
            if (IsBusy) return;

            using (BusyScope())
            {
                _isClearingQuickFilters = true;
                try
                {
                    SelectedCustomerFilter = "全部客户";
                    SelectedColorFilterText = string.Empty;
                    SelectedColorCodeUnique = string.Empty;
                    SearchText = string.Empty;
                }
                finally
                {
                    _isClearingQuickFilters = false;
                }

                // 中文注释：批量清空后仅触发一次查询。
                if (PageIndex != 1)
                {
                    PageIndex = 1;
                }
                else
                {
                    _ = LoadDataAsync();
                }
            }
        }
    }
}
