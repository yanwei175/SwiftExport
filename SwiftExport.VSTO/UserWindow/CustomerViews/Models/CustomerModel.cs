using SwiftExport.VSTO.UserWindow.Mvvm;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.Models
{

    public class CustomerModel : ViewModelBase, IBaseModel
    {
        // 注意：根据你的 BaseDto，如果那里叫 Id，这里最好也叫 Id。我先用 ID 保持一致。
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

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; MarkAsModified(); OnPropertyChanged(nameof(Name)); } }
        }

        private string _address;
        public string Address
        {
            get => _address;
            set { if (_address != value) { _address = value; MarkAsModified(); OnPropertyChanged(nameof(Address)); } }
        }

        private string _phoneNumber;
        public string PhoneNumber
        {
            get => _phoneNumber;
            set { if (_phoneNumber != value) { _phoneNumber = value; MarkAsModified(); OnPropertyChanged(nameof(PhoneNumber)); } }
        }

        private string _contactPerson;
        public string ContactPerson
        {
            get => _contactPerson;
            set { if (_contactPerson != value) { _contactPerson = value; MarkAsModified(); OnPropertyChanged(nameof(ContactPerson)); } }
        }

        private string _docRelateNo;
        public string DocRelateNo
        {
            get => _docRelateNo;
            set { if (_docRelateNo != value) { _docRelateNo = value; MarkAsModified(); OnPropertyChanged(nameof(DocRelateNo)); } }
        }

        // ================= 文件夹路径部分 =================
        private string _labelFolderPath;
        public string LabelFolderPath
        {
            get => _labelFolderPath;
            set { if (_labelFolderPath != value) { _labelFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(LabelFolderPath)); } }
        }

        private string _manualFolderPath;
        public string ManualFolderPath
        {
            get => _manualFolderPath;
            set { if (_manualFolderPath != value) { _manualFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(ManualFolderPath)); } }
        }

        private string _drawingFolderPath;
        public string DrawingFolderPath
        {
            get => _drawingFolderPath;
            set { if (_drawingFolderPath != value) { _drawingFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(DrawingFolderPath)); } }
        }

        private string _linedrawingFolderPath;
        public string LinedrawingFolderPath
        {
            get => _linedrawingFolderPath;
            set { if (_linedrawingFolderPath != value) { _linedrawingFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(LinedrawingFolderPath)); } }
        }

        private string _barcodeFolderPath;
        public string BarcodeFolderPath
        {
            get => _barcodeFolderPath;
            set { if (_barcodeFolderPath != value) { _barcodeFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(BarcodeFolderPath)); } }
        }

        private string _specSheetFolderPath;
        public string SpecSheetFolderPath
        {
            get => _specSheetFolderPath;
            set { if (_specSheetFolderPath != value) { _specSheetFolderPath = value; MarkAsModified(); OnPropertyChanged(nameof(SpecSheetFolderPath)); } }
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



        public bool IsValid
        {
            get
            {
                // 如果所有必填项都没有错误信息，则代表合法
                return !string.IsNullOrWhiteSpace(CustomerCode) &&
                    !string.IsNullOrWhiteSpace(Name) &&
                    !string.IsNullOrWhiteSpace(DocRelateNo);
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