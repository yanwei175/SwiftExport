using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    public class SalesOrderLineManagerModel : BaseLineModel
    {

        private string _mainImage;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }
        private int _lineNumber;
        public int LineNumber
        {
            get => _lineNumber;
            set => SetProperty(ref _lineNumber, value);
        }
        private string _customerSku;
        public string CustomerSku
        {
            get => _customerSku;
            set => SetProperty(ref _customerSku, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _descriptionEn;
        public string DescriptionEn
        {
            get => _descriptionEn;
            set => SetProperty(ref _descriptionEn, value);
        }

        private string _colorNameEn;
        public string ColorNameEn
        {
            get => _colorNameEn;
            set => SetProperty(ref _colorNameEn, value);
        }
        private decimal _qty;
        public decimal Qty
        {
            get => _qty;
            set => SetProperty(ref _qty, value);
        }
        private decimal? _shippedQty;
        public decimal? ShippedQty
        {
            get => _shippedQty;
            set => SetProperty(ref _shippedQty, value);
        }
        private decimal? _unshippedQty;
        public decimal? UnshippedQty
        {
            get => _unshippedQty;
            set => SetProperty(ref _unshippedQty, value);
        }

        private decimal? _usdPrice;
        public decimal? UsdPrice
        {
            get => _usdPrice;
            set => SetProperty(ref _usdPrice, value);
        }
        private decimal? _cnyPrice;
        public decimal? CnyPrice
        {
            get => _cnyPrice;
            set => SetProperty(ref _cnyPrice, value);
        }

        private decimal? usdAmount;
        public decimal? UsdAmount
        {
            get => usdAmount;
            set => SetProperty(ref usdAmount, value);
        }

        private decimal? cnyAmount;
        public decimal? CnyAmount
        {
            get => cnyAmount;
            set => SetProperty(ref cnyAmount, value);
        }
        private string _subNote;
        public string SubNote
        {
            get => _subNote;
            set => SetProperty(ref _subNote, value);
        }


    }
}
