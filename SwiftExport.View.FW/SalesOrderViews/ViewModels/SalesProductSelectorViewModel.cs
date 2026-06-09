using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.ViewModels
{
    public class SalesProductSelectorViewModel : ViewModelBaseEx
    {
        private readonly ISalesOrderApi _salesOrderApi;
        private readonly ICustomerApi _customerApi;
        private readonly IProductColorApi _pdColorApi;
        private CancellationTokenSource _searchCts;
        private int _pageIndex = 1;
        private int _pageSize = 50;
        private int _totalCount;

        public event Action RequestScrollToTop;

        /// <summary> 弹窗后的结果</summary>
        public List<SalesProductDto> ResultData { get; set; }

        /// <summary> 关闭弹窗的委托 </summary>
        public event EventHandler<bool> RequestClose;
        public SalesProductSelectorViewModel(ISalesOrderApi salesOrderApi, ICustomerApi customerApi,IProductColorApi productColorApi)
        {
            _salesOrderApi = salesOrderApi ?? IocProvider.GetService<ISalesOrderApi>();
            _customerApi = customerApi ?? IocProvider.GetService<ICustomerApi>();
            _pdColorApi = productColorApi ?? IocProvider.GetService<IProductColorApi>();

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync(false));
            ExecuteQueryCommand = new RelayCommand(async () => await LoadDataAsync(true));
            PageChangedCommand = new RelayCommand(async () => await LoadDataAsync(false));
            SelectItemsCommand = new RelayCommand(SelectItems);
            SelectAllCommand = new RelayCommand(SelectAll);
            RemoveItemsCommand = new RelayCommand(RemoveItems);
            RemoveAllCommand = new RelayCommand(RemoveAll);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(Cancel);
            ToggleMatchCustomerSkuPopupCommand = new RelayCommand(() => IsMatchCustomerSkuPopupOpen = !IsMatchCustomerSkuPopupOpen);

            _ = LoadCustomersAsync();
            _ = LoadColorsAsync();
            _ = LoadDataAsync(true);
        }
        public RelayCommand LoadDataCommand { get; }
        public RelayCommand ExecuteQueryCommand { get; }
        public RelayCommand PageChangedCommand { get; }
        public RelayCommand SelectItemsCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand RemoveItemsCommand { get; }
        public RelayCommand RemoveAllCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand ToggleMatchCustomerSkuPopupCommand { get; }

        public ObservableCollection<CustomerDto> CustomerList { get; } = new ObservableCollection<CustomerDto>();
        public ObservableCollection<ProductColorDto> ColorList { get; } = new ObservableCollection<ProductColorDto>();

        private ObservableCollection<SalesProductDto> _unshippedList = new ObservableCollection<SalesProductDto>();
        public ObservableCollection<SalesProductDto> UnshippedList
        {
            get => _unshippedList;
            set => SetProperty(ref _unshippedList, value);
        }

        private ObservableCollection<SalesProductDto> _selectedOutboundLines = new ObservableCollection<SalesProductDto>();
        public ObservableCollection<SalesProductDto> SelectedOutboundLines
        {
            get => _selectedOutboundLines;
            set => SetProperty(ref _selectedOutboundLines, value);
        }

        public List<SalesProductDto> TempSelectedSourceItems { get; set; } = new List<SalesProductDto>();
        public List<SalesProductDto> TempSelectedTargetItems { get; set; } = new List<SalesProductDto>();

        private ObservableCollection<CustomerSkuMatchResultItem> _matchResultCollection = new ObservableCollection<CustomerSkuMatchResultItem>();
        public ObservableCollection<CustomerSkuMatchResultItem> MatchResultCollection
        {
            get => _matchResultCollection;
            set => SetProperty(ref _matchResultCollection, value);
        }

        private Guid? _selectedCustomerId;
        public Guid? SelectedCustomerId
        {
            get => _selectedCustomerId;
            set
            {
                if (SetProperty(ref _selectedCustomerId, value))
                {
                    TriggerSearch();
                }
            }
        }
        private Guid? _selectedColorId;
        public Guid? SelectedColorId
        {
            get => _selectedColorId;
            set
            {
                if (SetProperty(ref _selectedColorId, value))
                {
                    TriggerSearch();
                }
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    TriggerSearch();
                }
            }
        }

        private string _matchCustomerSkuText;
        public string MatchCustomerSkuText
        {
            get => _matchCustomerSkuText;
            set
            {
                if (SetProperty(ref _matchCustomerSkuText, value))
                {
                    TriggerSearch();
                }
            }
        }
        private bool _isMatchCustomerSkuPopupOpen;
        public bool IsMatchCustomerSkuPopupOpen
        {
            get => _isMatchCustomerSkuPopupOpen;
            set => SetProperty(ref _isMatchCustomerSkuPopupOpen, value);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 当前页码（从 1 开始）。
        /// </summary>
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (value < 1) value = 1;
                SetProperty(ref _pageIndex, value);
            }
        }

        /// <summary>
        /// 每页条数。
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value <= 0) value = 50;
                if (SetProperty(ref _pageSize, value))
                {
                    RaisePropertyChanged(nameof(TotalPages));
                }
            }
        }

        /// <summary>
        /// 总记录数。
        /// </summary>
        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetProperty(ref _totalCount, value))
                {
                    RaisePropertyChanged(nameof(TotalPages));
                }
            }
        }

        /// <summary>
        /// 总页数（逻辑推算）。
        /// </summary>
        public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));



        private void TriggerSearch()
        {
            // 中文注释：取消上一次未执行的防抖任务，重新计时 350ms。
            // 不在此处使用 BusyScope——防抖 delay 是异步的，BusyScope 同步 Dispose 后
            // 会导致 IsBusy 在 delay 期间错误为 true，拦截 ExecuteQueryCommand 等主动查询。
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            Task.Delay(350, _searchCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                DispatcherProvider.Invoke(() =>
                {
                    PageIndex = 1;
                    _ = LoadDataAsync(false);
                });
            }, TaskScheduler.Default);
        }

        private async Task LoadCustomersAsync()
        {
            try
            {
                var result = await _customerApi.GetAllAsync();

                if (result?.Success == true && result.Data != null)
                {
                    var customers = result.Data;
                    DispatcherProvider.Invoke(() =>
                    {
                        CustomerList.Clear();
                        foreach (var customer in customers)
                        {
                            CustomerList.Add(customer);
                        }
                    });
                }
            }
            catch
            {
            }
        }
        private async Task LoadColorsAsync()
        {
            try
            {
                var result = await _pdColorApi.GetAllAsync();

                if (result?.Success == true && result.Data != null)
                {
                    var colors = result.Data;
                    DispatcherProvider.Invoke(() =>
                    {
                        ColorList.Clear();
                        foreach (var color in colors)
                        {
                            ColorList.Add(color);
                        }
                    });
                }
            }
            catch
            {
            }
        }




        private async Task LoadDataAsync(bool resetPage)
        {
            if (IsBusy) return;

            if (resetPage)
            {
                DispatcherProvider.Invoke(() =>
                {
                    PageIndex = 1;
                    RequestScrollToTop?.Invoke();
                });
            }

            StatusMessage = "正在查询客户产品数据...";

            using (BusyScope())
            {
                try
                {
                    var result = await _salesOrderApi.GetSalesProductPagedListAsync(BuildSearchDto());

                    if (result?.Success == true && result.Data != null)
                    {
                        var items = result.Data.Items?.ToList() ?? new List<SalesProductDto>();
                        DispatcherProvider.Invoke(() =>
                        {
                            UnshippedList.Clear();
                            foreach (var item in items)
                            {
                                UnshippedList.Add(item);
                            }

                            // 中文注释：使用后端返回的分页信息刷新分页控件状态。
                            PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
                            PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : PageSize;
                            TotalCount = result.Data.TotalCount;
                        });

                        UpdateMatchResultCollection();
                        StatusMessage = $"已加载第 {PageIndex}/{TotalPages} 页，共 {TotalCount} 条候选数据";
                    }
                    else
                    {
                        StatusMessage = $"查询失败: {result?.Message}";
                        DispatcherProvider.Invoke(() => MatchResultCollection.Clear());
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"查询异常: {ex.Message}";
                }
            }
        }


        private SalesProductSearchDto BuildSearchDto()
        {
            return new SalesProductSearchDto
            {
                CustomerId = SelectedCustomerId ?? Guid.Empty,
                ColorId = SelectedColorId ?? Guid.Empty,
                BatchKeywords = SplitLines(SearchText),
                BatchCustomerSkuList = SplitLines(MatchCustomerSkuText),
                PageIndex = PageIndex,
                PageSize = PageSize
            };
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void UpdateMatchResultCollection()
        {
            var expectedSkus = SplitLines(MatchCustomerSkuText);
            DispatcherProvider.Invoke(() =>
            {
                MatchResultCollection.Clear();
                if (expectedSkus.Count == 0) return;

                var sourceSkus = new HashSet<string>(
                    UnshippedList.Select(x => x.CustomerSku ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var sku in expectedSkus)
                {
                    MatchResultCollection.Add(new CustomerSkuMatchResultItem
                    {
                        CustomerSku = sku,
                        MatchResult = sourceSkus.Contains(sku) ? "已匹配" : "未匹配"
                    });
                }
            });
        }

        private void SelectItems()
        {
            var selectedItems = TempSelectedSourceItems;
            if (selectedItems == null || selectedItems.Count == 0) return;

            foreach (var item in selectedItems)
            {
                if (!ContainsSku(SelectedOutboundLines, item))
                {
                    SelectedOutboundLines.Add(item);
                }
            }
        }

        private void SelectAll()
        {
            foreach (var item in UnshippedList)
            {
                if (!ContainsSku(SelectedOutboundLines, item))
                {
                    SelectedOutboundLines.Add(item);
                }
            }
        }

        private void RemoveItems()
        {
            var selectedItems = TempSelectedTargetItems;
            if (selectedItems == null || selectedItems.Count == 0) return;

            foreach (var item in selectedItems.ToList())
            {
                SelectedOutboundLines.Remove(item);
            }
        }

        private void RemoveAll()
        {
            SelectedOutboundLines.Clear();
        }

        private void Confirm()
        {
            if (SelectedOutboundLines.Count == 0)
            {
                if (TempSelectedSourceItems != null && TempSelectedSourceItems.Count > 0)
                {
                    foreach (var item in TempSelectedSourceItems)
                    {
                        if (!ContainsSku(SelectedOutboundLines, item))
                        {
                            SelectedOutboundLines.Add(item);
                        }
                    }
                }
                else if (UnshippedList.Count > 0)
                {
                    SelectedOutboundLines.Add(UnshippedList.First());
                }
                else
                {
                    StatusMessage = "没有可确认的产品数据";
                    return;
                }
            }

            ResultData = SelectedOutboundLines.ToList();
            RequestClose?.Invoke(this, true);
        }

        private void Cancel()
        {
            ResultData = null;
            RequestClose?.Invoke(this, false);
        }

        private static bool ContainsSku(IEnumerable<SalesProductDto> collection, SalesProductDto item)
        {
            if (item == null) return false;

            var sku = (item.Sku ?? string.Empty).Trim();
            var customerSku = (item.CustomerSku ?? string.Empty).Trim();

            return collection.Any(x =>
                string.Equals((x.Sku ?? string.Empty).Trim(), sku, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.CustomerSku ?? string.Empty).Trim(), customerSku, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class CustomerSkuMatchResultItem
    {
        public string CustomerSku { get; set; }
        public string MatchResult { get; set; }
    }
}
