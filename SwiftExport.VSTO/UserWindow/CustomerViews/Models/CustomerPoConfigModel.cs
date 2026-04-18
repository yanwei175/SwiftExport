using SwiftExport.VSTO.UserWindow.Mvvm;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.Models
{
    public class CustomerPoConfigModel : ViewModelBase, IBaseModel
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set { if (_customerCode != value) { _customerCode = value; MarkAsModified(); OnPropertyChanged(nameof(CustomerCode)); } }
        }

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set { if (_supplierCode != value) { _supplierCode = value; MarkAsModified(); OnPropertyChanged(nameof(SupplierCode)); } }
        }

        private string _inletHoseType;
        public string InletHoseType
        {
            get => _inletHoseType;
            set { if (_inletHoseType != value) { _inletHoseType = value; MarkAsModified(); OnPropertyChanged(nameof(InletHoseType)); } }
        }

        private string _aeratorType;
        public string AeratorType
        {
            get => _aeratorType;
            set { if (_aeratorType != value) { _aeratorType = value; MarkAsModified(); OnPropertyChanged(nameof(AeratorType)); } }
        }

        private string _palletType;
        public string PalletType
        {
            get => _palletType;
            set { if (_palletType != value) { _palletType = value; MarkAsModified(); OnPropertyChanged(nameof(PalletType)); } }
        }

        private string _boxType;
        public string BoxType
        {
            get => _boxType;
            set { if (_boxType != value) { _boxType = value; MarkAsModified(); OnPropertyChanged(nameof(BoxType)); } }
        }

        private string _labelType;
        public string LabelType
        {
            get => _labelType;
            set { if (_labelType != value) { _labelType = value; MarkAsModified(); OnPropertyChanged(nameof(LabelType)); } }
        }

        private string _imType;
        public string IMType
        {
            get => _imType;
            set { if (_imType != value) { _imType = value; MarkAsModified(); OnPropertyChanged(nameof(IMType)); } }
        }

        private string _cartonType;
        public string CartonType
        {
            get => _cartonType;
            set { if (_cartonType != value) { _cartonType = value; MarkAsModified(); OnPropertyChanged(nameof(CartonType)); } }
        }

        private string _logoType;
        public string LogoType
        {
            get => _logoType;
            set { if (_logoType != value) { _logoType = value; MarkAsModified(); OnPropertyChanged(nameof(LogoType)); } }
        }

        private string _salesPerson;
        public string SalesPerson
        {
            get => _salesPerson;
            set { if (_salesPerson != value) { _salesPerson = value; MarkAsModified(); OnPropertyChanged(nameof(SalesPerson)); } }
        }

        private string _purchaser;
        public string Purchaser
        {
            get => _purchaser;
            set { if (_purchaser != value) { _purchaser = value; MarkAsModified(); OnPropertyChanged(nameof(Purchaser)); } }
        }

        private string _note;
        public string Note
        {
            get => _note;
            set { if (_note != value) { _note = value; MarkAsModified(); OnPropertyChanged(nameof(Note)); } }
        }


        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(CustomerCode) &&
                            !string.IsNullOrWhiteSpace(SupplierCode);
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }

        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }
    }
}
