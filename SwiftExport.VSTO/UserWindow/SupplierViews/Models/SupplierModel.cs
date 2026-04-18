using SwiftExport.VSTO.UserWindow.Mvvm;

namespace SwiftExport.VSTO.UserWindow.SupplierViews.Models
{
    public class SupplierModel : ViewModelBase, IBaseModel
    {

        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;

        public ModelState State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        // ================= 业务数据区 =================

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set { if (_supplierCode != value) { _supplierCode = value; MarkAsModified(); OnPropertyChanged(nameof(SupplierCode)); } }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; MarkAsModified(); OnPropertyChanged(nameof(Name)); } }
        }

        private string _docRelateNo;
        public string DocRelateNo
        {
            get => _docRelateNo;
            set { if (_docRelateNo != value) { _docRelateNo = value; MarkAsModified(); OnPropertyChanged(nameof(DocRelateNo)); } }
        }

        private decimal _zheKou;
        public decimal ZheKou
        {
            get => _zheKou;
            set { if (_zheKou != value) { _zheKou = value; MarkAsModified(); OnPropertyChanged(nameof(ZheKou)); } }
        }

        private string _zheKouBeiZhu;
        public string ZheKouBeiZhu
        {
            get => _zheKouBeiZhu;
            set { if (_zheKouBeiZhu != value) { _zheKouBeiZhu = value; MarkAsModified(); OnPropertyChanged(nameof(ZheKouBeiZhu)); } }
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