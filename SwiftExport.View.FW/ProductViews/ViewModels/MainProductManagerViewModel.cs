using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Models;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class MainProductManagerViewModel : ViewModelBaseEx
    {
        #region 主要字段与构造函数


        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBomAreaCollapsed;
        public bool IsBomAreaCollapsed
        {
            get => _isBomAreaCollapsed;
            set => SetProperty(ref _isBomAreaCollapsed, value);
        }

        private readonly IServiceProvider _serviceProvider;
        private readonly IProductApi _productApi;
        private readonly IBomApi _bomApi;
        private readonly IDictCacheService _dictCacheService;
        private readonly IDialogService _msg;
        public MainProductManagerViewModel(IProductApi productApi, IBomApi bomApi, IDictCacheService dictCacheService, IDialogService msg)
        {
            _productApi = productApi;
            _bomApi = bomApi;
            _dictCacheService = dictCacheService;
            _msg = msg;

            ProductList = new ProductList();
            BomList = new BomList();
            BomItemsView = CollectionViewSource.GetDefaultView(BomList.Items);

            //======产品命令初始化======
            AddProductCommand = new RelayCommand(AddProduct);

            BatchSaveProductAsyncCommand = new RelayCommand(async () => await BatchSaveProductAsync(), () => IsNotBusy && ProductList.IsDirty && ProductList.IsAllValid);
            ProductList.RegisterDirtyNotifyCommand(BatchSaveProductAsyncCommand);
            RegisterBusyNotifyCommand(BatchSaveProductAsyncCommand);

            DeleteProductAsyncCommand = new RelayCommand(async () => await DeleteProductAsync(), () => (ProductList.CheckedCount > 0 && IsNotBusy) || SelectedProduct != null);
            ProductList.RegisterCheckedNotifyCommand(DeleteProductAsyncCommand);
            RegisterBusyNotifyCommand(DeleteProductAsyncCommand);

            RefreshProductCommand = new RelayCommand(async () => await LoadProductAsync(), () => IsNotBusy);
            RegisterBusyNotifyCommand(RefreshProductCommand);

            InvertProductSelectionCommand = new RelayCommand(async () => InvertProductSelection(), () => ProductList.Items.Count > 0 && IsNotBusy);
            RegisterBusyNotifyCommand(InvertProductSelectionCommand);

            _产品搜索防抖计时器.Tick += 产品搜索防抖计时器_Tick;



            //======BOM命令初始化======
            AddBomItemCommand = new RelayCommand(AddBomItem, () => HasActiveBom && IsNotBusy);
            BatchSaveBomCommand = new RelayCommand(async () => await BatchSaveBomAsync(), () => HasActiveBom && IsNotBusy && BomList.IsDirty && BomList.IsAllValid);
            RemoveBomItemCommand = new RelayCommand(async () => await RemoveBomItemAsync(), () => HasActiveBom && IsNotBusy && (BomList.CheckedCount > 0 || SelectedBomItem != null));
            OpenProductSelectorCommand = new RelayCommand(OpenProductSelector, () => !IsBusy);
            CreateBomCommand = new RelayCommand(async () => await CreateBomAsync(), () => SelectedProduct != null && !IsBusy && SelectedProduct.SupplyType == SupplyType.按BOM拆分 && string.IsNullOrWhiteSpace(SelectedProduct.BomNo));




        }


        #endregion

        #region 左侧筛选区域

        private CustomerDto _filterCustomerDto;
        public CustomerDto FilterCustomerDto
        {
            get => _filterCustomerDto;
            set => SetProperty(ref _filterCustomerDto, value);
        }
        private ProductColorDto _filterColorDto;
        public ProductColorDto FilterColorDto
        {
            get => _filterColorDto;
            set => SetProperty(ref _filterColorDto, value);
        }
        private ProductCategoryDto _filterCategory;
        public ProductCategoryDto FilterCategory
        {
            get => _filterCategory;
            set => SetProperty(ref _filterCategory, value);
        }



        #endregion

        #region 右上区域

        #region 右上字段
        private readonly DispatcherTimer _产品搜索防抖计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        public ProductList ProductList { get; set; }

        private ProductModel _selectedProduct;
        public ProductModel SelectedProduct
        {
            get => _selectedProduct;
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
        public bool IsAllChecked
        {
            get => ProductList.IsAllChecked;
            set
            {
                if (ProductList.IsAllChecked == value)
                    return;
                ProductList.IsAllChecked = value;
                RaisePropertyChanged(nameof(IsAllChecked));
            }
        }
        private string _searchProductTxt;
        public string SearchProductTxt
        {
            get => _searchProductTxt;
            set
            {
                if (SetProperty(ref _searchProductTxt, value))
                {
                    产品防抖搜索开始();
                }
            }
        }
        private void 产品防抖搜索开始()
        {
            _产品搜索防抖计时器.Stop();
            _产品搜索防抖计时器.Start();
        }

        private void 产品搜索防抖计时器_Tick(object sender, EventArgs e)
        {
            _产品搜索防抖计时器.Stop();

            if (IsBusy) return;

            if (ProductList.IsDirty)
            {
                var result = System.Windows.MessageBox.Show("当前客户表有未保存的数据，搜索将会丢失，是否继续？", "未保存提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }
            // 如果已经在第1页，直接刷新
            if (PageIndex == 1) _ = LoadProductAsync();
            // 否则把页码设回1，setter 会自动触发 LoadProductAsync
            else PageIndex = 1;
        }


        #region 翻页

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex == value) return;
                if (SetProperty(ref _pageIndex, value))
                    _ = LoadProductAsync(); // 页码变化 → 加载数据
            }
        }

        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pageSize == value) return;
                if (SetProperty(ref _pageSize, value))
                {
                    // 换页大小时回第一页
                    if (PageIndex == 1) _ = LoadProductAsync();
                    else PageIndex = 1;
                }
            }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        // ================== 翻页命令 ==================

        public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(req =>
        {
            // ★ 拦截点：检查是否有未保存数据
            if (ProductList.IsDirty)
            {
                var ok = MessageBox.Show("当前有未保存修改，翻页将丢失，是否继续？",
                                         "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
                if (!ok) return; // 不执行 → UI 因 Mode=OneWay 自动保持原值
            }

            // 正常翻页：更新属性 → 触发 LoadProductAsync
            PageSize = req.NewSize;
            PageIndex = req.NewIndex;
        });
        #endregion

        #endregion


        #region 右上命令
        public RelayCommand AddProductCommand { get; }
        public RelayCommand BatchSaveProductAsyncCommand { get; }
        public RelayCommand DeleteProductAsyncCommand { get; }
        public RelayCommand RefreshProductCommand { get; }
        public RelayCommand InvertProductSelectionCommand { get; }

        #endregion

        #region 右上命令实现
        private void AddProduct()
        {
            if (ProductList == null) return;
            var model = new ProductModel
            {
                Sku = "NEW-PRODUCT-",
                Description = "新产品",
                State = ModelState.Added
            };
            ProductList.AddModelToFirst(model);
        }
        private async Task BatchSaveProductAsync()
        {
        }
        private async Task DeleteProductAsync()
        {
        }
        private async void InvertProductSelection() => ProductList.InvertCheckAll();
        private async Task LoadProductAsync()
        {
            // 做基本保护：如果API实例未注入，直接返回
            if (_productApi == null) return;

            using (BusyScope())
            {

                // 更新UI底部的文本状态提示
                StatusMessage = "正在加载数据...";
                try
                {
                    var cCode = string.Empty;
                    if (FilterCustomerDto != null && FilterCustomerDto.Name != "全部客户" && !string.IsNullOrWhiteSpace(FilterCustomerDto.Name)) { cCode = FilterCustomerDto.CustomerCode; }

                    // 构建用于提交给后端的查询条件 DTO
                    var search = new ProductSearchDto
                    {
                        // 获取搜索关键字
                        Keyword = SearchProductTxt,

                        //// 如果选中的是"全部客户"，则不传过滤条件
                        CustomerCodes = string.IsNullOrWhiteSpace(cCode) ? null : new List<string>() { cCode },

                        ColorCodeUnique = FilterColorDto != null && FilterColorDto.ColorNameZh != "全部颜色" && !string.IsNullOrWhiteSpace(FilterColorDto.ColorCode) ? FilterColorDto.ColorCode : null,

                        //// 按分类名筛选（支持包含子集）
                        CategoryId = FilterCategory != null && FilterCategory.Id > 0 ? FilterCategory.Id : 0,
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
                            ProductList.Clear();
                            ProductList.ReLoadRangeFromDtoList<ProductDto>(result.Data.Items);
                            TotalCount = result.Data.TotalCount;
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

        }
        #endregion

        #endregion

        #region 右下BOM
        private BomItemModel _selectedBomItem;
        public BomItemModel SelectedBomItem
        {
            get => _selectedBomItem;
            set => SetProperty(ref _selectedBomItem, value);
        }
        private string _searchBomTxt;
        public string SearchBomTxt
        {
            get => _searchBomTxt;
            set
            {
                if (SetProperty(ref _searchBomTxt, value))
                {
                    DebounceFilter(value); // 触发本地过滤，不发请求！
                }
            }
        }

        private CancellationTokenSource _debounceCts;

        private bool FilterItem(BomItemModel item, string searchText)
        {
            return (item.SubSku?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Description?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.BomNo?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Note?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // 中文注释：过滤刷新统一走线程安全调度，避免跨线程触发 ICollectionView 异常。
                DispatcherProvider.BeginInvoke(() =>
                {
                    BomItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((BomItemModel)obj, text);
                    BomItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        public bool HasActiveBom => SelectedProduct != null && SelectedProduct.SupplyType != SupplyType.直接采购 && !string.IsNullOrWhiteSpace(SelectedProduct.BomNo);

        public BomList BomList { get; set; } = new BomList();
        public ICollectionView BomItemsView { get; private set; }
        public RelayCommand AddBomItemCommand { get; private set; }// 添加BOM项的命令，绑定到BOM子表的添加按钮，当用户点击添加时会执行此命令在当前选中产品的BOM列表中插入一行新的空数据以供编辑
        public RelayCommand BatchSaveBomCommand { get; }
        public RelayCommand RemoveBomItemCommand { get; }
        public RelayCommand OpenProductSelectorCommand { get; }
        public RelayCommand CreateBomCommand { get; }
        /// <summary>
        /// 统一刷新所有 BOM 区域命令的 CanExecute 状态。
        /// 在选中产品切换、BOM 号变更等场景下调用。
        /// </summary>
        private void RaiseBomCommandsCanExecuteChanged()
        {
            AddBomItemCommand.RaiseCanExecuteChanged();
            BatchSaveBomCommand.RaiseCanExecuteChanged();
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

        private void AddBomItem()
        {
            if (SelectedProduct == null) return;

            var item = new BomItemModel
            {
                SubSku = "NEW-ITEM-",
                Description = "新子件",
                Numerator = 1,
                Denominator = 1,
                Note = string.Empty,
                MainImage = string.Empty,
                State = ModelState.Added
            };
            BomList.AddModelToFirst(item);

            DebounceFilter(SearchBomTxt);
            SelectedBomItem = item;
        }
        private async void LoadBomListAsync(ProductModel product)
        {
            if (_bomApi == null || product == null || string.IsNullOrWhiteSpace(product.BomNo) || product.SupplyType == SupplyType.直接采购) return;

            using (BusyScope())
            {
                try
                {
                    var result = await _bomApi.GetBomByBomNo(product.BomNo);
                    if (result == null || !result.Success || result.Data == null) return;
                    BomList.ReLoadRangeFromDtoList(result.Data);

                    return;
                }
                catch
                {
                    _msg.ShowCardAsync("加载BOM失败，请稍后重试, 错误");
                }
            }
        }

        private async Task BatchSaveBomAsync()
        {
        }
        private async Task RemoveBomItemAsync()
        {
        }
        private async void OpenProductSelector()
        {
        }
        private async Task CreateBomAsync()
        {
        }



        #endregion














    }
}
