using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PackingListViews.Models
{
    public class PackingListLineList : BaseListModel<PackingListLineModel>
    {


        public PackingListLineList() 
        {
            this.ListStateChanged += OnPkItemChanged;
        }

        private void OnPkItemChanged()
        {
            RecalculateSummary();
        }
        private readonly bool _isSuppressingTotals = false;
        private void RecalculateSummary()
        {
            if (_isSuppressingTotals) return;

            // 中文注释：合计字段统一在 UI 线程刷新，避免跨线程更新绑定属性。
            SummaryQty = Items.Sum(x => x.Qty);
            SummaryGw_Kg = Items.Sum(x => x.TotalGw_KG);
            SummaryNw_Kg = Items.Sum(x => x.TotalNw_KG);
            SummaryCtns = Items.Sum(x => x.TotalCtns);
            SummaryCbm_m3 = Items.Sum(x => x.TotalCbm_m3);
        }



        private decimal _summaryQty;
        public decimal SummaryQty
        {
            get => _summaryQty;
            set => SetProperty(ref _summaryQty, value);
        }

        private decimal? _summaryNw_Kg;
        public decimal? SummaryNw_Kg
        {
            get => _summaryNw_Kg;
            set => SetProperty(ref _summaryNw_Kg, value);
        }
        private decimal? _summaryCtns;
        public decimal? SummaryCtns
        {
            get => _summaryCtns;
            set => SetProperty(ref _summaryCtns, value);
        }
        private decimal? _summaryGw_Kg;
        public decimal? SummaryGw_Kg
        {
            get => _summaryGw_Kg;
            set => SetProperty(ref _summaryGw_Kg, value);
        }
        private decimal? _summaryCbm_m3;
        public decimal? SummaryCbm_m3
        {
            get => _summaryCbm_m3;
            set => SetProperty(ref _summaryCbm_m3, value);
        }


















        }
}
