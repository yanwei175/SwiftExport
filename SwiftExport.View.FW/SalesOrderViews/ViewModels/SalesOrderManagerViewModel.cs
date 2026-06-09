using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Mvvm.Helpers;
using SwiftExport.View.FW.SalesOrderViews.Models;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.ViewModels
{
    /// <summary>
    /// 销售订单管理工作台 ViewModel。
    /// 职责：分页查询订单头列表、批量审批/驳回、删除、快速保存 Header 字段，
    /// 以及通过弹出 SalesOrderDetailWindow 完成新建和编辑操作。
    /// 行级编辑逻辑完全下沉到 SalesOrderDetailViewModel，本 VM 不持有行集合。
    /// </summary>
    public class SalesOrderManagerViewModel : ViewModelBaseEx
    {
        #region 字段 / 依赖

        private readonly ISalesOrderApi _salesOrderApi;
        private readonly ICustomerApi _customerApi;
        private readonly ISysDicCurrencyApi _sysDicCurrencyApi;
        private readonly IDictCacheService _dictCacheService;
        private readonly IDialogService _msg;
        private readonly IProductColorApi _pdColorApi;

        #endregion

        #region 构造函数

        [Obsolete("仅供 XAML 设计器使用", true)]
        internal SalesOrderManagerViewModel() { }

        public SalesOrderManagerViewModel(
            ISalesOrderApi salesOrderApi,
            IDialogService dialogService,
            ICustomerApi customerApi,
            ISysDicCurrencyApi sysDicCurrencyApi,
            IProductColorApi productColorApi,
            IDictCacheService dictCacheService)
        {
            _salesOrderApi = salesOrderApi;
            _msg = dialogService;
            _customerApi = customerApi;
            _sysDicCurrencyApi = sysDicCurrencyApi;
            _dictCacheService = dictCacheService;
            _pdColorApi = productColorApi;

            OrderHeaders = new SalesOrderHeaderManagerList();

            // 中文注释：加载命令，带 BusyScope 保护，防止重复触发。
            LoadCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            RegisterBusyNotifyCommand(LoadCommand);

            // 中文注释：新建订单——弹出空白 DetailWindow。
            CreateOrderCommand = new RelayCommand(OpenCreateOrderWindow, () => IsNotBusy);
            RegisterBusyNotifyCommand(CreateOrderCommand);

            // 中文注释：编辑订单——弹出已加载数据的 DetailWindow。
            EditOrderCommand = new RelayCommand(async () => await OpenEditOrderWindowAsync(), () => IsNotBusy && SelectedOrderHeader != null);
            RegisterBusyNotifyCommand(EditOrderCommand);

            // 中文注释：Header 级快速保存（只提交 Header 变更，不涉及行）。
            SaveCommand = new RelayCommand(async () => await SaveHeaderAsync(), () => CanSave);
            RegisterBusyNotifyCommand(SaveCommand);

            ConfirmCommand = new RelayCommand(async () => await ConfirmAsync(), () => CanConfirm);
            RegisterBusyNotifyCommand(ConfirmCommand);

            CancelConfirmCommand = new RelayCommand(async () => await CancelConfirmAsync(), () => CanCancelConfirm);
            RegisterBusyNotifyCommand(CancelConfirmCommand);

            DeleteSelectedHeadCommand = new RelayCommand(async () => await DeleteSelectedHeadAsync(), () => SelectedOrderHeader != null && IsNotBusy);
            RegisterBusyNotifyCommand(DeleteSelectedHeadCommand);

            InvertCheckCommand = new RelayCommand(OrderHeaders.InvertCheckAll);


            // 中文注释：订阅多选选项的 IsSelected 变化，实时刷新 ToggleButton 上的摘要文字。
            foreach (var item in ConfirmStatusOptions)
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CheckableItem<ConfirmationStatusEnum>.IsSelected)) RaisePropertyChanged(nameof(SelectedConfirmStatuses)); };
            foreach (var item in OrderStatusOptions)
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CheckableItem<SalesOrderStatusEnum>.IsSelected)) RaisePropertyChanged(nameof(SelectedOrderStatuses)); };

            _ = LoadCustomersAsync();
            _ = LoadColorsAsync();
        }

        #endregion

        #region 列表与选中

        /// <summary> 订单头集合，绑定到 Manager DataGrid。</summary>
        public SalesOrderHeaderManagerList OrderHeaders { get; }

        private SalesOrderHeaderManagerModel _selectedOrderHeader;
        /// <summary> 当前选中订单头，切换时刷新命令状态。</summary>
        public SalesOrderHeaderManagerModel SelectedOrderHeader
        {
            get => _selectedOrderHeader;
            set
            {
                if (SetProperty(ref _selectedOrderHeader, value))
                {
                    RaisePropertyChanged(nameof(CanSave));
                    RaisePropertyChanged(nameof(StatusSummaryText));
                    SaveCommand?.RaiseCanExecuteChanged();
                    EditOrderCommand?.RaiseCanExecuteChanged();
                    DeleteSelectedHeadCommand?.RaiseCanExecuteChanged();
                    _ = LoadLinesByHeadIdAsync(value?.Uid ?? Guid.Empty);
                }
            }
        }

        private SalesOrderLineManagerList _currentOrderLines;
        public SalesOrderLineManagerList CurrentOrderLines
        {
            get => _currentOrderLines;
            set => SetProperty(ref _currentOrderLines, value);
        }

        private SalesOrderLineManagerModel _selectedLine;
        public SalesOrderLineManagerModel SelectedLine
        {
            get => _selectedLine;
            set => SetProperty(ref _selectedLine, value);
        }
        public ObservableCollection<CustomerDto> CustomerFilterItems { get; } = new ObservableCollection<CustomerDto>();
        public ObservableCollection<ProductColorDto> ColorList { get; } = new ObservableCollection<ProductColorDto>();

        public ObservableCollection<string> OrderNoList { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> InvNoList { get; } = new ObservableCollection<string>();
        #endregion

        #region 筛选字段

        private string _selectedCustomerFilter;
        public string SelectedCustomerFilter
        {
            get => _selectedCustomerFilter;
            set => SetProperty(ref _selectedCustomerFilter, value);
        }

        private string _selectedOrderNoFilter;
        public string SelectedOrderNoFilter
        {
            get => _selectedOrderNoFilter;
            set => SetProperty(ref _selectedOrderNoFilter, value);
        }

        private string _selectedInvNoFilter;
        public string SelectedInvNoFilter
        {
            get => _selectedInvNoFilter;
            set => SetProperty(ref _selectedInvNoFilter, value);
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private DateTime? _orderDateFrom;
        public DateTime? OrderDateFrom
        {
            get => _orderDateFrom;
            set => SetProperty(ref _orderDateFrom, value);
        }

        private DateTime? _orderDateTo;
        public DateTime? OrderDateTo
        {
            get => _orderDateTo;
            set => SetProperty(ref _orderDateTo, value);
        }

        private string _lineSearchText;
        public string LineSearchText
        {
            get => _lineSearchText;
            set
            {
                if (SetProperty(ref _lineSearchText, value))
                {
                    //内存筛选CurrentOrderLines,可能要建一个视图筛选模型


                }
            }
        }

        // 中文注释：审批状态多选选项，每项是 CheckableItem<ConfirmationStatusEnum>，IsSelected 双向绑定到 CheckBox。
        // PropertyChanged 订阅在构造函数中完成，变化时自动通知摘要属性刷新。
        public ObservableCollection<CheckableItem<ConfirmationStatusEnum>> ConfirmStatusOptions { get; } = EnumOptionHelper.ToCheckableItems<ConfirmationStatusEnum>();

        // 中文注释：审批状态摘要文字，绑定到 ToggleButton 上的 TextBlock.Text。
        public string SelectedConfirmStatuses =>
            ConfirmStatusOptions.Where(x => x.IsSelected) is var sel && sel.Any()
                ? string.Join("、", sel.Select(x => x.Label))
                : "全部";

        // 中文注释：订单状态多选选项。
        public ObservableCollection<CheckableItem<SalesOrderStatusEnum>> OrderStatusOptions { get; } = EnumOptionHelper.ToCheckableItems<SalesOrderStatusEnum>();


        // 中文注释：订单状态摘要文字。绑定到 ToggleButton 上的 TextBlock.Text。
        public string SelectedOrderStatuses =>
            OrderStatusOptions.Where(x => x.IsSelected) is var sel2 && sel2.Any()
                ? string.Join("、", sel2.Select(x => x.Label))
                : "全部";


        private string _batchCustomerSkuText;
        public string BatchCustomerSkuText
        {
            get => _batchCustomerSkuText;
            set => SetProperty(ref _batchCustomerSkuText, value);
        }

        #endregion

        #region 分页与状态栏

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set => SetProperty(ref _pageIndex, value);
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set => SetProperty(ref _pageSize, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary> 状态栏汇总文本，显示当前选中订单的基础信息。</summary>
        public string StatusSummaryText
        {
            get
            {
                if (SelectedOrderHeader == null) return string.Empty;
                return string.Format("订单：{0}  客户：{1}  金额：{2:N2} {3}",
                    SelectedOrderHeader.OrderNo,
                    SelectedOrderHeader.CustomerCode,
                    SelectedOrderHeader.TotalAmountOriginalCurrency,
                    SelectedOrderHeader.Currency);
            }
        }

        #endregion

        #region 命令启用条件

        /// <summary> 确认命令是否可用。</summary>
        public bool CanConfirm
        {
            get => SelectedOrderHeader != null &&
              SelectedOrderHeader.ConfirmationStatus != ConfirmationStatusEnum.已确认 && IsNotBusy;

        }

        /// <summary> 取消确认命令是否可用。</summary>
        public bool CanCancelConfirm
        {
            get => SelectedOrderHeader != null && SelectedOrderHeader.ConfirmationStatus == ConfirmationStatusEnum.已确认 && IsNotBusy;
        }

        /// <summary> Header 快速保存：有选中行且存在脏数据。</summary>
        public bool CanSave =>
            SelectedOrderHeader != null &&
            (SelectedOrderHeader.State != ModelState.Unchanged || OrderHeaders.IsDirty);

        #endregion

        #region 命令

        public RelayCommand LoadCommand { get; }
        public RelayCommand CreateOrderCommand { get; }
        public RelayCommand EditOrderCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelConfirmCommand { get; }
        public RelayCommand DeleteSelectedHeadCommand { get; }
        public RelayCommand InvertCheckCommand { get; }
        public RelayCommand ItemCheckedChangedCommand { get; }

        #endregion

        #region 加载数据
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
                        CustomerFilterItems.Clear();
                        foreach (var customer in customers)
                        {
                            CustomerFilterItems.Add(customer);
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
        private async Task LoadAsync()
        {
            if (_salesOrderApi == null)
            {
                StatusMessage = "SalesOrderApi 未注册";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var search = new SalesOrderManagerSearchDto
                    {
                        CustomerCode = SelectedCustomerFilter,
                        OrderNo = SelectedOrderNoFilter,
                        InvoiceNo = SelectedInvNoFilter,
                        Keyword = SearchText,
                        OrderDateFrom = OrderDateFrom,
                        OrderDateTo = OrderDateTo,
                        // 中文注释：将 CheckableItem 中被勾选的枚举值转换为整数列表传给后端。
                        ConfirmStatuses = ConfirmStatusOptions
                            .Where(x => x.IsSelected)
                            .Select(x => (int)x.Value)
                            .ToList(),
                        OrderStatuses = OrderStatusOptions
                            .Where(x => x.IsSelected)
                            .Select(x => (int)x.Value)
                            .ToList(),
                        BatchCustomerSkuList = BatchCustomerSkuText?.Split(new[] { '\n', '\r', ',', '；', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    };

                    var rst = await _salesOrderApi.GetSalesOrderHeaderManagerPagedListAsync(search);
                    if (rst.Success && rst.Data != null)
                    {
                        OrderHeaders.ReLoadRangeFromDtoList(rst.Data.Items);
                        TotalCount = rst.Data.TotalCount;
                        SelectedOrderHeader = OrderHeaders.Items.FirstOrDefault();
                        StatusMessage = string.Format("加载成功，共 {0} 条", TotalCount);
                    }
                    else
                    {
                        OrderHeaders?.Clear();
                        CurrentOrderLines?.Clear();
                        TotalCount = 0;
                        StatusMessage = "加载失败：" + rst.Message;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常：" + ex.Message;
                }
            }
        }

        /// <summary>
        /// 中文注释：弹窗关闭后仅刷新当前选中行，避免重载全表。
        /// 若订单 ID 能在现有列表中找到则局部更新；否则刷新全表。
        /// </summary>

        private async Task LoadLinesByHeadIdAsync(Guid orderId)
        {
            using (BusyScope())
            {
                try
                {
                    if (orderId == Guid.Empty)
                    {
                        CurrentOrderLines.Clear();
                        return;
                    }

                    var rst = await _salesOrderApi.GetSalesOrderLineManagerAsync(orderId);
                    if (rst?.Success == true && rst.Data != null)
                    {
                        CurrentOrderLines = new SalesOrderLineManagerList();
                        CurrentOrderLines.ReLoadRangeFromDtoList(rst.Data);
                    }
                    else
                    {
                        CurrentOrderLines = new SalesOrderLineManagerList();
                        StatusMessage = "加载订单行失败：" + rst?.Message;
                    }
                }
                catch (Exception ex)
                {
                    CurrentOrderLines = new SalesOrderLineManagerList();
                    StatusMessage = "加载订单行异常：" + ex.Message;
                }
            }
        }

        #endregion

        #region 新建 / 编辑弹窗

        /// <summary>
        /// 中文注释：打开新建订单弹窗，Detail VM 构造为空白状态。
        /// 确认保存后局部刷新列表。
        /// </summary>
        private async void OpenCreateOrderWindow()
        {
            try
            {
                var vm = IocProvider.GetService<SalesOrderDetailViewModel>();
                vm.InitForCreate();
                var win = new SalesOrderDetailWindow(vm);
                var result = win.ShowDialog();
                if (result == true && vm.ResultData != Guid.Empty)
                {
                    var rst = await _salesOrderApi.GetSalesOrderHeaderManagerByIdAsync(vm.ResultData);
                    if (rst?.Success == true && rst.Data != null)
                    {
                        SelectedOrderHeader.LoadFromDto(rst.Data, false);
                    }
                    else
                    {
                        await LoadAsync();
                    }
                }

            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("打开新建窗口失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 中文注释：按当前选中订单 ID 加载详情，弹出编辑窗口。
        /// 保存后局部刷新该行；取消则不刷新。
        /// </summary>
        private async Task OpenEditOrderWindowAsync()
        {
            if (SelectedOrderHeader == null) return;

            var orderId = SelectedOrderHeader.Uid;
            if (orderId == Guid.Empty) return;
            try
            {
                var vm = IocProvider.GetService<SalesOrderDetailViewModel>();
                await vm.InitForEditAsync(orderId);
                var win = new SalesOrderDetailWindow(vm);
                var result = win.ShowDialog();
                if (result == true && vm.ResultData != Guid.Empty)
                {
                    var rst = await _salesOrderApi.GetSalesOrderHeaderManagerByIdAsync(vm.ResultData);
                    if (rst?.Success == true && rst.Data != null)
                    {
                        SelectedOrderHeader.LoadFromDto(rst.Data, false);
                    }
                    else
                    {
                        await LoadAsync();
                    }
                }

            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("打开新建窗口失败：" + ex.Message);
            }
        }

        #endregion

        #region Header 快速保存

        /// <summary>
        /// 中文注释：仅提交 Header 字段变更（不涉及行），用于在 Manager 列表内快速修改状态、备注等。
        /// </summary>
        private async Task SaveHeaderAsync()
        {
            await _msg.ShowInfoAsync("Header 快速保存功能待实现，当前仅模拟保存成功的提示。", "提示");
        }

        #endregion

        #region 确认,取消确认

        private async Task ConfirmAsync()
        {
            if (SelectedOrderHeader == null) return;

            var current = SelectedOrderHeader;
            if (current == null)
            {
                await _msg.ShowWarningAsync("请先选择要取消确认的订单。", "操作提示");
                return;
            }

            if (current.ConfirmationStatus == ConfirmationStatusEnum.已确认)
            {
                await _msg.ShowWarningAsync("该订单已确认，禁止重复确认。", "操作提示");
                return;
            }
            var confirm = await _msg.ShowConfirmAsync($"确认选中的订单【{current.OrderNo}】？", "操作提示");
            if (!confirm) return;


            using (BusyScope())
            {
                try
                {
                    var req = new ConfirmationItemRequest
                    {
                        Uid = current.Uid,
                        Version = current.Version
                    };

                    var rst = await _salesOrderApi.ConfirmSalesOrdersAsync(req);
                    if (rst.Success && rst.Data != null)
                    {
                        current.ConfirmationStatus = rst.Data.NewStatus;
                        current.Version = rst.Data.Version; // 本地版本号自增，保持与后端一致，避免后续编辑时的版本冲突
                        StatusMessage = $"订单 {current.OrderNo} 确认成功：{rst.Data.Message}";
                        _msg.ShowCardAsync(StatusMessage, 3500);
                    }
                    else
                    {
                        StatusMessage = "确认失败：" + rst.Data.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "确认异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        private async Task CancelConfirmAsync()
        {
            if (SelectedOrderHeader == null) return;

            var current = SelectedOrderHeader;
            if (current == null)
            {
                await _msg.ShowWarningAsync("请先选择要取消确认的订单。", "操作提示");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(
                $"确认取消确认选中的订单【{current.OrderNo}】？", "取消确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var req = new ConfirmationItemRequest
                    {
                        Uid = current.Uid,
                        Version = current.Version
                    };

                    var rst = await _salesOrderApi.CancelConfirmSalesOrdersAsync(req);
                    if (rst.Success && rst.Data != null)
                    {
                        current.ConfirmationStatus = rst.Data.NewStatus;
                        current.Version = rst.Data.Version > 0
                            ? rst.Data.Version
                            : current.Version + 1;

                        StatusMessage = $"订单 {current.OrderNo} 取消确认成功：{rst.Data.Message}";
                        _msg.ShowCardAsync(StatusMessage, 3500);
                    }
                    else
                    {
                        StatusMessage = $"订单 {current.OrderNo} 取消确认失败：{rst.Message}";
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"订单 {current.OrderNo} 取消确认异常：{ex.Message}";
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        #endregion

        #region 删除勾选

        private async Task DeleteSelectedHeadAsync()
        {
            if (SelectedOrderHeader == null) return;
            var selected = SelectedOrderHeader;
            if (selected.Uid == Guid.Empty)
            {
                OrderHeaders.Delete(selected);
                StatusMessage = "删除成功";
                _msg.ShowCardAsync(StatusMessage, 1000);
                return;
            }

            if (selected.ConfirmationStatus == ConfirmationStatusEnum.已确认)
            {
                await _msg.ShowWarningAsync("已确认的订单禁止删除。", "操作提示");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(
                string.Format("确认删除选中的订单？此操作不可恢复。", selected.OrderNo), "删除确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var rst = await _salesOrderApi.DeleteSalesOrderAsync(selected.Uid);
                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                        return;
                    }

                    OrderHeaders.Delete(selected);
                    CurrentOrderLines.Clear();
                    StatusMessage = "删除成功";
                    _msg.ShowCardAsync(StatusMessage, 2500);
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        #endregion

    }
}