using GalaSoft.MvvmLight;
using System;

namespace SwiftExport.View.FW.SalesOrderViews.Demo
{
    /// <summary>
    /// Demo 明细行模型，业务项目真实页面仍可继续使用自己的 BaseLineModel。
    /// </summary>
    public class SalesOrderLineDemoModel : ObservableObject
    {
        private string _productSearchText = string.Empty;
        public string ProductSearchText
        {
            get => _productSearchText;
            set => Set(ref _productSearchText, value);
        }

        private ProductLookupDemoItem _selectedProductLookup;
        public ProductLookupDemoItem SelectedProductLookup
        {
            get => _selectedProductLookup;
            set => Set(ref _selectedProductLookup, value);
        }

        private Guid? _productId;
        public Guid? ProductId
        {
            get => _productId;
            set => Set(ref _productId, value);
        }

        private string _companyModel = string.Empty;
        public string CompanyModel
        {
            get => _companyModel;
            set => Set(ref _companyModel, value);
        }

        private string _descriptionEn = string.Empty;
        public string DescriptionEn
        {
            get => _descriptionEn;
            set => Set(ref _descriptionEn, value);
        }

        private decimal? _qty;
        public decimal? Qty
        {
            get => _qty;
            set
            {
                if (Set(ref _qty, value))
                {
                    RaisePropertyChanged(nameof(Amount));
                }
            }
        }

        private decimal? _unitPrice;
        public decimal? UnitPrice
        {
            get => _unitPrice;
            set
            {
                if (Set(ref _unitPrice, value))
                {
                    RaisePropertyChanged(nameof(Amount));
                }
            }
        }

        public decimal? Amount => Qty.HasValue && UnitPrice.HasValue ? Qty.Value * UnitPrice.Value : (decimal?)null;
    }
}
