using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.SupplierViews.Models
{
    public class SupplierModel : ViewModelBaseEx, IBaseModel
    {

        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;

        public ModelState State
        {
            get => _state;
            set { _state = value; RaisePropertyChanged(nameof(State)); }
        }

        // ================= 业务数据区 =================

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set { if (_supplierCode != value) { _supplierCode = value; MarkAsModified(); RaisePropertyChanged(nameof(SupplierCode)); } }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; MarkAsModified(); RaisePropertyChanged(nameof(Name)); } }
        }

        private string _docRelateNo;
        public string DocRelateNo
        {
            get => _docRelateNo;
            set { if (_docRelateNo != value) { _docRelateNo = value; MarkAsModified(); RaisePropertyChanged(nameof(DocRelateNo)); } }
        }

        private string _contactPerson;
        public string ContactPerson
        {
            get => _contactPerson;
            set { if (_contactPerson != value) { _contactPerson = value; MarkAsModified(); RaisePropertyChanged(nameof(ContactPerson)); } }
        }

        private string _contactPhone;
        public string ContactPhone
        {
            get => _contactPhone;
            set { if (_contactPhone != value) { _contactPhone = value; MarkAsModified(); RaisePropertyChanged(nameof(ContactPhone)); } }
        }

        private string _contactEmail;
        public string ContactEmail
        {
            get => _contactEmail;
            set { if (_contactEmail != value) { _contactEmail = value; MarkAsModified(); RaisePropertyChanged(nameof(ContactEmail)); } }
        }

        private string _address;
        public string Address
        {
            get => _address;
            set { if (_address != value) { _address = value; MarkAsModified(); RaisePropertyChanged(nameof(Address)); } }
        }

        private decimal _zheKou;
        public decimal ZheKou
        {
            get => _zheKou;
            set { if (_zheKou != value) { _zheKou = value; MarkAsModified(); RaisePropertyChanged(nameof(ZheKou)); } }
        }

        private string _currency = "CNY";
        public string Currency
        {
            get => _currency;
            set { if (_currency != value) { _currency = value; MarkAsModified(); RaisePropertyChanged(nameof(Currency)); } }
        }

        private decimal _taxRate;
        public decimal TaxRate
        {
            get => _taxRate;
            set { if (_taxRate != value) { _taxRate = value; MarkAsModified(); RaisePropertyChanged(nameof(TaxRate)); } }
        }

        private int _defaultLeadTime;
        public int DefaultLeadTime
        {
            get => _defaultLeadTime;
            set { if (_defaultLeadTime != value) { _defaultLeadTime = value; MarkAsModified(); RaisePropertyChanged(nameof(DefaultLeadTime)); } }
        }

        private string _paymentTerms;
        public string PaymentTerms
        {
            get => _paymentTerms;
            set { if (_paymentTerms != value) { _paymentTerms = value; MarkAsModified(); RaisePropertyChanged(nameof(PaymentTerms)); } }
        }

        private string _zheKouBeiZhu;
        public string ZheKouBeiZhu
        {
            get => _zheKouBeiZhu;
            set { if (_zheKouBeiZhu != value) { _zheKouBeiZhu = value; MarkAsModified(); RaisePropertyChanged(nameof(ZheKouBeiZhu)); } }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                RaisePropertyChanged(nameof(IsChecked));
            }
        }

        // ================= 验证逻辑区 =================

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(SupplierCode) &&
                     !string.IsNullOrWhiteSpace(Name) &&
                     !string.IsNullOrWhiteSpace(DocRelateNo);

            }
        }

        // ================= 辅助方法 =================

        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }
    }
}