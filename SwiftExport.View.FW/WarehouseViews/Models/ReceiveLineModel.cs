using Mapster;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.WarehouseViews.Models
{
    /// <summary>
    /// 入库单行 UI 模型。
    /// </summary>
    public class ReceiveLineModel : BaseLineModel
    {
        private string _receiveNo;
        public string ReceiveNo
        {
            get => _receiveNo;
            set => SetProperty(ref _receiveNo, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }
        /// <summary>
        /// 入库数量
        /// </summary>
        private decimal _qty;
        public decimal Qty
        {
            get => _qty;
            set => SetProperty(ref _qty, value);
        }

        private string _unit;
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        private Guid? _poLineId;
        public Guid? PoLineId
        {
            get => _poLineId;
            set => SetProperty(ref _poLineId, value);
        }

        private string _poNo;
        public string PoNo
        {
            get => _poNo;
            set => SetProperty(ref _poNo, value);
        }

        private decimal _poQty;
        /// <summary>
        /// 采购单数量
        /// </summary>
        public decimal PoQty
        {
            get => _poQty;
            set => SetProperty(ref _poQty, value);
        }

        private decimal _recQty;
        /// <summary>
        /// 已收数
        /// </summary>
        public decimal RecQty
        {
            get => _recQty;
            set => SetProperty(ref _recQty, value);
        }

        private decimal _readyQty;
        /// <summary>
        /// 完工数量
        /// </summary>
        public decimal ReadyQty
        {
            get => _readyQty;
            set => SetProperty(ref _readyQty, value);
        }

 /// <summary>
 /// 最大可收数量
 /// </summary>
        public decimal MaxCanReceive => PoQty - RecQty;

        public bool IsValidReceiveQty => Qty > 0 && Qty <= MaxCanReceive;


        public override string ValidateRow()
        {
            if (string.IsNullOrWhiteSpace(ReceiveNo)) return "入库单号不能为空";
            if (string.IsNullOrWhiteSpace(Sku)) return "SKU不能为空";
            if (Qty <= 0) return "入库数量必须大于0";
            return null;
        }
    }
}
