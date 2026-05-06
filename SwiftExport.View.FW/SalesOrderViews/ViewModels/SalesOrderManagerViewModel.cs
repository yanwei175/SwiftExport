using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SalesOrderViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.ViewModels
{
    /// <summary>
    /// 销售订单管理 ViewModel（支持审批流、状态管理、主从联动）。
    /// </summary>
    public class SalesOrderManagerViewModel : ViewModelBaseEx
    {
       

        #region 字段 / 事件

        private readonly ISalesOrderApi _salesOrderApi;
        private readonly IDialogService _msg;

        /// <summary>
        /// 当选中订单变化并明细刷新后触发，供 Tab 页刷新联动。
        /// </summary>
        public event Action SelectedOrderHeaderChanged;

        #endregion

        #region 构造函数

        [Obsolete("仅供 XAML 设计器使用", true)]
        internal SalesOrderManagerViewModel()
        {
        }

        public SalesOrderManagerViewModel(ISalesOrderApi salesOrderApi, IDialogService dialogService, SalesOrderDetailViewModel salesOrderDetailViewModel)
        {
            _salesOrderApi = salesOrderApi;
            _msg = dialogService;

            OrderHeaders = new SalesOrderHeaderList();
            CurrentOrderLines = new SalesOrderLineList();
            EmbeddedDetailViewModel = salesOrderDetailViewModel;

            OrderHeaders.PropertyChanged += OrderHeaders_PropertyChanged;
            CurrentOrderLines.PropertyChanged += CurrentOrderLines_PropertyChanged;
            CurrentOrderLines.Items.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(CanSave));
                RaisePropertyChanged(nameof(StatusSummaryText));
                SaveCommand?.RaiseCanExecuteChanged();
            };

            LoadCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            RegisterBusyNotifyCommand(LoadCommand);

            AddLineCommand = new RelayCommand(AddLine);
            DeleteLineCommand = new RelayCommand(DeleteLine, () => SelectedLine != null);

            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave);
            RegisterBusyNotifyCommand(SaveCommand);

            BatchApproveCommand = new RelayCommand(async () => await BatchApproveAsync(), () => CanBatchApprove);
            RegisterBusyNotifyCommand(BatchApproveCommand);

            BatchRejectCommand = new RelayCommand(async () => await BatchRejectAsync(), () => CanBatchReject);
            RegisterBusyNotifyCommand(BatchRejectCommand);

            // 主表工具栏兼容命令。
            AddNewRowCommand = new RelayCommand(AddNewRow);
            BatchSaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave);
            DeleteCheckedCommand = new RelayCommand(DeleteChecked, () => HasSelectedItems);
            InvertCheckCommand = new RelayCommand(InvertCheck);
            ItemCheckedChangedCommand = new RelayCommand(OnItemCheckedChanged);

            //// 默认筛选项。
            //ApprovalStatusOptions = new ObservableCollection<string>
            //{
            //    ApprovalStates.Draft,
            //    ApprovalStates.Pending,
            //    ApprovalStates.Approved,
            //    ApprovalStates.Rejected
            //};

            //OrderStatusOptions = new ObservableCollection<string>
            //{
            //    OrderStates.Open,
            //    OrderStates.Completed,
            //    OrderStates.Closed
            //};
        }

        #endregion

        #region 列表与选中

        public SalesOrderHeaderList OrderHeaders { get; }
        public SalesOrderLineList CurrentOrderLines { get; }
        /// <summary>
        /// 直接嵌入 Tab 的订单详情 ViewModel。
        /// </summary>
        public SalesOrderDetailViewModel EmbeddedDetailViewModel { get; }

        private SalesOrderHeaderModel _selectedOrderHeader;
        /// <summary>
        /// 当前选中订单头，变化时自动加载订单行并通知详情页刷新。
        /// </summary>
        public SalesOrderHeaderModel SelectedOrderHeader
        {
            get => _selectedOrderHeader;
            set
            {
                if (SetProperty(ref _selectedOrderHeader, value))
                {
                    LoadOrderLinesForSelectedHeader();
                    RaisePropertyChanged(nameof(CanSave));
                    RaisePropertyChanged(nameof(StatusSummaryText));
                    SaveCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private SalesOrderLineModel _selectedLine;
        public SalesOrderLineModel SelectedLine
        {
            get => _selectedLine;
            set
            {
                if (SetProperty(ref _selectedLine, value))
                    DeleteLineCommand?.RaiseCanExecuteChanged();
            }
        }

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

        public ObservableCollection<string> ApprovalStatusOptions { get; }
        public ObservableCollection<string> OrderStatusOptions { get; }

        public ObservableCollection<string> SelectedApprovalStatuses { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> SelectedOrderStatuses { get; } = new ObservableCollection<string>();

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

        /// <summary>
        /// 底部状态栏汇总：当前选中订单行数与金额。
        /// </summary>
        public string StatusSummaryText
        {
            get
            {
                var lineCount = CurrentOrderLines.Items.Count;
                var totalAmount = SelectedOrderHeader != null ? SelectedOrderHeader.TotalAmountOriginalCurrency : 0m;
                return string.Format("当前订单汇总：行数 {0}，总金额 {1:N2}", lineCount, totalAmount);
            }
        }

        #endregion

        #region 选择状态（主表勾选）

        private bool _isAllChecked;
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (SetProperty(ref _isAllChecked, value))
                {
                    foreach (var item in OrderHeaders.Items)
                    {
                        item.IsChecked = value;
                    }
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

        /// <summary>
        /// 当前勾选订单集合。
        /// </summary>
        public List<SalesOrderHeaderModel> SelectedItems => OrderHeaders.Items.Where(x => x.IsChecked).ToList();

        #endregion

        #region 按钮可用性

        /// <summary>
        /// 批量审批可用条件：有选中项，且所有选中项审批状态只能是草稿或待审核。
        /// </summary>
        public bool CanBatchApprove
        {
            get
            {
                var selected = SelectedItems;
                if (selected.Count == 0) return false;
                return selected.All(x => x.ApprovalStatus == ApprovalStatusEnum.草稿 || x.ApprovalStatus == ApprovalStatusEnum.待审批);
            }
        }

        /// <summary>
        /// 批量驳回可用条件：有选中项，且所有选中项审批状态只能是草稿或待审核。
        /// </summary>
        public bool CanBatchReject
        {
            get
            {
                var selected = SelectedItems;
                if (selected.Count == 0) return false;
                return selected.All(x => x.ApprovalStatus == ApprovalStatusEnum.草稿 || x.ApprovalStatus == ApprovalStatusEnum.待审批);
            }
        }

        public bool CanSave =>
            SelectedOrderHeader != null &&
            SelectedOrderHeader.IsValid &&
            CurrentOrderLines.IsAllValid &&
            (SelectedOrderHeader.State != ModelState.Unchanged ||
             OrderHeaders.IsDirty ||
             CurrentOrderLines.IsDirty);

        #endregion

        #region 命令

        public RelayCommand LoadCommand { get; }

        public RelayCommand AddLineCommand { get; }
        public RelayCommand DeleteLineCommand { get; }
        public RelayCommand SaveCommand { get; }

        public RelayCommand BatchApproveCommand { get; }
        public RelayCommand BatchRejectCommand { get; }

        public RelayCommand AddNewRowCommand { get; }
        public RelayCommand BatchSaveCommand { get; }
        public RelayCommand DeleteCheckedCommand { get; }
        public RelayCommand InvertCheckCommand { get; }
        public RelayCommand ItemCheckedChangedCommand { get; }

        #endregion

        #region 加载与主从联动

        private async Task LoadAsync()
        {
            if (_salesOrderApi == null)
            {
                StatusMessage = "SalesOrderApi 未注入";
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
                        OrderDateFrom = OrderDateFrom,
                        OrderDateTo = OrderDateTo,
                        ApprovalStatuses = SelectedApprovalStatuses.ToList(),
                        OrderStatuses = SelectedOrderStatuses.ToList(),
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    };

                    var rst = await _salesOrderApi.GetSalesOrderHeaderPagedListAsync(search);
                    if (rst.Success && rst.Data != null)
                    {
                        OrderHeaders.LoadFromDto(rst.Data.Items);
                        TotalCount = rst.Data.TotalCount;
                        RefreshSelectionState();

                        SelectedOrderHeader = OrderHeaders.Items.FirstOrDefault();
                        StatusMessage = string.Format("加载成功，共 {0} 条。", TotalCount);
                    }
                    else
                    {
                        OrderHeaders.LoadFromDto(null);
                        CurrentOrderLines.LoadFromDto(null);
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

        private void LoadOrderLinesForSelectedHeader()
        {
            if (_selectedOrderHeader == null)
            {
                CurrentOrderLines.BindHeader(null);
                CurrentOrderLines.LoadFromDto(null);
                SelectedOrderHeaderChanged?.Invoke();
                SyncEmbeddedDetailViewModel();
                return;
            }

            CurrentOrderLines.BindHeader(_selectedOrderHeader);

            // 中文注释：管理页详情加载为异步，不阻塞选中切换交互。
            _ = LoadLinesAsync(_selectedOrderHeader.Id);
        }

        private async Task LoadLinesAsync(int orderId)
        {
            if (_salesOrderApi == null || orderId <= 0)
            {
                CurrentOrderLines.LoadFromDto(null);
                RaisePropertyChanged(nameof(StatusSummaryText));
                SelectedOrderHeaderChanged?.Invoke();
                SyncEmbeddedDetailViewModel();
                return;
            }

            try
            {
                var rst = await _salesOrderApi.GetSalesOrderLinesAsync(orderId);
                if (rst.Success)
                {
                    CurrentOrderLines.LoadFromDto(rst.Data);
                }
                else
                {
                    CurrentOrderLines.LoadFromDto(null);
                    StatusMessage = "加载订单行失败：" + rst.Message;
                }
            }
            catch (Exception ex)
            {
                CurrentOrderLines.LoadFromDto(null);
                StatusMessage = "加载订单行异常：" + ex.Message;
            }
            finally
            {
                RaisePropertyChanged(nameof(StatusSummaryText));
                SelectedOrderHeaderChanged?.Invoke();
                SyncEmbeddedDetailViewModel();
            }
        }

        /// <summary>
        /// 同步主从管理页当前数据到内嵌详情页 VM。
        /// </summary>
        private void SyncEmbeddedDetailViewModel()
        {
            if (EmbeddedDetailViewModel == null)
                return;

            if (SelectedOrderHeader == null)
            {
                EmbeddedDetailViewModel.LoadFromDto(new SalesOrderDetailDto
                {
                    Header = new SalesOrderHeaderDto(),
                    Lines = new List<SalesOrderLineDto>()
                });
                return;
            }

            var dto = new SalesOrderDetailDto
            {
                Header = SelectedOrderHeader.Adapt<SalesOrderHeaderDto>(),
                Lines = CurrentOrderLines.Items.Select(x => x.Adapt<SalesOrderLineDto>()).ToList()
            };
            EmbeddedDetailViewModel.LoadFromDto(dto);
        }

        #endregion

        #region 批量审批流

        private async Task BatchApproveAsync()
        {
            var selected = SelectedItems;
            if (selected.Count == 0)
            {
                await _msg.ShowWarningAsync("请先勾选要审批的订单。", "批量审批");
                return;
            }

            // 中文注释：拦截已审核订单，禁止重复审批。
            var invalid = selected.Where(x => x.ApprovalStatus == ApprovalStatusEnum.已批准).ToList();
            if (invalid.Count > 0)
            {
                await _msg.ShowWarningAsync("选中订单中包含“已审核”单据，禁止重复审批。", "批量审批");
                return;
            }

            if (!CanBatchApprove)
            {
                await _msg.ShowWarningAsync("仅允许审批草稿/待审核状态订单。", "批量审批");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(string.Format("确认批量审批 {0} 张订单？", selected.Count), "批量审批确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var ids = selected.Select(x => x.Id).Distinct().ToList();
                    var rst = await _salesOrderApi.BatchApproveSalesOrdersAsync(ids);
                    if (rst.Success)
                    {
                        StatusMessage = "批量审批成功，已触发需求池拆解。";
                        _msg.ShowCardAsync(StatusMessage, 3500);
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "批量审批失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "批量审批");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量审批异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "批量审批");
                }
            }
        }

        private async Task BatchRejectAsync()
        {
            var selected = SelectedItems;
            if (selected.Count == 0)
            {
                await _msg.ShowWarningAsync("请先勾选要驳回的订单。", "批量驳回");
                return;
            }

            if (!CanBatchReject)
            {
                await _msg.ShowWarningAsync("仅允许驳回草稿/待审核状态订单。", "批量驳回");
                return;
            }

            var confirm = await _msg.ShowConfirmAsync(string.Format("确认批量驳回 {0} 张订单？", selected.Count), "批量驳回确认");
            if (!confirm) return;

            using (BusyScope())
            {
                try
                {
                    var ids = selected.Select(x => x.Id).Distinct().ToList();
                    var rst = await _salesOrderApi.BatchRejectSalesOrdersAsync(ids);
                    if (rst.Success)
                    {
                        StatusMessage = "批量驳回成功。";
                        _msg.ShowCardAsync(StatusMessage, 3000);
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "批量驳回失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage, "批量驳回");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量驳回异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage, "批量驳回");
                }
            }
        }

        #endregion

        #region 明细编辑

        private void AddLine()
        {
            if (SelectedOrderHeader == null)
            {
                _msg?.ShowWarningAsync("请先选择一个订单");
                return;
            }

            var line = new SalesOrderLineModel();
            line.MarkAsAdded();

            CurrentOrderLines.Items.Add(line);
            SelectedLine = line;
        }

        private void DeleteLine()
        {
            if (SelectedLine == null)
                return;

            CurrentOrderLines.Remove(SelectedLine);
            SelectedLine = null;
        }

        private async Task SaveAsync()
        {
            if (SelectedOrderHeader == null)
            {
                if (_msg != null)
                    await _msg.ShowWarningAsync("请先选择一个订单");
                return;
            }

            var asyncError = await CurrentOrderLines.ValidateAllAsync();
            if (!string.IsNullOrEmpty(asyncError))
            {
                if (_msg != null)
                    await _msg.ShowWarningAsync(asyncError);
                return;
            }

            if (_salesOrderApi == null)
            {
                StatusMessage = "SalesOrderApi 未注入";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var saveDto = new SalesOrderManagerSaveDto
                    {
                        CurrentOrderId = SelectedOrderHeader.Id,
                        HeaderChanges = OrderHeaders.GetChangeSet().Adapt<BatchRequestGeneric<SalesOrderHeaderDto>>(),
                        LineChanges = CurrentOrderLines.GetChangeSet().Adapt<BatchRequestGeneric<SalesOrderLineDto>>()
                    };

                    var rst = await _salesOrderApi.SaveSalesOrderManagerAsync(saveDto);
                    if (rst.Success)
                    {
                        StatusMessage = "保存成功";
                        _msg.ShowCardAsync(StatusMessage, 2500);

                        SelectedOrderHeader.CommitChanges();
                        foreach (var line in CurrentOrderLines.Items)
                        {
                            line.CommitChanges();
                        }

                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "保存失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        #endregion

        #region 主表勾选操作

        private void AddNewRow()
        {
            var newHeader = new SalesOrderHeaderModel();
            newHeader.MarkAsAdded();
            OrderHeaders.Items.Insert(0, newHeader);
            SelectedOrderHeader = newHeader;
            RefreshSelectionState();
        }

        private void DeleteChecked()
        {
            var selected = SelectedItems;
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                OrderHeaders.Delete(item);
            }

            RefreshSelectionState();
            RaisePropertyChanged(nameof(CanSave));
            SaveCommand?.RaiseCanExecuteChanged();
        }

        private void InvertCheck()
        {
            foreach (var item in OrderHeaders.Items)
            {
                item.IsChecked = !item.IsChecked;
            }

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

        private void CurrentOrderLines_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CurrentOrderLines.IsDirty) ||
                e.PropertyName == nameof(CurrentOrderLines.IsAllValid))
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand?.RaiseCanExecuteChanged();
            }
        }

        #endregion
    }
}
