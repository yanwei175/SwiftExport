using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.OrderDecomposeViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.OrderDecomposeViews.ViewModels
{
    /// <summary>
    /// 需求池管理页 ViewModel（背靠背采购任务清单模式）。
    /// </summary>
    public class DependentDemandManagerViewModel : ViewModelBaseEx
    {
        private readonly IDependentDemandApi _decomposeApi;
        private readonly IDialogService _msg;

        public DependentDemandManagerViewModel() : this(null, null)
        {
        }

        public DependentDemandManagerViewModel(IDependentDemandApi decomposeApi, IDialogService msg)
        {
            _decomposeApi = decomposeApi;
            _msg = msg;

            BridgeList = new DependentDemandList();

            LoadCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            RegisterBusyNotifyCommand(LoadCommand);

            TriggerDecomposeCommand = new RelayCommand(async () => await TriggerDecomposeAsync(), () => IsNotBusy && !string.IsNullOrWhiteSpace(SourceSoNoInput));
            RegisterBusyNotifyCommand(TriggerDecomposeCommand);

            BatchTriggerDecomposeCommand = new RelayCommand(async () => await BatchTriggerDecomposeAsync(), () => IsNotBusy && !string.IsNullOrWhiteSpace(BatchSourceSoNosInput));
            RegisterBusyNotifyCommand(BatchTriggerDecomposeCommand);

            // 工作台批量下达操作命令。
            BatchIssuePurchaseCommand = new RelayCommand(async () => await BatchIssuePurchaseAsync(), () => IsNotBusy && DetailItems.Any());
            RegisterBusyNotifyCommand(BatchIssuePurchaseCommand);

            BatchIssueProductionCommand = new RelayCommand(async () => await BatchIssueProductionAsync(), () => IsNotBusy && DetailItems.Any());
            RegisterBusyNotifyCommand(BatchIssueProductionCommand);

            // 目标执行单跳转与手工指定供应商。
            OpenTargetCommand = new RelayCommand<DependentDemandModel>(OnOpenTarget);
            SetSupplierCommand = new RelayCommand<DependentDemandModel>(async x => await OnSetSupplierAsync(x));

            // 一键转采购命令。
            ConvertSelectedSourceToPurchaseCommand = new RelayCommand(async () => await ConvertSelectedSourceToPurchaseAsync(), () => IsNotBusy && !string.IsNullOrWhiteSpace(SelectedSourceNo));
            RegisterBusyNotifyCommand(ConvertSelectedSourceToPurchaseCommand);

            // 中文注释：背靠背任务看板默认仅展示待处理任务。
            StatusFlag = RequirementStatusEnums.待处理;
        }

        public DependentDemandList BridgeList { get; }

        /// <summary>
        /// 左侧来源单任务列表（按日期倒序）。
        /// </summary>
        public ObservableCollection<SourceNoTaskItem> SourceTasks { get; } = new ObservableCollection<SourceNoTaskItem>();

        /// <summary>
        /// 右侧明细列表（按左侧来源单过滤后）。
        /// </summary>
        public ObservableCollection<DependentDemandModel> DetailItems { get; } = new ObservableCollection<DependentDemandModel>();

        private SourceNoTaskItem _selectedSourceTask;
        public SourceNoTaskItem SelectedSourceTask
        {
            get => _selectedSourceTask;
            set
            {
                if (SetProperty(ref _selectedSourceTask, value))
                {
                    // 中文注释：切换左侧来源单时，自动刷新右侧明细。
                    SelectedSourceNo = value == null ? null : value.SourceNo;
                    ApplyDetailFilter();
                }
            }
        }

        private string _selectedSourceNo;
        /// <summary>
        /// 当前选中的来源单号（右侧明细过滤依据）。
        /// </summary>
        public string SelectedSourceNo
        {
            get => _selectedSourceNo;
            set
            {
                if (SetProperty(ref _selectedSourceNo, value))
                {
                    ConvertSelectedSourceToPurchaseCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private DependentDemandModel _selectedItem;
        public DependentDemandModel SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _keyword;
        public string Keyword
        {
            get => _keyword;
            set => SetProperty(ref _keyword, value);
        }

        private string _sourceNo;
        /// <summary>
        /// 查询条件：来源单号（可选）。
        /// </summary>
        public string SourceNo
        {
            get => _sourceNo;
            set => SetProperty(ref _sourceNo, value);
        }

        private string _sourceType;
        /// <summary>
        /// 查询条件：来源类型。
        /// </summary>
        public string SourceType
        {
            get => _sourceType;
            set => SetProperty(ref _sourceType, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _supplyType;
        public string SupplyType
        {
            get => _supplyType;
            set => SetProperty(ref _supplyType, value);
        }

        private int? _bomLevel;
        /// <summary>
        /// 查询条件：BOM 层级过滤（成品/子件）。
        /// </summary>
        public int? BomLevel
        {
            get => _bomLevel;
            set => SetProperty(ref _bomLevel, value);
        }

        private string _supplierKeyword;
        /// <summary>
        /// 查询条件：供应商（编码/名称）。
        /// </summary>
        public string SupplierKeyword
        {
            get => _supplierKeyword;
            set => SetProperty(ref _supplierKeyword, value);
        }

        private string _statusFlag;
        public string StatusFlag
        {
            get => _statusFlag;
            set
            {
                if (SetProperty(ref _statusFlag, value))
                {
                    RaisePropertyChanged(nameof(ShowCompleted));
                }
            }
        }

        /// <summary>
        /// 是否显示已完成任务（StatusFlag 为空表示不过滤）。默认 false。
        /// </summary>
        public bool ShowCompleted
        {
            get => string.IsNullOrWhiteSpace(StatusFlag);
            set => StatusFlag = value ? string.Empty : RequirementStatusEnums.待处理;
        }

        private bool? _isKitted;
        public bool? IsKitted
        {
            get => _isKitted;
            set => SetProperty(ref _isKitted, value);
        }

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set => SetProperty(ref _pageIndex, value);
        }

        private int _pageSize = 200;
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

        private string _sourceSoNoInput;
        /// <summary>
        /// 高级维护：单号拆解输入。
        /// </summary>
        public string SourceSoNoInput
        {
            get => _sourceSoNoInput;
            set
            {
                if (SetProperty(ref _sourceSoNoInput, value))
                {
                    TriggerDecomposeCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _batchSourceSoNosInput;
        /// <summary>
        /// 高级维护：批量拆解输入。
        /// </summary>
        public string BatchSourceSoNosInput
        {
            get => _batchSourceSoNosInput;
            set
            {
                if (SetProperty(ref _batchSourceSoNosInput, value))
                {
                    BatchTriggerDecomposeCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand LoadCommand { get; }
        public RelayCommand TriggerDecomposeCommand { get; }
        public RelayCommand BatchTriggerDecomposeCommand { get; }
        public RelayCommand BatchIssuePurchaseCommand { get; }
        public RelayCommand BatchIssueProductionCommand { get; }
        public RelayCommand ConvertSelectedSourceToPurchaseCommand { get; }
        public RelayCommand<DependentDemandModel> OpenTargetCommand { get; }
        public RelayCommand<DependentDemandModel> SetSupplierCommand { get; }

        /// <summary>
        /// 当前筛选结果的平均齐套率（百分比文本）。
        /// </summary>
        private string _avgKittingRateText = "平均齐套率：0.00%";
        public string AvgKittingRateText
        {
            get => _avgKittingRateText;
            set => SetProperty(ref _avgKittingRateText, value);
        }

        private async Task LoadAsync()
        {
            if (_decomposeApi == null)
            {
                StatusMessage = "DependentDemandApi 未注入";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var search = new DependentDemandSearchDto
                    {
                        Keyword = Keyword,
                        SourceNo = SourceNo,
                        SourceType = SourceType,
                        Sku = Sku,
                        SupplyType = SupplyType,
                        BomLevel = BomLevel,
                        SupplierKeyword = SupplierKeyword,
                        StatusFlag = StatusFlag,
                        IsKitted = IsKitted,
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    };

                    var result = await _decomposeApi.GetPagedListAsync(search);
                    if (result.Success && result.Data != null)
                    {
                        BridgeList.ReLoadRangeFromDtoList<DependentDemandDto>(result.Data.Items);
                        TotalCount = result.Data.TotalCount;

                        RefreshSourceTasks();
                        ApplyDetailFilter();
                        RecalculateAvgKittingRate();

                        StatusMessage = "加载成功，共 " + TotalCount + " 条。";
                    }
                    else
                    {
                        BridgeList.Clear();
                        SourceTasks.Clear();
                        DetailItems.Clear();
                        TotalCount = 0;
                        AvgKittingRateText = "平均齐套率：0.00%";
                        StatusMessage = "加载失败：" + result.Message;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常：" + ex.Message;
                }
            }
        }

        /// <summary>
        /// 批量下达采购单（入口提示）。
        /// </summary>
        private async Task BatchIssuePurchaseAsync()
        {
            var pendingRows = DetailItems.Where(x => IsPurchaseSupplyType(x.SupplyType)).ToList();
            await _msg.ShowInfoAsync("已触发批量下达采购单入口，候选行数：" + pendingRows.Count);
        }

        /// <summary>
        /// 批量下达生产单（入口提示）。
        /// </summary>
        private async Task BatchIssueProductionAsync()
        {
            var pendingRows = DetailItems.Where(x => string.Equals(x.SupplyType, "Produce", StringComparison.OrdinalIgnoreCase)).ToList();
            await _msg.ShowInfoAsync("已触发批量下达生产单入口，候选行数：" + pendingRows.Count);
        }

        /// <summary>
        /// 打开目标执行单：若为空提示可转采购/工单，否则提示跳转目标。
        /// </summary>
        private void OnOpenTarget(DependentDemandModel row)
        {
            if (row == null) return;

            if (string.IsNullOrWhiteSpace(row.TargetNo))
            {
                var action = string.Equals(row.SupplyType, "Produce", StringComparison.OrdinalIgnoreCase) ? "转工单" : "转采购单";
                _msg.ShowCardAsync("当前行暂无目标单，可执行“" + action + "”逻辑。", 2500);
                return;
            }

            _msg.ShowCardAsync("跳转到目标执行单：" + row.TargetNo, 2500);
        }

        /// <summary>
        /// 右键菜单：手工指定供应商。
        /// </summary>
        private async Task OnSetSupplierAsync(DependentDemandModel row)
        {
            if (row == null) return;

            var supplierCode = await _msg.ShowInputAsync("请输入供应商编码", "手动指定供应商");
            if (string.IsNullOrWhiteSpace(supplierCode)) return;

            var supplierName = await _msg.ShowInputAsync("请输入供应商名称", "手动指定供应商");

            if (_decomposeApi == null)
            {
                await _msg.ShowWarningAsync("需求池API未注入，无法保存供应商");
                return;
            }

            var saveRst = await _decomposeApi.UpdateSupplierAsync(row.Id, supplierCode.Trim(), (supplierName ?? string.Empty).Trim());
            if (!saveRst.Success)
            {
                await _msg.ShowWarningAsync("保存供应商失败：" + saveRst.Message);
                return;
            }

            row.SupplierCode = supplierCode.Trim();
            row.SupplierName = (supplierName ?? string.Empty).Trim();
            _msg.ShowCardAsync("已为 " + row.Sku + " 指定并保存供应商：" + row.SupplierCode + "-" + row.SupplierName, 3000);
        }

        /// <summary>
        /// 计算并刷新底部平均齐套率。
        /// </summary>
        private void RecalculateAvgKittingRate()
        {
            var total = DetailItems.Count;
            if (total <= 0)
            {
                AvgKittingRateText = "平均齐套率：0.00%";
                return;
            }

            var kitted = DetailItems.Count(x => x.IsKitted);
            var rate = (decimal)kitted / total;
            AvgKittingRateText = string.Format("平均齐套率：{0:P2}", rate);
        }

        /// <summary>
        /// 刷新左侧来源单任务列表（按需求日期倒序）。
        /// </summary>
        private void RefreshSourceTasks()
        {
            var groups = BridgeList.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.SourceNo))
                .GroupBy(x => x.SourceNo)
                .Select(g => new SourceNoTaskItem
                {
                    SourceNo = g.Key,
                    RequiredDate = g.Where(x => x.RequiredDate.HasValue).Select(x => x.RequiredDate.Value).DefaultIfEmpty(DateTime.MinValue).Max(),
                    PendingCount = g.Count(x => x.StatusFlag == RequirementStatusEnums.待处理),
                    TotalCount = g.Count()
                })
                .OrderByDescending(x => x.RequiredDate)
                .ToList();

            SourceTasks.Clear();
            foreach (var item in groups)
            {
                SourceTasks.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(SelectedSourceNo))
            {
                SelectedSourceTask = SourceTasks.FirstOrDefault(x => string.Equals(x.SourceNo, SelectedSourceNo, StringComparison.OrdinalIgnoreCase));
            }
            else if (SourceTasks.Count > 0)
            {
                SelectedSourceTask = SourceTasks[0];
            }
        }

        /// <summary>
        /// 根据左侧选中的来源单过滤右侧明细。
        /// </summary>
        private void ApplyDetailFilter()
        {
            DetailItems.Clear();

            IEnumerable<DependentDemandModel> rows = BridgeList.Items;
            if (!string.IsNullOrWhiteSpace(SelectedSourceNo))
            {
                rows = rows.Where(x => string.Equals(x.SourceNo, SelectedSourceNo, StringComparison.OrdinalIgnoreCase));
            }

            // 中文注释：BomLevel=0 作为标题层，不在右侧任务明细中默认展示。
            rows = rows.Where(x => x.BomLevel > 0);

            foreach (var row in rows)
            {
                // 中文注释：供应商预判，如果编码为空尝试用 SupplierName 兜底显示。
                if (string.IsNullOrWhiteSpace(row.SupplierCode) && !string.IsNullOrWhiteSpace(row.SupplierName))
                {
                    row.SupplierCode = row.SupplierName;
                }
                DetailItems.Add(row);
            }
        }

        /// <summary>
        /// 一键将当前来源单需求转采购：先本地预判供应商分组数量，再调用后端执行。
        /// </summary>
        private async Task ConvertSelectedSourceToPurchaseAsync()
        {
            if (_decomposeApi == null || string.IsNullOrWhiteSpace(SelectedSourceNo))
                return;

            var sourceNo = SelectedSourceNo.Trim();
            var purchaseRows = DetailItems
                .Where(x => string.Equals(x.SourceNo, sourceNo, StringComparison.OrdinalIgnoreCase)
                            && x.StatusFlag == RequirementStatusEnums.待处理
                            && IsPurchaseSupplyType(x.SupplyType))
                .ToList();

            if (purchaseRows.Count == 0)
            {
                await _msg.ShowWarningAsync("当前来源单没有可转采购的待处理行。");
                return;
            }

            var groupCount = purchaseRows
                .GroupBy(x => string.IsNullOrWhiteSpace(x.SupplierCode) ? "__MISSING__" : x.SupplierCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Count();

            var hasMissingSupplier = purchaseRows.Any(x => string.IsNullOrWhiteSpace(x.SupplierCode));
            var previewMsg = "当前销售单【" + sourceNo + "】预计按供应商生成 " + groupCount + " 份采购单。";
            if (hasMissingSupplier)
            {
                previewMsg += "\n存在未指定供应商的物料，请先补充后再转单。";
            }

            var confirm = await _msg.ShowConfirmAsync(previewMsg, "一键转采购确认");
            if (!confirm || hasMissingSupplier) return;

            using (BusyScope())
            {
                try
                {
                    var result = await _decomposeApi.ConvertSourceToPurchaseOrdersAsync(OrderTypeEnums.销售订单, sourceNo);
                    if (result.Success)
                    {
                        var poNos = result.Data ?? new List<string>();
                        StatusMessage = "转采购成功，生成 " + poNos.Count + " 份采购单：" + string.Join(", ", poNos);
                        _msg.ShowCardAsync(StatusMessage, 4000);
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "转采购失败：" + result.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "转采购异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        /// <summary>
        /// 判断是否采购类型（兼容中文枚举与英文标识）。
        /// </summary>
        private bool IsPurchaseSupplyType(string supplyType)
        {
            if (string.IsNullOrWhiteSpace(supplyType)) return false;
            return string.Equals(supplyType, "Purchase", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(supplyType, SupplyTypeEnums.直接采购, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(supplyType, SupplyTypeEnums.采购, StringComparison.OrdinalIgnoreCase);
        }

        private async Task TriggerDecomposeAsync()
        {
            if (_decomposeApi == null || string.IsNullOrWhiteSpace(SourceSoNoInput)) return;

            using (BusyScope())
            {
                try
                {
                    var rst = await _decomposeApi.DecomposeBySalesOrderAsync(SourceSoNoInput.Trim());
                    if (rst.Success)
                    {
                        StatusMessage = rst.Message ?? "拆解成功";
                        _msg.ShowCardAsync(StatusMessage);
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "拆解失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "拆解异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        private async Task BatchTriggerDecomposeAsync()
        {
            if (_decomposeApi == null || string.IsNullOrWhiteSpace(BatchSourceSoNosInput)) return;

            using (BusyScope())
            {
                try
                {
                    var soNos = BatchSourceSoNosInput
                        .Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (soNos.Count == 0)
                    {
                        await _msg.ShowWarningAsync("请输入有效销售单号");
                        return;
                    }

                    var rst = await _decomposeApi.DecomposeBySalesOrdersAsync(soNos);
                    if (rst.Success)
                    {
                        StatusMessage = rst.Message ?? "批量拆解成功";
                        _msg.ShowCardAsync(StatusMessage);
                        await LoadAsync();
                    }
                    else
                    {
                        StatusMessage = "批量拆解失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "批量拆解异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        /// <summary>
        /// 左侧来源单任务项。
        /// </summary>
        public class SourceNoTaskItem
        {
            public string SourceNo { get; set; }
            public DateTime RequiredDate { get; set; }
            public int PendingCount { get; set; }
            public int TotalCount { get; set; }
            public string DisplayText => SourceNo + "  待处理 " + PendingCount + "/" + TotalCount;
        }
    }
}
