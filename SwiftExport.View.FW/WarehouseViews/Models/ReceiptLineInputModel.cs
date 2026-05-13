using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.WarehouseViews.Models
{
    /// <summary>
    /// 入库界面行模型（UI 输入模型）。
    /// </summary>
    public class ReceiptLineInputModel : ViewModelBaseEx
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        private string _poNo;
        public string PoNo
        {
            get => _poNo;
            set => SetProperty(ref _poNo, value);
        }

        private int? _poLineId;
        public int? PoLineId
        {
            get => _poLineId;
            set => SetProperty(ref _poLineId, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private decimal _poQty;
        public decimal PoQty
        {
            get => _poQty;
            set => SetProperty(ref _poQty, value);
        }

        private decimal _recQty;
        public decimal RecQty
        {
            get => _recQty;
            set => SetProperty(ref _recQty, value);
        }

        private decimal _readyQty;
        public decimal ReadyQty
        {
            get => _readyQty;
            set => SetProperty(ref _readyQty, value);
        }

        private decimal _receiveQty;
        public decimal ReceiveQty
        {
            get => _receiveQty;
            set => SetProperty(ref _receiveQty, value);
        }

        private string _unit;
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        public decimal MaxCanReceive => PoQty - RecQty;

        public bool IsValidReceiveQty => ReceiveQty > 0 && ReceiveQty <= MaxCanReceive;
    }
}
