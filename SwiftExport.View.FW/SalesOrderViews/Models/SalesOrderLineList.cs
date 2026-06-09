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

        public SalesOrderLineList()
        {
            this.ListStateChanged += OnListStateChanged;
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


        /// <summary>
        /// 中文注释：列表状态变化统一回调。
        /// 由基类在行属性变更、集合变更后触发，这里只做订单行特有逻辑。
        /// </summary>
        private void OnListStateChanged()
        {
            RecalculateTotals();
        }
        private readonly bool _isSuppressingTotals = false;
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

        }


  



    }

}
