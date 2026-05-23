using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Models;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.CustomerViews.Models;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.CustomerViews.ViewModels
{
    /// <summary>
    /// 客户产品管理子视图 ViewModel。
    /// 负责管理指定客户的产品列表（增删改查 + 分页 + 搜索），
    /// 供 CustomerManagerView 的"客户产品管理"TabItem 嵌入使用。
    /// </summary>
    /// 
    public class CustomerProductManagerViewModel : ViewModelBaseEx
    {
        #region 构造函数与字段

        private readonly ICustomerProductApi _customerProductApi;
        private readonly IProductApi _productApi;
        private readonly IDialogService _msg;

        // ===================== 预留：SKU联动逻辑（当前按需求禁用） =====================
        // 为了性能与交互稳定性，当前版本仅在“行模式”通过 Popup 选择时写入关联字段，
        // 单元格模式下不做自动联动。未来若要恢复，可启用文件底部的“预留联动逻辑”。
        // private readonly Dictionary<string, ProductModel> _skuProductCache = new Dictionary<string, ProductModel>(StringComparer.OrdinalIgnoreCase);
        // private readonly HashSet<CustomerProductModel> _linkingRows = new HashSet<CustomerProductModel>();

        /// <summary>
        /// 搜索框防抖计时器，停止输入 400ms 后才触发搜索请求。
        /// </summary>
        private readonly DispatcherTimer _搜索防抖计时器 = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        public CustomerProductManagerViewModel(ICustomerProductApi customerProductApi, IProductApi productApi, IDialogService msg)
        {
            _customerProductApi = customerProductApi;
            _productApi = productApi;
            _msg = msg;

            ProductList = new CustomerProductList();

            // 预留：单元格模式联动（当前按需求禁用）
            // ProductList.Items.CollectionChanged += OrderLineItems_CollectionChanged;
            // AttachItemHandlers(ProductList.Items);

            // ====== 命令初始化 ======
            AddProductCommand = new RelayCommand(AddProduct, () => CurrentCustomer != null && IsNotBusy);
            RegisterBusyNotifyCommand(AddProductCommand);

            BatchSaveProductAsyncCommand = new RelayCommand(
                async () => await BatchSaveProductAsync(),
                () => IsNotBusy && ProductList.IsDirty && ProductList.IsAllValid);
            ProductList.RegisterDirtyNotifyCommand(BatchSaveProductAsyncCommand);
            RegisterBusyNotifyCommand(BatchSaveProductAsyncCommand);

            DeleteProductAsyncCommand = new RelayCommand(
                async () => await DeleteProductAsync(),
                () => (ProductList.CheckedCount > 0 || SelectedProduct != null) && IsNotBusy);
            ProductList.RegisterCheckedNotifyCommand(DeleteProductAsyncCommand);
            RegisterBusyNotifyCommand(DeleteProductAsyncCommand);

            RefreshProductCommand = new RelayCommand(
                async () => await LoadProductAsync(),
                () => CurrentCustomer != null && IsNotBusy);
            RegisterBusyNotifyCommand(RefreshProductCommand);

            InvertProductSelectionCommand = new RelayCommand(
                () => ProductList.InvertCheckAll());

            //BOM区域的POPUP
            SearchPdForBomPopupCommand = new RelayCommand<string>(async keyword => await SearchPdForPopupAsync(keyword));
            CommitPdForBomPopupCommand = new RelayCommand<object>(CommitPdForPopup);

            // 防抖计时器触发时执行搜索
            _搜索防抖计时器.Tick += 搜索防抖计时器_Tick;
        }

        #endregion

        #region 属性

        /// <summary>当前操作状态提示，绑定到底部状态栏。</summary>
        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 当前关联的客户 Id，由父 ViewModel（CustomerManagerViewModel）注入。
        /// 切换客户时自动清空并重新加载产品列表。
        /// </summary>
        private CustomerModel _currentCustomer;
        public CustomerModel CurrentCustomer
        {
            get => _currentCustomer;
            set
            {
                if (_currentCustomer == value) return;
                _currentCustomer = value;
                RaisePropertyChanged(nameof(CurrentCustomer));

                // 客户切换时重置分页并清空列表
                ProductList.Clear();
                TotalCount = 0;
                PageIndex = 1;

                // 刷新依赖 CustomerId 的命令可用性
                AddProductCommand.RaiseCanExecuteChanged();
                RefreshProductCommand.RaiseCanExecuteChanged();

                // 有效客户时自动加载
                if (value != null)
                    _ = LoadProductAsync();
            }
        }

        /// <summary>产品数据集合，绑定到 DataGrid。</summary>
        public CustomerProductList ProductList { get; }

        /// <summary>当前选中行。</summary>
        private CustomerProductModel _selectedProduct;
        public CustomerProductModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                {
                    // 选中行变化时刷新删除按钮可用性
                    DeleteProductAsyncCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>全选状态，与 DataGrid CheckBox 列双向绑定。</summary>
        public bool IsAllChecked
        {
            get => ProductList.IsAllChecked;
            set
            {
                if (ProductList.IsAllChecked == value) return;
                ProductList.IsAllChecked = value;
                RaisePropertyChanged(nameof(IsAllChecked));
            }
        }

        /// <summary>搜索关键字，带防抖触发。</summary>
        private string _searchProductTxt;
        public string SearchProductTxt
        {
            get => _searchProductTxt;
            set
            {
                if (SetProperty(ref _searchProductTxt, value))
                    防抖搜索开始();
            }
        }

        #endregion

        #region 分页

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex == value) return;
                if (SetProperty(ref _pageIndex, value))
                    _ = LoadProductAsync();
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
                    // 页大小变化时回到第一页
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

        /// <summary>
        /// 翻页命令，拦截脏数据二次确认。
        /// </summary>
        public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(async req =>
        {
            if (ProductList.IsDirty)
            {
                var ok = await _msg.ShowConfirmAsync(
                    "当前有未保存修改，翻页将丢失，是否继续？",
                    "提示");
                if (!ok) return;
            }
            using (BusyScope())
            {
                PageSize = req.NewSize;
                PageIndex = req.NewIndex;
            }
        });

        #endregion

        #region 命令

        public RelayCommand AddProductCommand { get; }
        public RelayCommand BatchSaveProductAsyncCommand { get; }
        public RelayCommand DeleteProductAsyncCommand { get; }
        public RelayCommand RefreshProductCommand { get; }
        public RelayCommand InvertProductSelectionCommand { get; }
        public RelayCommand<string> SearchPdForBomPopupCommand { get; }
        public RelayCommand<object> CommitPdForBomPopupCommand { get; }

        #endregion

        #region 命令实现

        /// <summary>新增一行客户产品，预填客户 Id 和默认值。</summary>
        private void AddProduct()
        {
            if (ProductList == null) return;
            var model = new CustomerProductModel
            {
                CustomerId = CurrentCustomer.Id,
                CustomerCode = CurrentCustomer.CustomerCode,
                CustomerSku = "NEW-",
                IsActive= true
            };
            model.MarkAsAdded();
            ProductList.AddModelToFirst(model);
        }

        /// <summary>批量保存脏数据（新增 / 修改）。</summary>
        private async Task BatchSaveProductAsync()
        {
            var dirtyModels = ProductList.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (!ProductList.IsAllValid)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }

            StatusMessage = "正在保存更改...";
            var req = dirtyModels.Adapt<BatchRequestGeneric<CustomerProductDto>>();
            try
            {
                var result = await _customerProductApi.BatchSaveAsync(req);
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
                }
            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 删除选中产品。
        /// 未持久化的新建行本地直接移除；已持久化行走 API 删除。
        /// </summary>
        private async Task DeleteProductAsync()
        {
            var checkItems = ProductList.GetCheckedItems();
            var waitToDelete = new List<CustomerProductModel>();

            // 情况 1：没有勾选行，依赖当前选中行
            if (checkItems.Count == 0)
            {
                if (SelectedProduct == null) return;

                if (SelectedProduct.Id <= 0)
                {
                    // 新建行直接从本地集合移除
                    var tempIndex = ProductList.Items.IndexOf(SelectedProduct);
                    ProductList.Remove(SelectedProduct);
                    try { SelectedProduct = ProductList.Items[tempIndex]; }
                    catch { SelectedProduct = ProductList.Items.FirstOrDefault(); }
                    return;
                }

                waitToDelete.Add(SelectedProduct);
            }
            else
            {
                // 情况 2：有勾选行，全部加入待删除列表
                waitToDelete = checkItems;
            }

            StatusMessage = string.Format("准备删除 {0} 条记录...", waitToDelete.Count);
            try
            {
                if (!await _msg.ShowConfirmAsync(
                    string.Format("确定要删除选中的 {0} 条记录吗？", waitToDelete.Count), "确认删除"))
                    return;

                var req = new BatchRequestGeneric<CustomerProductDto>
                {
                    DeleteIdList = waitToDelete.Select(x => x.Id).ToList()
                };

                var rst = await _customerProductApi.BatchSaveAsync(req);
                if (rst.Success)
                {
                    await LoadProductAsync();
                    _msg.ShowCardAsync("删除成功！");
                }
                else
                {
                    await _msg.ShowErrorAsync("删除失败：" + rst.Message);
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

        /// <summary>从后端分页加载当前客户的产品列表。</summary>
        private async Task LoadProductAsync()
        {
            if (_customerProductApi == null || CurrentCustomer == null) return;

            using (BusyScope())
            {

                try
                {
                    var search = new ProductSearchDto
                    {
                        CustomerId = CurrentCustomer.Id,
                        PageIndex = PageIndex,
                        PageSize = PageSize,
                        BatchKeywords = SearchProductTxt?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(k => k.Trim())
                                        .Where(k => !string.IsNullOrEmpty(k))
                                        .ToList() // 装载批量精准查询参数
                    };

                    _msg.ShowCardAsync("正在加载数据...");
                    ProductList.Clear();

                    var result = await _customerProductApi.GetCustomerProductPagedListAsync(search);

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (result.Success && result.Data != null)
                        {

                            ProductList.ReLoadRangeFromDtoList<CustomerProductDto>(result.Data.Items);
                            TotalCount = result.Data.TotalCount;
                            StatusMessage = string.Format("加载成功，共 {0} 条。", TotalCount);

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

        #region 防抖搜索

        private void 防抖搜索开始()
        {
            _搜索防抖计时器.Stop();
            _搜索防抖计时器.Start();
        }

        private void 搜索防抖计时器_Tick(object sender, EventArgs e)
        {
            _搜索防抖计时器.Stop();
            if (IsBusy) return;

            if (ProductList.IsDirty)
            {
                var result = MessageBox.Show(
                    "当前有未保存的数据，搜索将会丢失，是否继续？",
                    "未保存提示", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            // 若已在第 1 页，直接刷新；否则重置页码（setter 会自动触发加载）
            if (PageIndex == 1) _ = LoadProductAsync();
            else PageIndex = 1;
        }

        #endregion

        #region Popup区域
        public ObservableCollection<ProductModel> PopItemSource { get; private set; } = new ObservableCollection<ProductModel>();// BOM条目中用于搜索产品的下拉列表数据集合

        private ProductModel _popSelectedItem;
        public ProductModel PopSelectedItem
        {
            get => _popSelectedItem;
            set => SetProperty(ref _popSelectedItem, value);
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
        private async Task SearchPdForPopupAsync(string keyword)
        {
            using (BusyScope())
            {
                if (_productApi == null) return;

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    DispatcherProvider.Invoke(() =>
                    {
                        PopSelectedItem = null;
                        PopItemSource.Clear();
                        IsPopupOpen = false;
                    });
                    return;
                }

                try
                {
                    var productSearchDto = new ProductSearchDto
                    {
                       
                        PageIndex = 1,
                        PageSize = 20,
                        BatchKeywords = keyword?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToList() // 装载批量精准查询参数
                    };
                    var result = await _productApi.GetProductPagedListAsync(productSearchDto);

                    DispatcherProvider.Invoke(() =>
                    {
                        PopItemSource.Clear();
                        PopSelectedItem = null;

                        if (!result.Success || result.Data == null)
                        {
                            IsPopupOpen = false;
                            return;
                        }

                        foreach (var dto in result.Data.Items)
                        {
                            var model = dto.Adapt<ProductModel>();
                            model.State = ModelState.Unchanged;
                            PopItemSource.Add(model);

                            // 预留：搜索结果写入缓存（当前按需求禁用）
                            // if (!string.IsNullOrWhiteSpace(model.Sku))
                            //     _skuProductCache[model.Sku.Trim()] = model;
                        }

                        PopSelectedItem = PopItemSource.FirstOrDefault();
                        IsPopupOpen = PopSelectedItem != null;
                    });
                }
                catch
                {
                    DispatcherProvider.Invoke(() =>
                    {
                        PopSelectedItem = null;
                        PopItemSource.Clear();
                        IsPopupOpen = false;
                    });
                }
            }

        }

        private void CommitPdForPopup(object rowObj)
        {

            using (BusyScope())
            {
                DispatcherProvider.BeginInvoke(() =>
                {
                    try
                    {
                        // 中文注释：优先使用行为传入的行对象；
                        // 单元格模式下 SelectedProduct 可能为 null，因此再兜底当前单元格行。
                        var row = rowObj as CustomerProductModel
                                  ?? SelectedProduct
                                  ?? GetCurrentCellRow();
                        if (row == null) return;

                        var selectedPopupProduct = PopSelectedItem;
                        if (selectedPopupProduct == null) return;

                        // 中文注释：当前策略：仅行模式 Popup 选择时写入关联字段。
                        row.SkuId = selectedPopupProduct.Id;
                        row.Sku = selectedPopupProduct.Sku;
                        row.LinkSku = selectedPopupProduct.LinkSku;
                        row.Description = selectedPopupProduct.DescriptionEn;
                        row.CustomerPdDescription = selectedPopupProduct.DescriptionEn;
                        row.CustomerPdFeatures = selectedPopupProduct.FeaturesEn;
                        // 中文注释：选择关联物料时，默认带出标准产品美元/CNYPrice，
                        // 便于客户产品行后续快速调整，减少重复录入。
                        row.USDPrice = selectedPopupProduct.USDPrice;
                        row.CNYPrice = selectedPopupProduct.CNYPrice;
                        row.MainImage = selectedPopupProduct.MainImage;
                        row.IsActive = true;

                        SelectedProduct = row;
                        IsPopupOpen = false;
                    }
                    finally
                    {

                    }
                });
            }

        }

        /// <summary>
        /// 在单元格模式下，从主窗口焦点链路兜底获取当前 DataGrid 行对象。
        /// 说明：Ctrl+D/粘贴等 Excel 快捷操作不依赖 SelectedItem，这里用于 Popup 提交兜底。
        /// </summary>
        private CustomerProductModel GetCurrentCellRow()
        {
            try
            {
                var focused = Keyboard.FocusedElement as DependencyObject;
                while (focused != null)
                {
                    var cell = focused as DataGridCell;
                    if (cell != null)
                    {
                        var row = DataGridRow.GetRowContainingElement(cell);
                        return row?.Item as CustomerProductModel;
                    }

                    focused = System.Windows.Media.VisualTreeHelper.GetParent(focused);
                }
            }
            catch
            {
                // 中文注释：兜底失败不抛异常，交由上层 null 判断。
            }

            return null;
        }

        #region 预留联动逻辑（当前按需求禁用，仅保留未来可用性）

        /*
        /// <summary>
        /// 监听列表新增/移除，维护每行 PropertyChanged 订阅。
        /// </summary>
        private void OrderLineItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<CustomerProductModel>())
                {
                    item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                    item.PropertyChanged += SalesOrderLineItem_PropertyChanged;
                }
            }

            if (e?.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<CustomerProductModel>())
                {
                    item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                }
            }
        }

        /// <summary>
        /// 初始化时为已有行绑定事件。
        /// </summary>
        private void AttachItemHandlers(IEnumerable<CustomerProductModel> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                item.PropertyChanged += SalesOrderLineItem_PropertyChanged;
            }
        }

        /// <summary>
        /// 当 Sku 变化时，自动联动 SkuId/LinkSku/DescriptionEn/MainImage。
        /// 兼容：手工输入、Popup 回填、Ctrl+D、粘贴。
        /// </summary>
        private async void SalesOrderLineItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || e.PropertyName != nameof(CustomerProductModel.Sku)) return;

            var row = sender as CustomerProductModel;
            if (row == null) return;

            await TryApplyLinkageAsync(row);
        }

        /// <summary>
        /// 根据行内 Sku 尝试联动产品信息。
        /// </summary>
        private async Task TryApplyLinkageAsync(CustomerProductModel row)
        {
            if (row == null) return;
            if (_linkingRows.Contains(row)) return;

            var sku = (row.Sku ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                // 中文注释：当 Sku 被清空时，同步清空联动字段，避免旧值残留。
                DispatcherProvider.Invoke(() =>
                {
                    row.SkuId = 0;
                    row.LinkSku = string.Empty;
                    row.DescriptionEn = string.Empty;
                    row.MainImage = string.Empty;
                });
                return;
            }

            _linkingRows.Add(row);
            try
            {
                ProductModel product;
                if (!_skuProductCache.TryGetValue(sku, out product))
                {
                    product = await QueryProductBySkuAsync(sku);
                    if (product != null)
                    {
                        _skuProductCache[sku] = product;
                    }
                }

                if (product == null) return;

                // 中文注释：应用前再次确认当前行 Sku 未被用户改动，避免旧请求覆盖新输入。
                if (!string.Equals((row.Sku ?? string.Empty).Trim(), sku, StringComparison.OrdinalIgnoreCase))
                    return;

                DispatcherProvider.Invoke(() =>
                {
                    ApplyProductLinkage(row, product, overwriteSku: false);
                });
            }
            catch
            {
                // 中文注释：联动失败不打断编辑流程，保持静默。
            }
            finally
            {
                _linkingRows.Remove(row);
            }
        }

        /// <summary>
        /// 通过 API 按 Sku 查询产品并返回最匹配项。
        /// </summary>
        private async Task<ProductModel> QueryProductBySkuAsync(string sku)
        {
            if (_productApi == null || string.IsNullOrWhiteSpace(sku)) return null;

            var result = await _productApi.GetProductPagedListAsync(new ProductSearchDto
            {
                Keyword = sku,
                PageIndex = 1,
                PageSize = 20
            });

            if (!result.Success || result.Data == null || result.Data.Items == null || result.Data.Items.Count == 0)
                return null;

            var exact = result.Data.Items
                .FirstOrDefault(x => string.Equals((x.Sku ?? string.Empty).Trim(), sku.Trim(), StringComparison.OrdinalIgnoreCase));

            var dto = exact ?? result.Data.Items.FirstOrDefault();
            return dto?.Adapt<ProductModel>();
        }

        /// <summary>
        /// 应用产品联动字段到当前客户产品行。
        /// </summary>
        private void ApplyProductLinkage(CustomerProductModel row, ProductModel product, bool overwriteSku)
        {
            if (row == null || product == null) return;

            row.SkuId = product.Id;
            if (overwriteSku)
            {
                // 中文注释：仅在 Popup 确认场景覆盖 Sku 文本。
                row.Sku = product.Sku;
            }

            row.LinkSku = product.LinkSku;
            row.DescriptionEn = product.DescriptionEn;
            row.MainImage = product.MainImage;
            row.IsActive = true;
        }
        */

        #endregion

        #endregion
    }
}
