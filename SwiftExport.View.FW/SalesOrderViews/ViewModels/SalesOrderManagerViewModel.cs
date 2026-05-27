using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.SalesOrderViews.Models;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.Services;
using SwiftExport.View.FW.SystemViews.Models;
using SwiftExport.View.FW.WarehouseViews.Models;
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

            OrderHeaders = new SalesOrderHeaderList();
            OrderHeaders.PropertyChanged += OrderHeaders_PropertyChanged;

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

            BatchApproveCommand = new RelayCommand(async () => await BatchApproveAsync(), () => CanBatchApprove);
            RegisterBusyNotifyCommand(BatchApproveCommand);

            BatchRejectCommand = new RelayCommand(async () => await BatchRejectAsync(), () => CanBatchReject);
            RegisterBusyNotifyCommand(BatchRejectCommand);

            DeleteCheckedCommand = new RelayCommand(async () => await DeleteCheckedAsync(), () => HasSelectedItems && IsNotBusy);
            RegisterBusyNotifyCommand(DeleteCheckedCommand);

            InvertCheckCommand = new RelayCommand(InvertCheck);
            ItemCheckedChangedCommand = new RelayCommand(OnItemCheckedChanged);

            // 中文注释：订阅多选选项的 IsSelected 变化，实时刷新 ToggleButton 上的摘要文字。
            foreach (var item in ApprovalStatusOptions)
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CheckableItem<int>.IsSelected)) RaisePropertyChanged(nameof(SelectedApprovalStatuses)); };
            foreach (var item in OrderStatusOptions)
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CheckableItem<int>.IsSelected)) RaisePropertyChanged(nameof(SelectedOrderStatuses)); };

            _ = LoadCustomersAsync();
            _= LoadColorsAsync();
        }

        #endregion

        #region 列表与选中

        /// <summary> 订单头集合，绑定到 Manager DataGrid。</summary>
        public SalesOrderHeaderList OrderHeaders { get; }

        private SalesOrderHeaderModel _selectedOrderHeader;
        /// <summary> 当前选中订单头，切换时刷新命令状态。</summary>
        public SalesOrderHeaderModel SelectedOrderHeader
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
                    _ = LoadLinesByHeadIdAsync(value?.Id ?? 0);
                }
            }
        }

        private SalesOrderLineList _currentOrderLines;
        public SalesOrderLineList CurrentOrderLines
        {
            get => _currentOrderLines;
            set => SetProperty(ref _currentOrderLines, value);
        }
        private SalesOrderLineModel _selectedLine;
        public SalesOrderLineModel SelectedLine
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

        // 中文注释：审批状态多选选项，每项是 CheckableItem<int>，IsSelected 双向绑定到 CheckBox。
        // PropertyChanged 订阅在构造函数中完成，变化时自动通知摘要属性刷新。
        public ObservableCollection<CheckableItem<int>> ApprovalStatusOptions { get; }
            = new ObservableCollection<CheckableItem<int>>
              {
                  new CheckableItem<int> { Value = ApprovalStatusEnum.草稿,   Label = "草稿"   },
                  new CheckableItem<int> { Value = ApprovalStatusEnum.待审批, Label = "待审批" },
                  new CheckableItem<int> { Value = ApprovalStatusEnum.已批准, Label = "已批准" },
                  new CheckableItem<int> { Value = ApprovalStatusEnum.已驳回, Label = "已驳回" },
                  new CheckableItem<int> { Value = ApprovalStatusEnum.已取消, Label = "已取消" },
              };

        // 中文注释：审批状态摘要文字，绑定到 ToggleButton 上的 TextBlock.Text。
        public string SelectedApprovalStatuses =>
            ApprovalStatusOptions.Where(x => x.IsSelected) is var sel && sel.Any()
                ? string.Join("、", sel.Select(x => x.Label))
                : "全部";

        // 中文注释：订单状态多选选项。
        public ObservableCollection<CheckableItem<int>> OrderStatusOptions { get; }
            = new ObservableCollection<CheckableItem<int>>
              {
                  new CheckableItem<int> { Value = SalesOrderStatusEnum.草稿,     Label = "草稿"     },
                  new CheckableItem<int> { Value = SalesOrderStatusEnum.已确认,   Label = "已确认"   },
                  new CheckableItem<int> { Value = SalesOrderStatusEnum.部分发货, Label = "部分发货" },
                  new CheckableItem<int> { Value = SalesOrderStatusEnum.全部发货, Label = "全部发货" },
                  new CheckableItem<int> { Value = SalesOrderStatusEnum.已关闭,   Label = "已关闭"   },
              };

        // 中文注释：订单状态摘要文字。
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

        #region 勾选状态与批量选择

        private bool _isAllChecked;
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (SetProperty(ref _isAllChecked, value))
                {
                    foreach (var item in OrderHeaders.Items)
                        item.IsChecked = value;
                    RefreshSelectionState();
                }
            }
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        private bool _hasSelectedItems;
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            set => SetProperty(ref _hasSelectedItems, value);
        }

        /// <summary> 当前已勾选的订单头快照列表。</summary>
        public List<SalesOrderHeaderModel> SelectedItems
            => OrderHeaders.Items.Where(x => x.IsChecked).ToList();

        #endregion

        #region 命令启用条件

        /// <summary> 批量审批：全部勾选项均为草稿或驳回状态才可操作。</summary>
        public bool CanBatchApprove
        {
            get
            {
                var selected = SelectedItems;
                if (selected.Count == 0) return false;
                return selected.All(x =>
                    x.ApprovalStatus == ApprovalStatusEnum.草稿 ||
                    x.ApprovalStatus == ApprovalStatusEnum.已驳回);
            }
        }

        /// <summary> 批量驳回：全部勾选项均为草稿或待审批状态才可操作。</summary>
        public bool CanBatchReject
        {
            get
            {
                var selected = SelectedItems;
                if (selected.Count == 0) return false;
                return selected.All(x =>
                    x.ApprovalStatus == ApprovalStatusEnum.草稿 ||
                    x.ApprovalStatus == ApprovalStatusEnum.待审批);
            }
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
        public RelayCommand BatchApproveCommand { get; }
        public RelayCommand BatchRejectCommand { get; }
        public RelayCommand DeleteCheckedCommand { get; }
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
                        ApprovalStatuses = ApprovalStatusOptions
                            .Where(x => x.IsSelected)
                            .Select(x => x.Value)
                            .ToList(),
                        OrderStatuses = OrderStatusOptions
                            .Where(x => x.IsSelected)
                            .Select(x => x.Value)
                            .ToList(),
                        
                        BatchCustomerSkuList = BatchCustomerSkuText?.Split(new[] { '\n', '\r', ',', '；', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    };

                    var rst = await _salesOrderApi.GetSalesOrderHeaderPagedListAsync(search);
                    if (rst.Success && rst.Data != null)
                    {
                        OrderHeaders.ReLoadRangeFromDtoList(rst.Data.Items);
                        TotalCount = rst.Data.TotalCount;
                        RefreshSelectionState();
                        SelectedOrderHeader = OrderHeaders.Items.FirstOrDefault();
                        StatusMessage = string.Format("加载成功，共 {0} 条", TotalCount);
                    }
                    else
                    {
                        OrderHeaders.Clear();
                        TotalCount = 0;
                        RefreshSelectionState();
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
        private async Task RefreshSingleOrderAsync(int orderId)
        {
            if (orderId <= 0)
            {
                await LoadAsync();
                return;
            }

            try
            {
                var rst = await _salesOrderApi.GetSalesOrderHeaderPagedListAsync(new SalesOrderManagerSearchDto
                {
                    OrderId = orderId,
                    PageIndex = 1,
                    PageSize = 1
                });

                if (rst?.Success == true && rst.Data?.Items?.Count > 0)
                {
                    var updated = rst.Data.Items[0];
                    var existing = OrderHeaders.Items.FirstOrDefault(x => x.Id == orderId);
                    if (existing != null)
                    {
                        existing.LoadFromDto(updated, false);
                        SelectedOrderHeader = existing;
                        RaisePropertyChanged(nameof(StatusSummaryText));
                    }
                    else
                    {
                        await LoadAsync();
                    }
                }
            }
            catch
            {
                await LoadAsync();
            }
        }

        private async Task LoadLinesByHeadIdAsync(int orderId)
        {

            using (BusyScope())
            {
                try
                {
                    if (orderId <= 0)
                    {
                        CurrentOrderLines.Clear();
                        return;
                    }

                    var rst = await _salesOrderApi.GetSalesOrderLinesAsync(orderId);
                    if (rst?.Success == true && rst.Data != null)
                    {
                        CurrentOrderLines = new SalesOrderLineList();
                        CurrentOrderLines.ReLoadRangeFromDtoList(rst.Data);
                    }
                    else
                    {
                        CurrentOrderLines = new SalesOrderLineList();
                        StatusMessage = "加载订单行失败：" + rst?.Message;
                    }
                }
                catch (Exception ex)
                {
                    CurrentOrderLines = new SalesOrderLineList();
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
                var vm=IocProvider.GetService<SalesOrderDetailViewModel>();
                vm.InitForCreate();
                var win = new SalesOrderDetailWindow(vm);
                var result = win.ShowDialog();
                if (result == true && vm.ResultData > 0)
                {
                    var rst= await _salesOrderApi.GetSalesOrderDetailAsync(vm.ResultData);
                    if(rst?.Success == true && rst.Data != null)
                    {
                        SelectedOrderHeader.LoadFromDto(rst.Data.Header, false);
                        CurrentOrderLines.ReLoadRangeFromDtoList(rst.Data.Lines);
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

            var orderId = SelectedOrderHeader.Id;
            if (orderId <= 0) return;
            try
            {
                var vm = IocProvider.GetService<SalesOrderDetailViewModel>();
                await vm.InitForEditAsync(SelectedOrderHeader);
                var win = new SalesOrderDetailWindow(vm);
                var result = win.ShowDialog();
                if (result == true && vm.OrderHeader != null)
                {
                    _ = RefreshSingleOrderAsync(vm.OrderHeader.Id);
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
            if (SelectedOrderHeader == null) return;
            if (_salesOrderApi == null)
            {
                StatusMessage = "SalesOrderApi 未注册";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var changeSet = OrderHeaders.GetChangeSet();
                    if (!changeSet.HasChanges)
                    {
                        StatusMessage = "没有变更，无需保存";
                        return;
                    }

                    // 中文注释：将 Model 列表映射为 DTO 列表后构建保存请求。
                    var saveDto = new SalesOrderManagerSaveDto
                    {
                        CurrentOrderId = SelectedOrderHeader.Id,
                        HeaderChanges = new BatchRequestGeneric<SalesOrderHeaderDto>
                        {
                            AddList = changeSet.AddList.Select(m => m.Adapt<SalesOrderHeaderDto>()).ToList(),
                            UpdateList = changeSet.UpdateList.Select(m => m.Adapt<SalesOrderHeaderDto>()).ToList(),
                            DeleteIdList = changeSet.DeleteIdList
                        },
                        LineChanges = new BatchRequestGeneric<SalesOrderLineDto>()
                    };

                    var rst = await _salesOrderApi.SaveSalesOrderManagerAsync(saveDto);
                    if (rst.Success)
                    {
                        StatusMessage = "保存成功";
                        _msg?.ShowCardAsync(StatusMessage, 2500);
                        SelectedOrderHeader.CommitChanges();
                        RaisePropertyChanged(nameof(CanSave));
                        SaveCommand?.RaiseCanExecuteChanged();
                    }
                    else
                    {
                        StatusMessage = "保存失败：" + rst.Message;
                        await _msg?.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存异常：" + ex.Message;
                    await _msg?.ShowErrorAsync(StatusMessage);
                }
            }
        }

        #endregion

        #region 批量审批 / 驳回

        private async Task BatchApproveAsync()
        {
            var selected = SelectedItems;
            if (selected.Count == 0)
            {
                await _msg.ShowWarningAsync("请先勾选要审批的订单。", "操作提示");
                return;
            }

            var invalid = selected.Where(x => x.ApprovalStatus == ApprovalStatusEnum.已批准).ToList();
            if (invalid.Count > 0)
            {
                await _msg.ShowWarningAsync("选中订单中包含已审批订单，禁止重复审批。", "操作提示");
                return;
            }

            if (!CanBatchApprove)
            {
                await _msg.ShowWarningAsync("只有草稿/驳回状态的订单才可审批。", "操作提示");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(
                string.Format("确认审批选中的 {0} 条订单？", selected.Count), "批量审批确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var ids = selected.Select(x => x.Id).Distinct().ToList();
                    var rst = await _salesOrderApi.BatchApproveSalesOrdersAsync(ids);
                    if (rst.Success && (rst.Data == null || rst.Data.Count == 0))
                    {
                        StatusMessage = "批量审批成功。";
                        _msg.ShowCardAsync(StatusMessage, 3500);
                        await LoadAsync();
                    }
                    else if (rst.Data != null && rst.Data.Count > 0)
                    {
                        // 中文注释：Data 中存放的是逐条失败原因，展示第一条即可
                        StatusMessage = "批量审批部分失败：" + rst.Data[0];
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "批量审批失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量审批异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        private async Task BatchRejectAsync()
        {
            var selected = SelectedItems;
            if (selected.Count == 0)
            {
                await _msg.ShowWarningAsync("请先勾选要驳回的订单。", "操作提示");
                return;
            }

            if (!CanBatchReject)
            {
                await _msg.ShowWarningAsync("只有草稿/待审批状态的订单才可驳回。", "操作提示");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(
                string.Format("确认驳回选中的 {0} 条订单？", selected.Count), "批量驳回确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var ids = selected.Select(x => x.Id).Distinct().ToList();
                    var rst = await _salesOrderApi.BatchRejectSalesOrdersAsync(ids);
                    if (rst.Success && (rst.Data == null || rst.Data.Count == 0))
                    {
                        StatusMessage = "批量驳回成功。";
                        _msg.ShowCardAsync(StatusMessage, 3000);
                        await LoadAsync();
                    }
                    else if (rst.Data != null && rst.Data.Count > 0)
                    {
                        StatusMessage = "批量驳回部分失败：" + rst.Data[0];
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "批量驳回失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量驳回异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        #endregion

        #region 删除勾选

        private async Task DeleteCheckedAsync()
        {
            var selected = SelectedItems;
            if (selected.Count == 0) return;

            var confirm = await _msg.ShowConfirmAsync(
                string.Format("确认删除选中的 {0} 条订单？此操作不可恢复。", selected.Count), "删除确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var ids = selected.Select(x => x.Id).Where(id => id > 0).Distinct().ToList();
                    if (ids.Count > 0)
                    {
                        var rst = await _salesOrderApi.BatchDeleteSalesOrderAsync(ids);
                        if (!rst.Success)
                        {
                            StatusMessage = "删除失败：" + rst.Message;
                            await _msg.ShowWarningAsync(StatusMessage, "操作失败");
                            return;
                        }
                    }

                    foreach (var item in selected.Where(x => x.Id <= 0))
                        OrderHeaders.Delete(item);

                    StatusMessage = "删除成功";
                    _msg.ShowCardAsync(StatusMessage, 2500);
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "操作异常");
                }
            }
        }

        #endregion

        #region 勾选辅助

        private void InvertCheck()
        {
            foreach (var item in OrderHeaders.Items)
                item.IsChecked = !item.IsChecked;
            RefreshSelectionState();
        }

        private void OnItemCheckedChanged()
        {
            RefreshSelectionState();
        }

        private void RefreshSelectionState()
        {
            var count = OrderHeaders.Items.Count(x => x.IsChecked);
            SelectedCount = count;
            HasSelectedItems = count > 0;

            _isAllChecked = OrderHeaders.Items.Count > 0 && count == OrderHeaders.Items.Count;
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(CanBatchApprove));
            RaisePropertyChanged(nameof(CanBatchReject));

            BatchApproveCommand?.RaiseCanExecuteChanged();
            BatchRejectCommand?.RaiseCanExecuteChanged();
            DeleteCheckedCommand?.RaiseCanExecuteChanged();
        }

        #endregion

        #region 事件回调

        private void OrderHeaders_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OrderHeaders.IsDirty) ||
                e.PropertyName == nameof(OrderHeaders.IsAllValid))
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand?.RaiseCanExecuteChanged();
            }
        }

        #endregion
    }
}