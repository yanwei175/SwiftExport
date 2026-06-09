using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    public class SalesOrderLineManagerList : BaseListModel<SalesOrderLineManagerModel>
    {

        public SalesOrderLineManagerList()
        {
            this.ListStateChanged += OnLineItemChanged;
        }

        private void OnLineItemChanged()
        {
            RecalculateSummary();
        }
        private readonly bool _isSuppressingTotals = false;
        private void RecalculateSummary()
        {
            if (_isSuppressingTotals) return;

            // 中文注释：合计字段统一在 UI 线程刷新，避免跨线程更新绑定属性。
            SummaryQty = Items.Sum(x => x.Qty);
            SummaryShippedQty = Items.Sum(x => x.ShippedQty);
            SummaryUnshippedQty = Items.Sum(x => x.UnshippedQty);
            SummaryUsdAmount = Items.Sum(x => x.UsdAmount);
            SummaryCnyAmount = Items.Sum(x => x.CnyAmount);
        }



        private decimal _summaryQty;
        public decimal SummaryQty
        {
            get => _summaryQty;
            set => SetProperty(ref _summaryQty, value);
        }

        private decimal? _summaryShippedQty;
        public decimal? SummaryShippedQty
        {
            get => _summaryShippedQty;
            set => SetProperty(ref _summaryShippedQty, value);
        }
        private decimal? _summaryUnshippedQty;
        public decimal? SummaryUnshippedQty
        {
            get => _summaryUnshippedQty;
            set => SetProperty(ref _summaryUnshippedQty, value);
        }
        private decimal? _summaryUsdAmount;
        public decimal? SummaryUsdAmount
        {
            get => _summaryUsdAmount;
            set => SetProperty(ref _summaryUsdAmount, value);
        }
        private decimal? _summaryCnyAmount;
        public decimal? SummaryCnyAmount
        {
            get => _summaryCnyAmount;
            set => SetProperty(ref _summaryCnyAmount, value);
        }
    }
}
