using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.DictViews.Models
{
    // 2. 右侧的具体枚举项 (Detail)
    public class DictDataModel : ViewModelBaseEx, IBaseModel
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State { get => _state; set { _state = value; RaisePropertyChanged(); } }

        public bool IsChecked { get; set; }

        private string _dictCode;
        public string DictCode { get => _dictCode; set { _dictCode = value; MarkAsModified(); RaisePropertyChanged(); } }

        private string _dictLabel;
        public string DictLabel { get => _dictLabel; set { _dictLabel = value; MarkAsModified(); RaisePropertyChanged(); } }

        private string _dictValue;
        public string DictValue { get => _dictValue; set { _dictValue = value; MarkAsModified(); RaisePropertyChanged(); } }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { _sortOrder = value; MarkAsModified(); RaisePropertyChanged(); } }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set { _isSystem = value; MarkAsModified(); RaisePropertyChanged(); } }

        // 极其纯粹的保存前校验
        public bool IsValid => !string.IsNullOrWhiteSpace(DictLabel) && !string.IsNullOrWhiteSpace(DictValue);

        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged) State = ModelState.Modified;
        }
    }
}
