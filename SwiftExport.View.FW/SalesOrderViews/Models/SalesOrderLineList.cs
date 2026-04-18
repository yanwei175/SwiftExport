using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 行集合模型（企业级 ERP 标准）
    /// 负责：
    /// 1. 自动行号管理（新增行使用负数行号）
    /// 2. 自动监听行变化（金额、箱数、重量、体积）
    /// 3. 自动合计（总金额、总箱数、总重量、总体积）
    /// 4. 自动验证（所有行是否有效）
    /// 5. 自动 ChangeSet（一次性提交 Added/Modified/Deleted）
    /// 6. 脏数据检测（IsDirty）
    /// 7. 软删除（DeleteLine）
    /// </summary>
    public class SalesOrderLineList : ViewModelBaseEx
    {
        private SalesOrderHeaderModel _header;
        private bool _isSyncingCheckState;

        /// <summary>
        /// UI 可见的行集合（用于 DataGrid 绑定）
        /// </summary>
        public ObservableCollection<SalesOrderLineModel> Items { get; } = new ObservableCollection<SalesOrderLineModel>();

        /// <summary>
        /// 内部所有行（包括 Deleted 行）
        /// 用于 ChangeSet、IsDirty 等业务逻辑
        /// </summary>
        private readonly List<SalesOrderLineModel> _allItems = new List<SalesOrderLineModel>();

        private int _tempLineNumber = -1; // 新增行使用负数行号

        public SalesOrderLineList() : this(null)
        {
        }

        public SalesOrderLineList(SalesOrderHeaderModel header)
        {
            Items.CollectionChanged += OnCollectionChanged;
            BindHeader(header);
        }

        /// <summary>
        /// 中文注释：为当前行集合动态绑定订单头。
        /// 详情页场景下，LineList 会把汇总值同步回 Header；
        /// 主从管理页场景下，也可以传 null，表示从表仅展示自身明细与汇总，不回写主表。
        /// </summary>
        public void BindHeader(SalesOrderHeaderModel header)
        {
            if (ReferenceEquals(_header, header))
                return;

            if (_header != null)
                _header.PropertyChanged -= OnHeaderPropertyChanged;

            _header = header;

            if (_header != null)
                _header.PropertyChanged += OnHeaderPropertyChanged;

            SyncHeaderSummary();
        }

        private bool _isSuppressingTotals = false;

        /// <summary>
        /// 从 DTO 加载行集合（用于加载订单）
        /// </summary>
        public void LoadFromDto(List<SalesOrderLineDto> dtos)
        {
            _isSuppressingTotals = true; // 锁定计算
            try
            {
                // 中文注释：重新加载订单明细时，需要同时清空 UI 集合与内部追踪集合，避免残留旧状态。
                Items.Clear();
                _allItems.Clear();

                // 中文注释：重置临时行号种子，保证之后新增行仍从负数开始分配。
                _tempLineNumber = -1;

                if (dtos == null || dtos.Count == 0)
                {
                    RecalculateTotals();
                    UpdateSelectionState();
                    RaisePropertyChanged(nameof(IsDirty));
                    RaisePropertyChanged(nameof(IsAllValid));
                    return;
                }

                foreach (var dto in dtos)
                {
                    // 中文注释：加载场景统一走行模型自己的 LoadFromDto，
                    // 由行模型内部负责 程序内部修改Scope、字段映射和状态清理。
                    var line = new SalesOrderLineModel();
                    line.LoadFromDto(dto);
                    Items.Add(line);
                }

                UpdateSelectionState();
                RaisePropertyChanged(nameof(IsDirty));
                RaisePropertyChanged(nameof(IsAllValid));
            }
            finally
            {
                _isSuppressingTotals = false;
                RecalculateTotals(); // 统一计算一次
            }
        }

        // ============================================================
        // 1. 合计字段（UI 可绑定）
        // ============================================================
        #region 合计字段
        private decimal _totalQty;
        public decimal TotalQty
        {
            get => _totalQty;
            private set => SetProperty(ref _totalQty, value);
        }

        private decimal _totalUsdAmount;
        public decimal TotalUsdAmount
        {
            get => _totalUsdAmount;
            private set => SetProperty(ref _totalUsdAmount, value);
        }

        private decimal _totalRmbAmount;
        public decimal TotalRmbAmount
        {
            get => _totalRmbAmount;
            private set => SetProperty(ref _totalRmbAmount, value);
        }

        private decimal _totalCtns;
        public decimal TotalCtns
        {
            get => _totalCtns;
            private set => SetProperty(ref _totalCtns, value);
        }

        private decimal _totalNw;
        public decimal TotalNw
        {
            get => _totalNw;
            private set => SetProperty(ref _totalNw, value);
        }

        private decimal _totalGw;
        public decimal TotalGw
        {
            get => _totalGw;
            private set => SetProperty(ref _totalGw, value);
        }

        private decimal _totalCbm;
        public decimal TotalCbm
        {
            get => _totalCbm;
            private set => SetProperty(ref _totalCbm, value);
        }

        #endregion
        // ============================================================
        // 1.1 勾选字段（UI 可绑定）
        // ============================================================
        #region 勾选字段

        private bool _isAllChecked;
        /// <summary>
        /// 中文注释：表头“全选/全不选”复选框绑定字段。
        /// 当用户勾选表头时，自动同步所有可见行的 IsChecked。
        /// </summary>
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (SetProperty(ref _isAllChecked, value))
                {
                    if (_isSyncingCheckState)
                        return;

                    SetAllChecked(value);
                }
            }
        }

        private int _selectedCount;
        /// <summary>
        /// 中文注释：当前已勾选的可见行数。
        /// </summary>
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value);
        }

        private bool _hasSelectedItems;
        /// <summary>
        /// 中文注释：是否存在至少一条已勾选的可见行。
        /// 可用于启用/禁用批量删除、批量导出等按钮。
        /// </summary>
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            private set => SetProperty(ref _hasSelectedItems, value);
        }

        #endregion
        // ============================================================
        // 2. 集合变更事件（新增/删除行）
        // ============================================================
        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 新增行
            if (e.NewItems != null)
            {
                foreach (SalesOrderLineModel line in e.NewItems)
                {
                    // 新增行 → 自动分配负数行号
                    if (line.行号 == 0)
                        line.行号 = _tempLineNumber--;

                    // 加入内部列表
                    if (!_allItems.Contains(line))
                        _allItems.Add(line);

                    // 监听行变化
                    line.PropertyChanged += OnLinePropertyChanged;
                }
            }

            // 删除行（UI 隐藏，但不从 _allItems 删除）
            if (e.OldItems != null)
            {
                foreach (SalesOrderLineModel line in e.OldItems)
                {
                    line.PropertyChanged -= OnLinePropertyChanged;
                }
            }

            RecalculateTotals();
            UpdateSelectionState();
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        // ============================================================
        // 3. 行变化事件（金额、箱数、重量、体积变化）
        // ============================================================
        private void OnLinePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            string[] watchedProps =
            {
                nameof(SalesOrderLineModel.数量),
                nameof(SalesOrderLineModel.美金小计),
                nameof(SalesOrderLineModel.人民币小计),
                nameof(SalesOrderLineModel.箱数),
                nameof(SalesOrderLineModel.净重小计),
                nameof(SalesOrderLineModel.毛重小计),
                nameof(SalesOrderLineModel.体积小计)
            };

            if (watchedProps.Contains(e.PropertyName))
            {
                RecalculateTotals();
            }

            // 中文注释：单行勾选状态变化时，需要同步表头全选状态与选中数量统计。
            if (e.PropertyName == nameof(BaseLineModel.IsChecked))
            {
                UpdateSelectionState();
            }

            // 中文注释：任意行字段变化都可能影响 IsValid/ValidateRow，
            // 因此这里统一刷新 IsAllValid，确保保存按钮和校验状态及时联动。
            RaisePropertyChanged(nameof(IsAllValid));

            // 行状态变化 → 脏数据变化
            if (e.PropertyName == nameof(SalesOrderLineModel.State))
            {
                RaisePropertyChanged(nameof(IsDirty));
            }
        }

        /// <summary>
        /// 中文注释：当 Header 的币种或汇率变化时，需要重新同步主表汇总金额。
        /// 这样详情页中改了汇率，订单头显示的本币金额会立即刷新。
        /// </summary>
        private void OnHeaderPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SalesOrderHeaderModel.货币) ||
                e.PropertyName == nameof(SalesOrderHeaderModel.汇率))
            {
                SyncHeaderSummary();
            }
        }

        // ============================================================
        // 4. 自动合计
        // ============================================================
        private void RecalculateTotals()
        {
            if (_isSuppressingTotals) return;

            DispatcherProvider.BeginInvoke(() =>
            {
                if (_isSuppressingTotals) return;
                TotalUsdAmount = Items.Sum(x => x.美金小计);
                TotalRmbAmount = Items.Sum(x => x.人民币小计);
                TotalCtns = Items.Sum(x => x.箱数);
                TotalNw = Items.Sum(x => x.净重小计);
                TotalGw = Items.Sum(x => x.毛重小计);
                TotalCbm = Items.Sum(x => x.体积小计);
                SyncHeaderSummary();
            });
            TotalUsdAmount = Items.Sum(x => x.美金小计);
            TotalRmbAmount = Items.Sum(x => x.人民币小计);
            TotalCtns = Items.Sum(x => x.箱数);
            TotalNw = Items.Sum(x => x.净重小计);
            TotalGw = Items.Sum(x => x.毛重小计);
            TotalCbm = Items.Sum(x => x.体积小计);

            SyncHeaderSummary();
        }

        /// <summary>
        /// 中文注释：把当前行集合的汇总值同步回订单头。
        /// 如果当前场景没有 Header（例如订单管理页的从表只展示明细），则直接跳过。
        /// </summary>
        private void SyncHeaderSummary()
        {
            if (_header == null) return;
            var totalQty = Items.Sum(x => x.数量);
            var lineCount = Items.Count;
            var totalAmountOriginalCurrency = ResolveOriginalCurrencyAmount();
            var totalAmountLocalCurrency = ResolveLocalCurrencyAmount(totalAmountOriginalCurrency);

            DispatcherProvider.BeginInvoke(() =>
            {
                // 再次判空，防止异步排队期间 Header 被释放
                if (_header == null) return;

                // 使用 HeaderModel 里已经写好的 UpdateSummaryFromLines
                // 该方法内部应该已经包裹了 using (程序内部修改Scope())
                _header.UpdateSummaryFromLines(totalQty, lineCount, totalAmountOriginalCurrency, totalAmountLocalCurrency);
            });
        }

        /// <summary>
        /// 中文注释：根据订单头币种决定“原币金额”取哪套明细金额。
        /// 当前行模型只有 USD / RMB 两套金额，因此这里按常见场景做兼容：
        /// 1. 币种为 RMB/CNY → 原币金额取 RMB 合计；
        /// 2. 其他情况默认取 USD 合计。
        /// </summary>
        private decimal ResolveOriginalCurrencyAmount()
        {
            if (_header == null)
                return TotalUsdAmount;

            var currency = (_header.货币 ?? string.Empty).Trim().ToUpperInvariant();
            if (currency == "RMB" || currency == "CNY")
                return TotalRmbAmount;

            return TotalUsdAmount;
        }

        /// <summary>
        /// 中文注释：计算本币金额。
        /// 1. 如果订单本身就是 RMB/CNY，则本币金额与原币金额一致；
        /// 2. 其他情况优先使用汇率换算；
        /// 3. 如果汇率未设置，则回退到明细中的 RMB 合计。
        /// </summary>
        private decimal ResolveLocalCurrencyAmount(decimal totalAmountOriginalCurrency)
        {
            if (_header == null)
                return TotalRmbAmount;

            var currency = (_header.货币 ?? string.Empty).Trim().ToUpperInvariant();
            if (currency == "RMB" || currency == "CNY")
                return totalAmountOriginalCurrency;

            if (_header.汇率 > 0)
                return Math.Round(totalAmountOriginalCurrency * _header.汇率, 2, MidpointRounding.AwayFromZero);

            return TotalRmbAmount;
        }

        // ============================================================
        // 5. 自动验证（所有行是否有效）
        // ============================================================
        public bool IsAllValid => _allItems
            .Where(x => x.State != ModelState.Deleted)
            .All(x => x.IsValid);

        public async Task<string> ValidateAllAsync()
        {
            foreach (var line in Items)
            {
                var asyncError = await line.ValidateAsync();
                if (!string.IsNullOrEmpty(asyncError))
                    return asyncError;
            }
            return null;
        }

        // ============================================================
        // 6. 脏数据检测（Dirty）
        // ============================================================
        public bool IsDirty =>
            _allItems.Any(x =>
                x.State == ModelState.Added ||
                x.State == ModelState.Modified ||
                x.State == ModelState.Deleted);
        // ============================================================
        // 6.1 勾选联动（全选 / 取消全选 / 反选）
        // ============================================================
        #region 勾选联动

        /// <summary>
        /// 中文注释：根据当前可见行的勾选状态，统一刷新表头全选状态、选中数量和是否存在选中项。
        /// 该方法既可在行复选框点击后调用，也可在集合变更后调用。
        /// </summary>
        public void UpdateSelectionState()
        {
            _isSyncingCheckState = true;
            try
            {
                SelectedCount = Items.Count(x => x.IsChecked);
                HasSelectedItems = SelectedCount > 0;
                IsAllChecked = Items.Count > 0 && Items.All(x => x.IsChecked);
            }
            finally
            {
                _isSyncingCheckState = false;
            }
        }

        /// <summary>
        /// 中文注释：统一设置当前所有可见行的勾选状态。
        /// true 表示全选，false 表示全不选。
        /// </summary>
        public void SetAllChecked(bool isChecked)
        {
            _isSyncingCheckState = true;
            try
            {
                foreach (var item in Items)
                {
                    item.IsChecked = isChecked;
                }
            }
            finally
            {
                _isSyncingCheckState = false;
            }

            UpdateSelectionState();
        }

        /// <summary>
        /// 中文注释：反选当前所有可见行。
        /// 已选中的变未选，未选中的变已选。
        /// </summary>
        public void InvertCheck()
        {
            _isSyncingCheckState = true;
            try
            {
                foreach (var item in Items)
                {
                    item.IsChecked = !item.IsChecked;
                }
            }
            finally
            {
                _isSyncingCheckState = false;
            }

            UpdateSelectionState();
        }

        /// <summary>
        /// 中文注释：批量删除当前所有已勾选的可见行。
        /// 删除规则与单行 DeleteLine 保持一致：
        /// 1. 未保存的新行直接从内部集合与 UI 集合移除；
        /// 2. 已持久化的行仅标记为 Deleted，并从 UI 集合隐藏，等待后端统一提交。
        /// </summary>
        public void DeleteChecked()
        {
            var checkedItems = Items.Where(x => x.IsChecked).ToList();
            if (checkedItems.Count == 0)
                return;

            foreach (var line in checkedItems)
            {
                DeleteLine(line);
            }

            UpdateSelectionState();
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        #endregion
        // ============================================================
        // 7. 删除行（软删除）
        // ============================================================
        public void DeleteLine(SalesOrderLineModel line)
        {
            if (line == null) return;

            if (line.Id == 0)
            {
                // 中文注释：新增未保存的行不需要进入 Deleted ChangeSet，直接从内部列表和 UI 一并移除即可。
                _allItems.Remove(line);
                Items.Remove(line);
                UpdateSelectionState();
                RaisePropertyChanged(nameof(IsDirty));
                RaisePropertyChanged(nameof(IsAllValid));
                return;
            }

            // 标记删除
            line.State = ModelState.Deleted;

            // UI 隐藏
            Items.Remove(line);

            UpdateSelectionState();
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        // ============================================================
        // 8. ChangeSet（一次性提交）
        // ============================================================
        public BatchRequestGeneric<SalesOrderLineModel> GetChangeSet()
        {
            return new BatchRequestGeneric<SalesOrderLineModel>
            {
                AddList = _allItems.Where(x => x.State == ModelState.Added).ToList(),
                UpdateList = _allItems.Where(x => x.State == ModelState.Modified).ToList(),
                DeleteIdList = _allItems.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList()
            };
        }
    }

}
