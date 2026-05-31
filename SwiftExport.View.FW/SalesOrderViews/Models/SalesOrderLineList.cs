using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 行集合模型（企业级 ERP 标准）
    /// 负责：
    /// 1. 自动行号管理（新增行使用负数行号）
    /// 2. 自动监听行变化（金额、Ctns、重量、体积）
    /// 3. 自动合计（总金额、总箱数、总重量、总体积）
    /// 4. 自动验证（所有行是否有效）
    /// 5. 自动 ChangeSet（一次性提交 Added/Modified/Deleted）
    /// 6. 脏数据检测（IsDirty）
    /// 7. 软删除（DeleteLine）
    /// </summary>
    public class SalesOrderLineList : BaseListModel<SalesOrderLineModel>
    {
        private SalesOrderHeaderModel _header;

        private int _tempLineNumber = -1; // 新增行使用负数行号

        public SalesOrderLineList() : this(null)
        {
        }

        public SalesOrderLineList(SalesOrderHeaderModel header)
        {
            // 中文注释：基类已经订阅了 CollectionChanged，这里只保留订单行自己的扩展逻辑。
            Items.CollectionChanged += OnCollectionChanged;
            // 中文注释：统一监听列表状态变化（行属性变化、勾选变化、增删变化），
            // 用于刷新汇总和选择状态代理字段。
            ListStateChanged += OnListStateChanged;
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
        public override void ReLoadRangeFromDtoList<SalesOrderLineDto>(List<SalesOrderLineDto> dtos, bool 抑制清理脏数据 = false)
        {
            using (DisabledStateScope())
            {
                _isSuppressingTotals = true; // 锁定计算
                try
                {
                    // 中文注释：直接复用基类批量加载逻辑，避免重复维护 _allItems 与事件订阅。
                    base.ReLoadRangeFromDtoList(dtos, 抑制清理脏数据);

                    // 中文注释：重置临时行号种子，保证之后新增行仍从负数开始分配。
                    _tempLineNumber = -1;
                }
                finally
                {
                    _isSuppressingTotals = false;
                    RecalculateTotals(); // 统一计算一次
                }
                RaiseAllState();//最终刷新状态，确保 UI 绑定的合计字段、校验状态、保存按钮等都能正确响应。
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
        private decimal _totalShippedQty;
        public decimal TotalShippedQty
        {
            get => _totalShippedQty;
            private set => SetProperty(ref _totalShippedQty, value);
        }
        private decimal _totalUnshippedQty;
        public decimal TotalUnshippedQty
        {
            get => _totalUnshippedQty;
            private set => SetProperty(ref _totalUnshippedQty, value);
        }

        #endregion

        // ============================================================
        // 2. 集合变更事件（新增/删除行）
        // ============================================================
        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 中文注释：新增行时仅处理业务扩展（自动分配负数行号）。
            // _allItems 维护、校验状态刷新、勾选状态刷新由基类统一处理。
            if (e.NewItems != null)
            {
                foreach (SalesOrderLineModel line in e.NewItems)
                {
                    // 新增行 → 自动分配负数行号
                    if (line.LineNumber == 0)
                        line.LineNumber = _tempLineNumber--;
                }
            }
        }

        /// <summary>
        /// 中文注释：列表状态变化统一回调。
        /// 由基类在行属性变更、集合变更后触发，这里只做订单行特有逻辑。
        /// </summary>
        private void OnListStateChanged()
        {
            RecalculateTotals();
            UpdateSelectionState();
        }

        /// <summary>
        /// 中文注释：当 Header 的币种或汇率变化时，需要重新同步主表汇总金额。
        /// 这样详情页中改了汇率，订单头显示的本币金额会立即刷新。
        /// </summary>
        private void OnHeaderPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SalesOrderHeaderModel.Currency) ||
                e.PropertyName == nameof(SalesOrderHeaderModel.ExchangeRate))
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

            // 中文注释：合计字段统一在 UI 线程刷新，避免跨线程更新绑定属性。
            TotalUsdAmount = Items.Sum(x => x.UsdAmount);
            TotalRmbAmount = Items.Sum(x => x.CnyAmount);
            TotalQty = Items.Sum(x => x.Qty);
            TotalCtns = Items.Sum(x => x.Ctns);
            TotalNw = Items.Sum(x => x.NwSubTotal);
            TotalGw = Items.Sum(x => x.GwSubTotal);
            TotalCbm = Items.Sum(x => x.CbmSubTotal);

            SyncHeaderSummary();
        }

        /// <summary>
        /// 中文注释：把当前行集合的汇总值同步回订单头。
        /// 如果当前场景没有 Header（例如订单管理页的从表只展示明细），则直接跳过。
        /// </summary>
        private void SyncHeaderSummary()
        {
            if (_header == null) return;
            var totalQty = Items.Sum(x => x.Qty);
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

            var currency = (_header.Currency ?? string.Empty).Trim().ToUpperInvariant();
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

            var currency = (_header.Currency ?? string.Empty).Trim().ToUpperInvariant();
            if (currency == "RMB" || currency == "CNY")
                return totalAmountOriginalCurrency;

            if (_header.ExchangeRate > 0)
                return Math.Round(totalAmountOriginalCurrency * _header.ExchangeRate, 2, MidpointRounding.AwayFromZero);

            return TotalRmbAmount;
        }

        /// <summary>
        /// 中文注释：选中项数量代理（兼容 ViewModel 现有绑定）。
        /// 实际值复用基类 CheckedCount。
        /// </summary>
        public int SelectedCount => CheckedCount;

        /// <summary>
        /// 中文注释：是否存在已选中项代理（兼容 ViewModel 现有绑定）。
        /// </summary>
        public bool HasSelectedItems => CheckedCount > 0;

        /// <summary>
        /// 中文注释：刷新选择状态相关属性通知。
        /// 当前不重复计算，直接复用基类结果并同步通知代理属性。
        /// </summary>
        public void UpdateSelectionState()
        {
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(CheckedCount));
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelectedItems));
        }

    }

}
