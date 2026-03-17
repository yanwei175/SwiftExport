using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.VSTO.UserWindow.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SwiftExport.VSTO.UserWindow.ProductViews.ViewModels
{
    // 继承你自己的 ViewModelBase
    public class ProductSelectorViewModel : ViewModelBase
    {
        private readonly IProductApi _productApi;
        private CancellationTokenSource _searchCts;

        private readonly Dispatcher _dispatcher;

        private int _currentPage = 1;
        private readonly int _pageSize = 50;
        private bool _hasMoreData = true;

        // 通知 Window 关闭的事件 (true=确定, false=取消)
        public event Action<bool> RequestClose;
        // 通知 Window 隐藏的事件
        public event Action RequestHide;
        // 通知 UI 滚动到顶部
        public event Action RequestScrollToTop;

        // ================= 命令声明 =================
        public RelayCommand LoadDataCommand { get; }
        public RelayCommand SelectItemsCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand RemoveItemsCommand { get; }
        public RelayCommand RemoveAllCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand HideCommand { get; }
        public ProductSelectorViewModel(IProductApi productApi)
        {

            _productApi = productApi;

            _dispatcher = Dispatcher.CurrentDispatcher;

            // C# 7.3 不支持 new() 简写，必须写全类型
            TempSelectedSourceItems = new List<ProductDto>();
            TempSelectedTargetItems = new List<ProductDto>();
            Customers = new ObservableCollection<string>();
            SourceProducts = new ObservableCollection<ProductDto>();
            SelectedProducts = new ObservableCollection<ProductDto>();

            // 初始化所有命令 (假设你的 RelayCommand 接受 Action)
            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync(false));
            SelectItemsCommand = new RelayCommand(SelectItems);
            SelectAllCommand = new RelayCommand(SelectAll);
            RemoveItemsCommand = new RelayCommand(RemoveItems);
            RemoveAllCommand = new RelayCommand(RemoveAll);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(Cancel);
            HideCommand = new RelayCommand(Hide);

            _ = LoadCustomersAsync();
            // 这里只需要处理业务逻辑：选了客户后要查数据
            CustomerChangedCommand = new RelayCommand<object>((obj) =>
            {
                // 触发查询，并处理可能的未捕获异常
                // 因为这里是 Command，没法 await，只能 Fire-and-Forget
                //InitializeSearchAsync();
                TriggerSearch();
            });
        }
        // 定义一个专门给 Command 用的包装方法
        private async void InitializeSearchAsync()
        {
            try
            {
                await LoadDataAsync(resetPage: true);
            }
            catch (Exception ex)
            {
                // 确保这里能捕获到 LoadDataAsync 内部抛出的异常
                StatusMessage = $"查询出错: {ex.Message}";
            }
        }
        // ================= 数据源属性 =================
        public ObservableCollection<string> Customers { get; set; }
        // ★ 接收控件通知的命令
        public RelayCommand<object> CustomerChangedCommand { get; }


        private ObservableCollection<ProductDto> _sourceProducts;
        public ObservableCollection<ProductDto> SourceProducts
        {
            get => _sourceProducts;
            set { _sourceProducts = value; RaisePropertyChanged(nameof(SourceProducts)); }
        }

        private ObservableCollection<ProductDto> _selectedProducts;
        public ObservableCollection<ProductDto> SelectedProducts
        {
            get => _selectedProducts;
            set { _selectedProducts = value; RaisePropertyChanged(nameof(SelectedProducts)); }
        }

        // 临时存储 UI 选中的多行数据
        public List<ProductDto> TempSelectedSourceItems { get; set; }
        public List<ProductDto> TempSelectedTargetItems { get; set; }

        // ================= 搜索条件属性 =================
        // 选中的客户
        public string SelectedCustomer { get; set; }
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    RaisePropertyChanged(nameof(SearchText));
                    TriggerSearch();
                }
            }
        }

        private string _batchSearchText;
        public string BatchSearchText
        {
            get => _batchSearchText;
            set
            {
                if (_batchSearchText != value)
                {
                    _batchSearchText = value;
                    RaisePropertyChanged(nameof(BatchSearchText));
                    TriggerSearch();
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; RaisePropertyChanged(nameof(IsLoading)); }
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(nameof(StatusMessage)); }
        }

        // 注意：这里的 RaisePropertyChanged 请替换为你 ViewModelBase 中实际唤起属性变更通知的方法名。
        private void RaisePropertyChanged(string propertyName)
        {
            // 如果你的基础类叫 Raise，直接调用 Raise(propertyName); 
            // 如果是 OnPropertyChanged，就调用 OnPropertyChanged(propertyName);
            base.OnPropertyChanged(propertyName); // 猜测依据你提供的其他文件
        }

        // ================= 核心加载逻辑 =================
        private async Task LoadCustomersAsync()
        {
            try
            {
                StatusMessage = "正在加载客户列表...";

                // 1. 发起网络请求
                var result = await _productApi.GetAllCustomerCodeListAsync();

                if (result != null && result.Success)
                {
                    var list = result.Data?.ToList() ?? new List<string>();
                    list.Insert(0, ""); // 插入空项代表“全部”

                    // 2. ★ 强制切回 WPF 的主 UI 线程进行数据绑定操作 ★
                    _dispatcher.Invoke(() =>
                    {
                        // 不要 new 新对象，而是操作在构造函数里已经 new 好的那个集合
                        Customers.Clear();
                        foreach (var item in list)
                        {
                            Customers.Add(item);
                        }
                    });

                    StatusMessage = "准备就绪";
                }
                else
                {
                    // 拿到后端的报错信息
                    StatusMessage = $"加载客户失败: {result?.Message}";
                }
            }
            catch (Exception ex)
            {
                // 如果是反序列化失败、网络不通，这里能立刻捕捉到！
                StatusMessage = $"加载客户异常: {ex.Message}";
                MessageBox.Show($"加载客户数据异常:\n{ex.Message}", "调试信息", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void TriggerSearch()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            Task.Delay(500, _searchCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _dispatcher.Invoke(() => LoadDataAsync(resetPage: true));
            });
        }

        private async Task LoadDataAsync(bool resetPage = false)
        {
            if (IsLoading) return;

            if (resetPage)
            {
                _currentPage = 1;
                SourceProducts.Clear();
                _hasMoreData = true;
                RequestScrollToTop?.Invoke();
            }

            if (!_hasMoreData) return;

            if (string.IsNullOrWhiteSpace(SearchText) &&
                string.IsNullOrWhiteSpace(BatchSearchText) &&
                string.IsNullOrWhiteSpace(SelectedCustomer))
            {
                SourceProducts.Clear();
                return;
            }

            IsLoading = true;
            StatusMessage = "正在查询数据...";

            try
            {
                ProductSearchDto searchArgs = new ProductSearchDto
                {
                    Keyword = SearchText,
                    CustomerCode = SelectedCustomer,
                    BatchKeywords = BatchSearchText,
                    PageIndex = _currentPage,
                    PageSize = _pageSize
                };

                var result = await _productApi.GetProductPagedListAsync(searchArgs);

                if (result.Success)
                {
                    var items = result.Data.Items;
                    foreach (var item in items)
                    {
                        SourceProducts.Add(item);
                    }

                    _hasMoreData = items.Count == _pageSize;
                    if (_hasMoreData) _currentPage++;

                    StatusMessage = $"加载完成，当前左侧共 {SourceProducts.Count} 条数据";
                }
                else
                {
                    StatusMessage = $"查询失败: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"发生异常: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }



        // ================= 表单验证逻辑 =================


        // 2. 添加一个辅助属性，用来判断整个表单是否通过
        // 供 Command 判断按钮是否可用
        public bool IsValid
        {
            get
            {
                // 只要 SelectedCustomer 没有报错，就是有效的
                // 这种写法复用了上面的验证逻辑，避免写两遍
                return !string.IsNullOrWhiteSpace(SelectedCustomer);
            }
        }

        // ================= 穿梭移动逻辑 =================
        private void SelectItems()
        {
            var targetItems = TempSelectedSourceItems;
            if (targetItems == null || !targetItems.Any()) return;

            foreach (var item in targetItems)
            {
                if (!SelectedProducts.Any(x => x.Sku == item.Sku))
                    SelectedProducts.Add(item);
            }
        }

        private void SelectAll()
        {
            foreach (var item in SourceProducts)
            {
                if (!SelectedProducts.Any(x => x.Sku == item.Sku))
                    SelectedProducts.Add(item);
            }
        }

        private void RemoveItems()
        {
            var targetItems = TempSelectedTargetItems;
            if (targetItems == null || !targetItems.Any()) return;

            // 为了避免在遍历中修改集合报错，先 ToList()
            foreach (var item in targetItems.ToList())
            {
                SelectedProducts.Remove(item);
            }
        }

        private void RemoveAll()
        {
            SelectedProducts.Clear();
        }

        // ================= 确认与取消 =================
        private void Confirm()
        {
            // 场景：用户搜出结果后，直接按回车，懒得点“选择”移到右边去
            if (SelectedProducts.Count == 0)
            {
                // 如果用户在左侧(SourceDataGrid)用鼠标点中（高亮）了某几行，直接拿走
                if (TempSelectedSourceItems != null && TempSelectedSourceItems.Any())
                {
                    foreach (var item in TempSelectedSourceItems)
                    {
                        SelectedProducts.Add(item);
                    }
                }
                // 如果连鼠标都没点，但左侧查出了数据，直接强行拿走第一条！
                else if (SourceProducts.Count > 0)
                {
                    SelectedProducts.Add(SourceProducts.First());
                }
                else
                {
                    // 连数据都没有，没法确认，提示一下
                    StatusMessage = "没有可确认的产品！";
                    return;
                }
            }

            // 触发关闭并返回 True
            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
        private void Hide()
        {
            RequestHide?.Invoke();
        }
    }
}