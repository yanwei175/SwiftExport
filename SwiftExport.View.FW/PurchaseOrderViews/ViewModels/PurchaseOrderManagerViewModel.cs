using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.Models;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Mvvm.Helpers;
using SwiftExport.View.FW.PurchaseOrderViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PurchaseOrderViews.ViewModels
{
    /// <summary>
    /// 采购管理工作台 ViewModel。
    /// 中文注释：提供主表查询、明细跟单编辑、完工统计与批量保存能力。
    /// </summary>
    public class PurchaseOrderManagerViewModel : ViewModelBaseEx
    {
        private readonly IPurchaseOrderApi _purchaseOrderApi;
        private readonly IReceiveApi _receiveApi;
        private readonly ISupplierApi _supplierApi;
        private readonly IDialogService _msg;

        public PurchaseOrderManagerViewModel() : this(null, null, null, null)
        {
        }

        public PurchaseOrderManagerViewModel(IPurchaseOrderApi purchaseOrderApi, IReceiveApi receiveApi, ISupplierApi supplierApi, IDialogService msg)
        {
            _purchaseOrderApi = purchaseOrderApi;
            _receiveApi = receiveApi;
            _supplierApi = supplierApi;
            _msg = msg;

            SupplierItems = new ObservableCollection<SupplierDto>();
            HeadItems = new ObservableCollection<PurchaseOrderHeadModel>();
            LineItems = new PurchaseOrderLineList();
            ReceiptItems = new ObservableCollection<PurchaseReceiptRecordModel>();

            // 中文注释：监听行状态变化，实时刷新完工统计提示。
            LineItems.ListStateChanged += RecalculateReadySummary;

            LoadCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            RegisterBusyNotifyCommand(LoadCommand);

            PageChangeCommand = new RelayCommand<PageChangeRequest>(async req =>
            {
                PageSize = req.NewSize;
                PageIndex = req.NewIndex;
                await LoadAsync();
            }, req => IsNotBusy);

            BatchSaveCommand = new RelayCommand(async () => await BatchSaveAsync(), () => IsNotBusy && LineItems.IsDirty);
            RegisterBusyNotifyCommand(BatchSaveCommand);
            LineItems.RegisterDirtyNotifyCommand(BatchSaveCommand);

            QuickReceiveCommand = new RelayCommand(async () => await QuickReceiveAsync(), () => IsNotBusy && SelectedLine != null && SelectedLine.ReadyQty > 0);
            RegisterBusyNotifyCommand(QuickReceiveCommand);

            PrintCommand = new RelayCommand(async () => await PrintAsync(), () => IsNotBusy && SelectedHead != null);
            RegisterBusyNotifyCommand(PrintCommand);

            _ = InitAsync();
        }

        #region 筛选栏字段

        public ObservableCollection<SupplierDto> SupplierItems { get; }

        private string _selectedSupplier;
        public string SelectedSupplier
        {
            get { return _selectedSupplier; }
            set { SetProperty(ref _selectedSupplier, value); }
        }

        public ObservableCollection<CheckableItem<PurchaseOrderStatusEnum>> StatusOptions { get; }=EnumOptionHelper.ToCheckableItems<PurchaseOrderStatusEnum>();


        private int _selectedStatus ;
        public int SelectedStatus
        {
            get { return _selectedStatus; }
            set { SetProperty(ref _selectedStatus, value); }
        }

        private DateTime? _orderDateFrom;
        public DateTime? OrderDateFrom
        {
            get { return _orderDateFrom; }
            set { SetProperty(ref _orderDateFrom, value); }
        }

        private DateTime? _orderDateTo;
        public DateTime? OrderDateTo
        {
            get { return _orderDateTo; }
            set { SetProperty(ref _orderDateTo, value); }
        }

        private string _searchPoNo;
        public string SearchPoNo
        {
            get { return _searchPoNo; }
            set { SetProperty(ref _searchPoNo, value); }
        }

        private string _searchSalesOrderNo;
        public string SearchSalesOrderNo
        {
            get { return _searchSalesOrderNo; }
            set { SetProperty(ref _searchSalesOrderNo, value); }
        }

        #endregion

        #region 主表与明细

        public ObservableCollection<PurchaseOrderHeadModel> HeadItems { get; }

        private PurchaseOrderHeadModel _selectedHead;
        public PurchaseOrderHeadModel SelectedHead
        {
            get { return _selectedHead; }
            set
            {
                if (SetProperty(ref _selectedHead, value))
                {
                    _ = LoadDetailBySelectedHeadAsync();
                }
            }
        }

        public PurchaseOrderLineList LineItems { get; }

        private PurchaseOrderLineModel _selectedLine;
        public PurchaseOrderLineModel SelectedLine
        {
            get { return _selectedLine; }
            set
            {
                if (SetProperty(ref _selectedLine, value))
                {
                    QuickReceiveCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<PurchaseReceiptRecordModel> ReceiptItems { get; }

        /// <summary>
        /// 中文注释：明细全选状态（用于批量快速入库）。
        /// </summary>
        public bool IsAllLineChecked
        {
            get { return LineItems.IsAllChecked; }
            set
            {
                LineItems.IsAllChecked = value;
                RaisePropertyChanged(nameof(IsAllLineChecked));
                RaisePropertyChanged(nameof(CheckedLineCount));
                BatchQuickReceiveCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 中文注释：当前勾选明细条数。
        /// </summary>
        public int CheckedLineCount
        {
            get { return LineItems.CheckedCount; }
        }

        #endregion

        #region 分页与状态

        private int _pageIndex = 1;
        public int PageIndex
        {
            get { return _pageIndex; }
            set { SetProperty(ref _pageIndex, value); }
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get { return _pageSize; }
            set { SetProperty(ref _pageSize, value); }
        }

        private int _totalCount;
        public int TotalCount
        {
            get { return _totalCount; }
            set { SetProperty(ref _totalCount, value); }
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        #endregion

        #region 完工统计

        private decimal _totalReadyGw;
        public decimal TotalReadyGw
        {
            get { return _totalReadyGw; }
            set { SetProperty(ref _totalReadyGw, value); }
        }

        private decimal _totalReadyCbm;
        public decimal TotalReadyCbm
        {
            get { return _totalReadyCbm; }
            set { SetProperty(ref _totalReadyCbm, value); }
        }

        private bool _isContainerSuggestionWarning;
        public bool IsContainerSuggestionWarning
        {
            get { return _isContainerSuggestionWarning; }
            set { SetProperty(ref _isContainerSuggestionWarning, value); }
        }

        private string _containerSuggestionText = "统计提示：正常";
        public string ContainerSuggestionText
        {
            get { return _containerSuggestionText; }
            set { SetProperty(ref _containerSuggestionText, value); }
        }

        #endregion

        #region 命令

        public RelayCommand LoadCommand { get; }
        public RelayCommand<PageChangeRequest> PageChangeCommand { get; }
        public RelayCommand BatchSaveCommand { get; }
        public RelayCommand QuickReceiveCommand { get; }
        public RelayCommand BatchQuickReceiveCommand { get; }
        public RelayCommand PrintCommand { get; }

        #endregion

        /// <summary>
        /// 中文注释：初始化加载供应商与首屏数据。
        /// </summary>
        private async Task InitAsync()
        {
            await LoadSuppliersAsync();
            await LoadAsync();
        }

        /// <summary>
        /// 中文注释：加载供应商下拉数据。
        /// </summary>
        private async Task LoadSuppliersAsync()
        {
            if (_supplierApi == null) return;

            try
            {
                var rst = await _supplierApi.GetAllAsync();
                SupplierItems.Clear();
                if (rst.Success && rst.Data != null)
                {
                    foreach (var item in rst.Data)
                    {
                        SupplierItems.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowWarningAsync("加载供应商数据失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 中文注释：批量快速入库（按勾选行提交）。
        /// 仅提交 ReadyQty > 0 的勾选行，并携带 SoLineId 打通需求池回写。
        /// </summary>
        private async Task BatchQuickReceiveAsync()
        {
            if (_receiveApi == null)
            {
                await _msg.ShowWarningAsync("ReceiveApi 未注入，无法执行批量快速入库。");
                return;
            }

            if (SelectedHead == null)
            {
                await _msg.ShowInfoAsync("请先选择采购订单头。");
                return;
            }

            var checkedLines = LineItems.GetCheckedItems();
            if (checkedLines == null || checkedLines.Count == 0)
            {
                await _msg.ShowInfoAsync("请先勾选明细行。");
                return;
            }

            var validLines = checkedLines.Where(x => x.ReadyQty > 0).ToList();
            if (validLines.Count == 0)
            {
                await _msg.ShowInfoAsync("勾选行中没有 ReadyQty > 0 的数据。");
                return;
            }

            var confirmText = string.Format("确认对 {0} 行执行批量快速入库？", validLines.Count);
            var ok = await _msg.ShowConfirmAsync(confirmText, "批量快速入库确认");
            if (!ok) return;

            using (BusyScope())
            {
                try
                {
                    var request = new PurchaseReceiveExecuteRequestDto
                    {
                        PoNo = SelectedHead.BoNo,
                        OrderNo = SelectedHead.OrderNo,
                        SupplierCode = SelectedHead.SupplierCode,
                        ReceiveDate = DateTime.Now,
                        Handler = Environment.UserName,
                        Remark = "采购管理工作台-批量快速入库",
                        Lines = validLines.Select(x => new ReceiveLineDto
                        {
                            Sku = x.Sku,
                            Qty = x.ReadyQty,
                            Unit = null,
                            PoLineId = x.Uid,
                        }).ToList()
                    };

                    var rst = await _receiveApi.ExecutePurchaseReceiveAsync(request);
                    if (rst.Success)
                    {
                        StatusMessage = "批量快速入库成功，入库单号：" + rst.Data;
                        await _msg.ShowInfoAsync(StatusMessage);

                        await LoadDetailBySelectedHeadAsync();
                        RaisePropertyChanged(nameof(IsAllLineChecked));
                        RaisePropertyChanged(nameof(CheckedLineCount));
                    }
                    else
                    {
                        StatusMessage = "批量快速入库失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量快速入库异常：" + ex.Message;
                    await _msg.ShowWarningAsync(StatusMessage);
                }
            }
        }
     


        /// <summary>
        /// 中文注释：主表分页加载。
        /// </summary>
        private async Task LoadAsync()
        {
            if (_purchaseOrderApi == null)
            {
                StatusMessage = "PurchaseOrderApi 未注入";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var search = new PurchaseOrderSearchDto
                    {
                        SupplierCode = SelectedSupplier,
                        PoNo = string.IsNullOrWhiteSpace(SearchPoNo) ? null : SearchPoNo.Trim(),
                        SalesOrderNo = string.IsNullOrWhiteSpace(SearchSalesOrderNo) ? null : SearchSalesOrderNo.Trim(),
                        OrderStatus = SelectedStatus,
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    };

                    var rst = await _purchaseOrderApi.GetPagedListAsync(search);
                    HeadItems.Clear();

                    if (rst.Success && rst.Data != null)
                    {
                        var filtered = ApplyDateFilter(rst.Data.Items);
                        foreach (var dto in filtered)
                        {
                            HeadItems.Add(dto.Adapt<PurchaseOrderHeadModel>());
                        }

                        TotalCount = rst.Data.TotalCount;
                        StatusMessage = "采购单加载成功，共 " + TotalCount + " 条。";

                        SelectedHead = HeadItems.FirstOrDefault();
                    }
                    else
                    {
                        TotalCount = 0;
                        LineItems.Clear();
                        ReceiptItems.Clear();
                        RecalculateReadySummary();
                        StatusMessage = "采购单加载失败：" + rst.Message;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "采购单加载异常：" + ex.Message;
                }
            }
        }

        /// <summary>
        /// 中文注释：日期范围过滤（前端二次过滤，兼容当前查询 DTO 未提供日期字段）。
        /// </summary>
        private List<PurchaseOrderHeadDto> ApplyDateFilter(List<PurchaseOrderHeadDto> source)
        {
            var result = source ?? new List<PurchaseOrderHeadDto>();

            if (OrderDateFrom.HasValue)
            {
                var from = OrderDateFrom.Value.Date;
                result = result.Where(x => x.OrderDate.HasValue && x.OrderDate.Value.Date >= from).ToList();
            }

            if (OrderDateTo.HasValue)
            {
                var to = OrderDateTo.Value.Date;
                result = result.Where(x => x.OrderDate.HasValue && x.OrderDate.Value.Date <= to).ToList();
            }

            return result;
        }

        /// <summary>
        /// 中文注释：主表选中项变化后自动刷新明细与统计。
        /// </summary>
        private async Task LoadDetailBySelectedHeadAsync()
        {
            LineItems.Clear();
            ReceiptItems.Clear();
            RecalculateReadySummary();

            if (SelectedHead == null || _purchaseOrderApi == null)
            {
                return;
            }

            try
            {
                var rst = await _purchaseOrderApi.GetDetailByOrderNoAsync(SelectedHead.OrderNo);
                if (rst.Success && rst.Data != null)
                {
                    LineItems.LoadFromDto(rst.Data.Lines);
                    BuildVirtualReceiptRecords(rst.Data.Lines);
                    RecalculateReadySummary();
                    StatusMessage = "已加载明细：" + SelectedHead.OrderNo;
                }
                else
                {
                    StatusMessage = "加载明细失败：" + rst.Message;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载明细异常：" + ex.Message;
            }
        }

        /// <summary>
        /// 中文注释：构建虚拟入库记录（预留真实入库接口前的占位实现）。
        /// </summary>
        private void BuildVirtualReceiptRecords(List<PurchaseOrderLineDto> lines)
        {
            ReceiptItems.Clear();
            if (lines == null || lines.Count == 0) return;

            var hasRec = lines.Where(x => x.RecQty > 0).ToList();
            if (hasRec.Count == 0)
            {
                return;
            }

            var index = 1;
            foreach (var line in hasRec)
            {
                ReceiptItems.Add(new PurchaseReceiptRecordModel
                {
                    ReceiptNo = "VGR-" + DateTime.Now.ToString("yyyyMMdd") + "-" + index.ToString("D3"),
                    ReceiptDate = DateTime.Now,
                    Sku = line.Sku,
                    Qty = line.RecQty,
                    Remark = "系统虚拟入库记录"
                });
                index++;
            }
        }

        /// <summary>
        /// 中文注释：批量保存 ReadyQty / FollowExpectedDate / SubNote。
        /// </summary>
        private async Task BatchSaveAsync()
        {
            if (SelectedHead == null)
            {
                await _msg.ShowInfoAsync("请先选择采购订单。");
                return;
            }

            if (!LineItems.IsDirty)
            {
                await _msg.ShowInfoAsync("没有需要保存的更改。");
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var headDto = SelectedHead.Adapt<PurchaseOrderHeadDto>();
                    var allLines = LineItems.Items.Select(x => x.Adapt<PurchaseOrderLineDto>()).ToList();

                    var req = new PurchaseOrderDetailDto
                    {
                        Header = headDto,
                        Lines = allLines
                    };

                    var rst = await _purchaseOrderApi.SaveDetailAsync(req);
                    if (rst.Success)
                    {
                        // 中文注释：保存成功后重新加载明细，重置行状态。
                        await LoadDetailBySelectedHeadAsync();
                        StatusMessage = "批量保存成功。";
                        await _msg.ShowInfoAsync("批量保存成功。");
                    }
                    else
                    {
                        StatusMessage = "批量保存失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量保存异常：" + ex.Message;
                    await _msg.ShowWarningAsync(StatusMessage);
                }
            }
        }

        /// <summary>
        /// 中文注释：预留快速入库命令，把选中行 ReadyQty 传递到虚拟入库界面的占位动作。
        /// </summary>
        private async Task QuickReceiveAsync()
        {
            if (SelectedLine == null)
            {
                await _msg.ShowInfoAsync("请先选择明细行。");
                return;
            }

            if (_receiveApi == null)
            {
                await _msg.ShowWarningAsync("ReceiveApi 未注入，无法执行快速入库。");
                return;
            }

            if (SelectedLine.ReadyQty <= 0)
            {
                await _msg.ShowInfoAsync("当前行 ReadyQty 必须大于 0 才能入库。");
                return;
            }

            if (SelectedHead == null)
            {
                await _msg.ShowInfoAsync("请先选择采购订单头。");
                return;
            }

            using (BusyScope())
            {
                try
                {
                    // 中文注释：快速入库仅提交当前选中行，数量取 ReadyQty。
                    var request = new PurchaseReceiveExecuteRequestDto
                    {
                        PoNo = SelectedHead.BoNo,
                        OrderNo = SelectedHead.OrderNo,
                        SupplierCode = SelectedHead.SupplierCode,
                        ReceiveDate = DateTime.Now,
                        Handler = Environment.UserName,
                        Remark = "采购管理工作台-快速入库",
                        Lines = new List<ReceiveLineDto>
                        {
                            new ReceiveLineDto
                            {
                                Sku = SelectedLine.Sku,
                                Qty = SelectedLine.ReadyQty,
                                Unit = null,
                                PoLineId = SelectedLine.Uid
                            }
                        }
                    };

                    var rst = await _receiveApi.ExecutePurchaseReceiveAsync(request);
                    if (rst.Success)
                    {
                        StatusMessage = "快速入库成功，入库单号：" + rst.Data;
                        await _msg.ShowInfoAsync(StatusMessage);

                        // 中文注释：成功后刷新明细，确保 RecQty 与统计区域立即同步。
                        await LoadDetailBySelectedHeadAsync();
                    }
                    else
                    {
                        StatusMessage = "快速入库失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "快速入库异常：" + ex.Message;
                    await _msg.ShowWarningAsync(StatusMessage);
                }
            }
        }

        /// <summary>
        /// 中文注释：预留打印采购合同命令。
        /// </summary>
        private async Task PrintAsync()
        {
            if (SelectedHead == null)
            {
                await _msg.ShowInfoAsync("请先选择采购订单。");
                return;
            }

            var msg = string.Format("预留功能：将按模板生成采购合同 PDF，订单：{0}", SelectedHead.OrderNo);
            await _msg.ShowInfoAsync(msg);
        }

        /// <summary>
        /// 中文注释：实时计算完工统计（Total GW / Total CBM），并给出定柜提示。
        /// </summary>
        private void RecalculateReadySummary()
        {
            var lines = LineItems.Items.ToList();

            //TotalReadyGw = lines.Sum(x => x.ReadyQty * (x.UnitWeight ?? 0m));
            //TotalReadyCbm = lines.Sum(x => x.ReadyQty * (x.UnitCBM ?? 0m));

            var near28 = Math.Abs(TotalReadyCbm - 28m) <= 1.5m;
            var near68 = Math.Abs(TotalReadyCbm - 68m) <= 2m;

            IsContainerSuggestionWarning = near28 || near68;
            ContainerSuggestionText = IsContainerSuggestionWarning
                ? "建议定柜：总体积接近 28 / 68 CBM"
                : "统计提示：未达到建议定柜区间";
        }
    }
}
