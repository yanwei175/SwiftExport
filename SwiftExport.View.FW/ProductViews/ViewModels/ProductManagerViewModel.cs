using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Models;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductManagerViewModel : ViewModelBaseEx
    {
        #region 主要字段与构造函数


        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand ItemCheckedChangedCommand { get; }//只是为了xaml里不触发绑定警告

        private readonly IProductApi _productApi;
        private readonly IBomApi _bomApi;
        private readonly IProductColorApi _productColorApi;
        private readonly IProductCategoryApi _productCategoryApi;
        private readonly ICustomerApi _customerApi;
        private readonly ISupplierApi _supplierApi;
        private readonly IDictCacheService _dictCacheService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDialogService _msg;
        public ProductManagerViewModel(IProductApi productApi, IBomApi bomApi, IDictCacheService dictCacheService, IDialogService msg, IServiceProvider serviceProvider,
            IProductColorApi productColorApi, IProductCategoryApi productCategoryApi, ICustomerApi customerApi, ISupplierApi supplierApi)
        {

            _productApi = productApi;
            _productColorApi = productColorApi;
            _productCategoryApi = productCategoryApi;
            _customerApi = customerApi;
            _supplierApi = supplierApi;
            _bomApi = bomApi;
            _dictCacheService = dictCacheService;
            _msg = msg;
            _serviceProvider = serviceProvider;

            ProductList = new ProductList();
            BomList = new BomList();
            BomItemsView = CollectionViewSource.GetDefaultView(BomList.Items);

            //======产品命令初始化======
            QuickAddFinishCommand = new RelayCommand(ExecuteQuickAddFinish);
            QuickAddCategoryCommand = new RelayCommand(ExecuteQuickAddCategory);

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

            ResetFilterCommand = new RelayCommand(ResetFilter);

            _产品搜索防抖计时器.Tick += 产品搜索防抖计时器_Tick;

            //======BOM命令初始化======
            AddBomItemCommand = new RelayCommand(AddBomItem, () => !NeedCreatBom);

            BatchSaveBomAsyncCommand = new RelayCommand(async () => await BatchSaveBomAsync(), () => IsNotBusy && BomList.IsDirty && BomList.IsAllValid);
            BomList.RegisterDirtyNotifyCommand(BatchSaveBomAsyncCommand);
            RegisterBusyNotifyCommand(BatchSaveBomAsyncCommand);

            DeleteBomItemCommand = new RelayCommand(async () => await DeleteBomItemAsync(), () => (BomList.IsNotBusy && BomList.CheckedCount > 0) || SelectedBomItem != null);
            BomList.RegisterCheckedNotifyCommand(DeleteBomItemCommand);
            RegisterBusyNotifyCommand(DeleteBomItemCommand);

            RefreshBomListCommand = new RelayCommand(async () => await CheckAndLoadBom(), () => IsNotBusy);
            RegisterBusyNotifyCommand(RefreshBomListCommand);

            InvertBomListCommand = new RelayCommand(async () => InvertBomList(), () => BomList.Items.Count > 0 && IsNotBusy);
            RegisterBusyNotifyCommand(InvertBomListCommand);


            OpenProductSelectorCommand = new RelayCommand(OpenProductSelector, () => IsNotBusy);
            RegisterBusyNotifyCommand(OpenProductSelectorCommand);

            CreateBomCommand = new RelayCommand(async () => await CreateBomAsync(), () => NeedCreatBom && IsNotBusy);
            RegisterBusyNotifyCommand(CreateBomCommand);


            //BOM区域的POPUP
            SearchPdForBomPopupCommand = new RelayCommand<string>(async keyword => await SearchPdForBomPopupAsync(keyword));
            CommitPdForBomPopupCommand = new RelayCommand<object>(CommitPdForBomPopup);



            _ = LoadProductAsync();
            _ = LoadFilterDataAsync();
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

        private string _filterMaterialTypeSelected;
        public string FilterMaterialTypeSelected
        {
            get => _filterMaterialTypeSelected;
            set => SetProperty(ref _filterMaterialTypeSelected, value);
        }

        private string _filterCategorySearchText;
        public string FilterCategorySearchText
        {
            get => _filterCategorySearchText;
            set => SetProperty(ref _filterCategorySearchText, value);
        }



        #endregion

        #region 产品区域

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
                    // 中文注释：在 CellOrRowHeader 模式下，DataGrid 会出现瞬时 null 抖动。
                    // 若这里直接触发 CheckAndLoadBom()，会清空并中断正在返回的 BOM 数据。
                    // 因此 null 场景仅刷新状态，不执行 BOM 重载。
                    if (value == null)
                    {
                        return;
                    }

                    _ = CheckAndLoadBom();
                    DeleteProductAsyncCommand.RaiseCanExecuteChanged();
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

        private async void 产品搜索防抖计时器_Tick(object sender, EventArgs e)
        {
            _产品搜索防抖计时器.Stop();

            if (IsBusy) return;

            if (ProductList.IsDirty)
            {
                var rst = await _msg.ShowConfirmAsync("当前客户表有未保存的数据，搜索将会丢失，是否继续？", "未保存提示");
                if (!rst)
                {
                    return;
                }
            }
            // 如果已经在第1页，直接刷新
            if (PageIndex == 1) _ = LoadProductAsync();
            // 否则把页码设回1，setter 会自动触发 LoadProductAsync
            else PageIndex = 1;
        }

        public ObservableCollection<ProductColorDto> ProductFinishList { get; private set; } = new ObservableCollection<ProductColorDto>();// 产品表面处理的下拉列表数据集合
        public ObservableCollection<ProductCategoryDto> ProductCategoryList { get; private set; } = new ObservableCollection<ProductCategoryDto>();// 产品分类的下拉列表数据集合 
        public ObservableCollection<CustomerDto> CustomerList { get; private set; } = new ObservableCollection<CustomerDto>();// 客户编码的下拉列表数据集合
        public ObservableCollection<SupplierDto> SupplierList { get; private set; } = new ObservableCollection<SupplierDto>();// 供应商编码的下拉列表数据集合
        public ObservableCollection<string> SupplyTypeList { get; private set; } = new ObservableCollection<string>();// 供应类型的下拉列表数据集合
        public ObservableCollection<string> MaterialTypeList { get; private set; } = new ObservableCollection<string>();//物料类型
        public ObservableCollection<string> FilterMaterialTypeList { get; private set; } = new ObservableCollection<string>();

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

        public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(async req =>
        {
            // ★ 拦截点：检查是否有未保存数据
            if (ProductList.IsDirty)
            {
                var ok = await _msg.ShowConfirmAsync("当前有未保存修改，翻页将丢失，是否继续？", "提示");
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
        public RelayCommand ResetFilterCommand { get; }
        public RelayCommand QuickAddFinishCommand { get; private set; }// 快速添加表面处理的命令，绑定到UI的快速添加表面处理按钮，当用户点击时会打开一个新的窗口用于管理表面处理选项，窗口关闭后会刷新表面处理列表以反映新增或修改的选项
        public RelayCommand QuickAddCategoryCommand { get; private set; }// 快速添加分类的命令，绑定到UI的快速添加分类按钮，当用户点击时会打开一个新的窗口用于管理产品分类，窗口关闭后会刷新分类树和相关列表以反映新增或修改的分类选项


        #endregion

        #region 右上命令实现
        private void AddProduct()
        {
            if (ProductList == null) return;
            var model = new ProductModel
            {
                Sku = "NEW-PRODUCT-",
                DescriptionEn = "新产品",
                State = ModelState.Added
            };
            ProductList.AddModelToFirst(model);
        }
        private async Task BatchSaveProductAsync()
        {
            var dirtyModels = ProductList.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (ProductList.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<ProductDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _productApi.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";
                    _msg.ShowCardAsync("保存成功！");
                    _ = LoadProductAsync();
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    _msg.ShowCardAsync(StatusMessage);
                    return;
                }
            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("异常: " + ex.Message);
            }
        }
        private async Task DeleteProductAsync()
        {
            var checkItems = ProductList.GetCheckedItems();
            List<ProductModel> waitToDelete = new List<ProductModel>();

            // 情况 1：没有勾选任何行
            if (checkItems.Count == 0)
            {
                if (SelectedBomItem == null)
                    return; // ★ 没有勾选也没有选中 → 直接退出

                if (SelectedBomItem.Id <= 0)
                {
                    // 删除新建行（本地删除）
                    var tempIndex = ProductList.Items.IndexOf(SelectedProduct);
                    ProductList.Remove(SelectedProduct);

                    try
                    {
                        SelectedProduct = ProductList.Items[tempIndex];
                    }
                    catch
                    {
                        SelectedProduct = ProductList.Items.FirstOrDefault();
                    }

                    return; // ★ 新建行删除不需要 API
                }

                // ★ 有选中行（且 Id > 0）→ 加入待删除列表
                waitToDelete.Add(SelectedProduct);
            }
            else
            {
                // 情况 2：有勾选行 → 全部加入待删除列表
                waitToDelete = checkItems;
            }

            // ★★★ 关键：这里不 return，继续走 API 删除流程

            StatusMessage = $"准备删除 {waitToDelete.Count} 条记录...";

            try
            {
                if (waitToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync(
                        $"确定要删除选中的 {waitToDelete.Count} 条记录吗？", "确认删除"))
                        return;

                    var req = new BatchRequestGeneric<ProductDto>
                    {
                        DeleteIdList = waitToDelete.Select(x => x.Id).ToList()
                    };

                    var rst = await _productApi.BatchSaveAsync(req);

                    if (rst.Success)
                    {
                        await LoadProductAsync();
                        _msg.ShowCardAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + rst.Message);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("删除异常：" + ex.Message);
            }
            finally
            {
                StatusMessage = "准备就绪";
            }
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
                    if (FilterCustomerDto != null && FilterCustomerDto.CustomerName != "全部客户" && !string.IsNullOrWhiteSpace(FilterCustomerDto.CustomerName)) { cCode = FilterCustomerDto.CustomerCode; }

                    // 构建用于提交给后端的查询条件 DTO
                    var search = new ProductSearchDto
                    {
                        // 获取搜索关键字
                        Keyword = SearchProductTxt,

                        //// 如果选中的是"全部客户"，则不传过滤条件
                        CustomerCodes = string.IsNullOrWhiteSpace(cCode) ? null : new List<string>() { cCode },

                        ColorCodeUnique = FilterColorDto != null && FilterColorDto.ColorNameZh != "全部颜色" && !string.IsNullOrWhiteSpace(FilterColorDto.ColorCode) ? FilterColorDto.ColorCode : null,
                        MaterialType = FilterMaterialTypeSelected != null ? FilterMaterialTypeSelected : null,
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
                            BomList.Clear();
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

        private async void ExecuteQuickAddFinish()
        {
            var vm = (ProductColorManagerViewModel)_serviceProvider.GetService(typeof(ProductColorManagerViewModel));
            var view = new Views.ProductColorManagerView(vm);
            var win = new System.Windows.Window
            {
                Title = "产品颜色管理",
                Content = view,
                Width = 1024,
                Height = 768,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            };
            win.ShowDialog();
            // 窗体关闭后重新拉取字典等数据以刷新下拉列表
            await LoadFilterDataAsync();
        }
        //快速添加分类的命令响应方法，打开分类管理窗口，窗口关闭后刷新分类树和相关列表以反映变动
        private async void ExecuteQuickAddCategory()
        {
            var vm = (ProductCategoryManagerViewModel)_serviceProvider.GetService(typeof(ProductCategoryManagerViewModel));
            // ProductCategoryManagerView 已改为 UserControl（用于 DataTemplate 路由），
            // 此处用匿名 Window 包裹，与 ProductColorManagerView 保持一致的对话框打开方式。
            var view = new Views.ProductCategoryManagerView { DataContext = vm };
            var win = new System.Windows.Window
            {
                Title = "产品分类管理",
                Content = view,
                Width = 900,
                Height = 600,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            };
            win.ShowDialog();
            // 窗体关闭后重新拉取字典/分类等数据刷新树和下拉列表
            await LoadFilterDataAsync();
        }

        private void ResetFilter()
        {
            using (BusyScope())
            {
                FilterMaterialTypeSelected = null;
                FilterCategory = null;
                FilterCategorySearchText = string.Empty;
                FilterColorDto = null;
                FilterCustomerDto = null;
                SearchProductTxt = null;
            }

        }


        #region 字典与下拉数据加载 (Dictionary & Lookups)

        private async Task LoadFilterDataAsync()
        {

            var task1 = _productColorApi.GetAllAsync();
            var task2 = _customerApi.GetAllAsync();
            var task3 = _productCategoryApi.GetAllAsync();
            await Task.WhenAll(task1, task2, task3);

            ProductFinishList.Refresh(task1.Result.Data);
            CustomerList.Refresh(task2.Result.Data);
            ProductCategoryList.Refresh(task3.Result.Data);
            // 供应类型这种固定的，可以直接初始化
            if (SupplyTypeList.Count == 0)
            {
                SupplyTypeList.Refresh(SupplyTypeEnums.SupplyTypes);
            }
            if (MaterialTypeList.Count == 0)
            {
                MaterialTypeList.Refresh(MaterialTypeEnums.MaterialTypes);
            }
            if (FilterMaterialTypeList.Count == 0) { FilterMaterialTypeList.Refresh(MaterialTypeEnums.MaterialTypes); }
        }




        #endregion



        #endregion

        #endregion

        #region BOM区域

        #region BOM字段
        private BomItemModel _selectedBomItem;
        public BomItemModel SelectedBomItem
        {
            get => _selectedBomItem;
            set
            {
                if (SetProperty(ref _selectedBomItem, value))
                {
                    DeleteBomItemCommand.RaiseCanExecuteChanged();
                    if (value == null)
                    {
                        return;
                    }
                }

            }
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

        private bool _needCreatBom;
        public bool NeedCreatBom
        {
            get => _needCreatBom;
            set
            {
                if (SetProperty(ref _needCreatBom, value))
                {
                    CreateBomCommand.RaiseCanExecuteChanged();
                }
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
                    // 当用户展开区域时，如果当前有选中的产品，立即加载数据
                    if (!_isBomAreaCollapsed)
                    {
                        _ = CheckAndLoadBom();
                    }
                }
            }
        }




        #region BOM区域的Popup
        public ObservableCollection<ProductModel> BomPopItemSource { get; private set; } = new ObservableCollection<ProductModel>();// BOM条目中用于搜索产品的下拉列表数据集合

        private ProductModel _bomPopSelectedItem;
        public ProductModel BomPopSelectedItem
        {
            get => _bomPopSelectedItem;
            set => SetProperty(ref _bomPopSelectedItem, value);
        }

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
            DispatcherProvider.BeginInvoke(() =>
            {
                var row = rowObj as BomItemModel ?? SelectedBomItem;

                if (row == null) return;

                var selectedProduct = BomPopSelectedItem;

                if (selectedProduct == null) return;

                row.BomNo = SelectedProduct.BomNo;
                row.SubSku = selectedProduct.Sku;
                row.Description = selectedProduct.DescriptionEn;
                row.Numerator = 1;
                row.Denominator = 1;
                row.Note = string.Empty;
                row.MainImage = selectedProduct.MainImage;

                SelectedBomItem = row;
                IsPopupOpen = false;
            });
        }

        /// <summary>
        /// 打开产品选择器对话框，将用户确认的产品批量追加为当前 SKU 的 BOM 子件。
        /// 直接使用 ProductSelectorWindow（Dialog 模式），不再依赖 IProductSelectorService 间接层。
        /// </summary>
        private void OpenProductSelector()
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
                    // 1. 检查全局池（不仅仅是 Items）
                    var existing = BomList.GetFromAll(x => string.Equals(x.SubSku, p.Sku, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        // 如果已经在 UI 里显示了，直接跳过
                        if (BomList.Items.Contains(existing)) continue;

                        // 如果在全集里有，但 UI 里没有，说明是被“删除”了
                        if (existing.State == ModelState.Deleted)
                        {
                            // 撤销删除：这会让 State 变回有效，并触发列表的 Dirty 状态更新
                            existing.RestoreFromDeleted();
                            // 重新插入 UI 列表
                            BomList.AddModelToFirst(existing);
                        }
                        continue;
                    }


                    BomList.AddModelToFirst(new BomItemModel
                    {
                        Id = 0,
                        SubSku = p.Sku,
                        Description = p.DescriptionZh,
                        Numerator = 1,
                        Denominator = 1,
                        Note = string.Empty,
                        MainImage = p.MainImage ?? string.Empty,
                        State = ModelState.Added
                    });
                }
                SelectedBomItem = null;
            }
        }

        #endregion





        #endregion


        #region BOM命令
        public BomList BomList { get; set; } = new BomList();
        public ICollectionView BomItemsView { get; private set; }

        public RelayCommand AddBomItemCommand { get; private set; }// 添加BOM项的命令，绑定到BOM子表的添加按钮，当用户点击添加时会执行此命令在当前选中产品的BOM列表中插入一行新的空数据以供编辑
        public RelayCommand BatchSaveBomAsyncCommand { get; }
        public RelayCommand DeleteBomItemCommand { get; }
        public RelayCommand CreateBomCommand { get; }

        public RelayCommand RefreshBomListCommand { get; }
        public RelayCommand InvertBomListCommand { get; }

        public RelayCommand<string> SearchPdForBomPopupCommand { get; private set; }//Bom条目中搜索产品的命令，绑定到BOM编辑界面中用于选择产品的搜索框，当用户输入搜索关键字并执行搜索时会触发此命令调用API获取匹配的产品列表以供用户选择添加到BOM中
        public RelayCommand<object> CommitPdForBomPopupCommand { get; private set; }//Bom条目中提交选择的产品的命令，绑定到BOM编辑界面中用于选择产品的确认按钮，当用户选中一个产品并点击确认时会触发此命令將選中的產品信息添加到當前BOM列表中作為新的BOM項
        public RelayCommand OpenProductSelectorCommand { get; private set; }// 打开产品选择器窗口，用户选中产品后批量追加到当前BOM列表




        //public RelayCommand ToggleBomAreaCommand { get; } = new RelayCommand(() => IsBomAreaCollapsed = !IsBomAreaCollapsed);
        /// <summary>
        /// 统一刷新所有 BOM 区域命令的 CanExecute 状态。
        /// 在选中产品切换、BOM 号变更等场景下调用。
        /// </summary>
        private void RaiseBomCommandsCanExecuteChanged()
        {
            AddBomItemCommand.RaiseCanExecuteChanged();
            BatchSaveBomAsyncCommand.RaiseCanExecuteChanged();
            DeleteBomItemCommand.RaiseCanExecuteChanged();
            OpenProductSelectorCommand.RaiseCanExecuteChanged();
            CreateBomCommand.RaiseCanExecuteChanged();
        }

        private async Task CheckAndLoadBom()
        {
            // 中文注释：BOM 区域折叠时跳过加载，避免向后端发出无效请求。
            if (!IsBomAreaCollapsed) return;
            await LoadBomListAsync(SelectedProduct);
        }
        private async Task LoadBomListAsync(ProductModel product)
        {
            BomList.Clear();
            NeedCreatBom = false;

            // 1. 初步预判 (同步逻辑)
            if (product == null || product.SupplyType == SupplyTypeEnums.直接采购)
            {
                NeedCreatBom = false;
                return;
            }

            // 如果没有 Bom 号，直接判定为需要新建
            if (string.IsNullOrWhiteSpace(product.BomNo))
            {
                NeedCreatBom = true;
                return;
            }

            if (_bomApi == null) return;

            using (BusyScope())
            {

                try
                {
                    // 默认先设为 false，等查出来没有再说
                    NeedCreatBom = false;

                    var result = await _bomApi.GetBomByBomNo(product.BomNo);
                    if (result == null || !result.Success || result.Data == null)
                    {
                        // 情况 C1：有号但库里没数据 (可能被人删了或录错了)
                        _msg.ShowCardAsync("不存在的Bom号");
                        NeedCreatBom = true;
                    }
                    else
                    {
                        // 情况 C2：正常加载
                        BomList.ReLoadRangeFromDtoList(result.Data);
                        NeedCreatBom = false;// 查到了，不需要新建

                    }
                }
                catch
                {
                    _msg.ShowCardAsync("加载BOM失败，请稍后重试, 错误");
                    NeedCreatBom = false;
                }
            }
        }

        private void AddBomItem()
        {
            //if (SelectedBomItem == null) return;
            if (SelectedProduct == null) return;
            if (SelectedProduct.SupplyType == SupplyTypeEnums.直接采购) return;
            if (SelectedProduct.BomNo == null) return;

            var item = new BomItemModel
            {
                BomNo = SelectedProduct.BomNo,
                SubSku = "NEW-ITEM-",
                Description = "新子件",
                Numerator = 1,
                Denominator = 1,
                Note = string.Empty,
                MainImage = string.Empty,
                State = ModelState.Added
            };

            BomList.AddModelToFirst(item);
            RaiseBomCommandsCanExecuteChanged();

            SelectedBomItem = item;
        }

        private async Task BatchSaveBomAsync()
        {
            var dirtyModels = BomList.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (BomList.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<BomDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _bomApi.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";
                    _msg.ShowCardAsync("保存成功！");

                    await CheckAndLoadBom(); // 保存后刷新当前产品的BOM列表
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    _msg.ShowCardAsync(StatusMessage);
                    return;
                }
            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("异常: " + ex.Message);
            }

        }
        private async Task DeleteBomItemAsync()
        {
            var checkItems = BomList.GetCheckedItems();
            List<BomItemModel> waitToDelete = new List<BomItemModel>();

            // 情况 1：没有勾选任何行
            if (checkItems.Count == 0)
            {
                if (SelectedBomItem == null)
                    return; // ★ 没有勾选也没有选中 → 直接退出

                if (SelectedBomItem.Id <= 0)
                {
                    // 删除新建行（本地删除）
                    var tempIndex = BomList.Items.IndexOf(SelectedBomItem);
                    BomList.Remove(SelectedBomItem);
                    try
                    {
                        SelectedBomItem = BomList.Items[tempIndex];
                    }
                    catch
                    {
                        SelectedBomItem = BomList.Items.FirstOrDefault();
                    }

                    return; // ★ 新建行删除不需要 API
                }

                // ★ 有选中行（且 Id > 0）→ 加入待删除列表
                waitToDelete.Add(SelectedBomItem);
            }
            else
            {
                // 情况 2：有勾选行 → 全部加入待删除列表
                waitToDelete = checkItems;
            }

            // ★★★ 关键：这里不 return，继续走 API 删除流程

            StatusMessage = $"准备删除 {waitToDelete.Count} 条记录...";

            try
            {
                if (waitToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync(
                        $"确定要删除选中的 {waitToDelete.Count} 条记录吗？", "确认删除"))
                        return;

                    var req = new BatchRequestGeneric<BomDto>
                    {
                        DeleteIdList = waitToDelete.Select(x => x.Id).ToList()
                    };

                    var rst = await _bomApi.BatchSaveAsync(req);

                    if (rst.Success)
                    {
                        await CheckAndLoadBom();
                        _msg.ShowCardAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + rst.Message);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("删除异常：" + ex.Message);
            }
            finally
            {
                StatusMessage = "准备就绪";
            }

        }
        /// <summary>
        /// 为没有BOM号的产品新建BOM：先调用 API 校验是否已存在同名 BOM，
        /// 若已存在则提示用户先删除旧 BOM，否则生成新 BOM 号并初始化空 BOM 列表。
        /// </summary>
        private async Task CreateBomAsync()
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
            BomList.Items.Clear();
            var newBomItem = new BomItemModel
            {
                BomNo = bomNo,
            };
            BomList.AddModelToFirst(newBomItem);

            // 刷新 BOM 区域启用状态

            RaiseBomCommandsCanExecuteChanged();

            await _msg.ShowInfoAsync($"已为产品 [{SelectedProduct.Sku}] 创建新的BOM，请在下方添加子件并保存。");
        }

        private void AddBomEmptyBomItem()
        {

        }
        private void InvertBomList() => BomList.InvertCheckAll();

        #endregion

        #endregion














    }
}
